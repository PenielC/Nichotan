# ADR 0001: Foundation architecture

- **Status:** Accepted
- **Date:** 2026-10-26
- **Scope:** Milestone 1 (authentication, roles, candidate profile, staff accounts, audit, local environment)

## Context

Nichotan manages candidates' study-abroad journeys: applications, documents, offers, payments evidence, visas and pre-departure. The system holds sensitive personal data (passports, finances, immigration history). CLAUDE.md proposes React + TypeScript, an ASP.NET Core modular monolith, PostgreSQL, private S3-compatible or Azure Blob storage and Docker Compose, but leaves build tools, ORM, identity provider and UI library open. Those choices were confirmed with the product owner before this milestone.

## Decisions

### 1. Backend: ASP.NET Core (.NET 10) modular monolith in clean layers

Four projects: `Domain` (entities, rules, no dependencies), `Application` (one command or query per use case, validated and authorized), `Infrastructure` (EF Core, Identity, email, storage) and `Api` (HTTP). Code inside each layer is grouped by capability (Identity, Staff, Candidates, Audit), so later modules (Applications, Documents, Visas) slot in beside them.

- **Mediator** (source-generated, MIT) dispatches commands and queries, with a pipeline step that runs **FluentValidation** validators. MediatR 13+ and FluentAssertions 8+ were avoided because of their commercial licences; tests use **AwesomeAssertions**.
- **EF Core + Npgsql** on **PostgreSQL 17**. Migrations live in Infrastructure.
- The solution uses the .NET 10 default `.slnx` format.

### 2. Accounts: ASP.NET Core Identity, with our own tokens

ASP.NET Core Identity (`AddIdentityCore`, Guid keys) handles password hashing, lockout (5 failures, 15 minutes), the authenticator key and recovery codes. Passwords need at least 10 characters with upper and lower case letters and a digit.

- **Access tokens** are JWTs valid for 15 minutes. Every request reloads the user, so a disabled account or a changed role takes effect at once rather than when the token expires.
- **Refresh tokens** are random, stored only as SHA-256 hashes, rotated on every use and grouped in families. Presenting an already-used token revokes the whole family (theft detection) and is audited. The token travels in an `HttpOnly; Secure; SameSite=Strict` cookie scoped to `/api/auth`. The refresh endpoint also requires an `X-Nichotan-Client` header, which a cross-site form can't send.
- **Email links** (verification 48 h, password reset 1 h, staff invitation 72 h) use our own single-use `AuthToken` rows, stored as hashes, rather than Identity's data-protection tokens. A new link replaces any older unused one, and redemption is easy to audit. Identity registers only the authenticator token provider.
- **Two-step verification**: authenticator apps (TOTP) with 10 single-use recovery codes. Sign-in with TOTP happens in two steps, joined by a 5-minute challenge token with its own audience. Staff sessions record whether a code was used (`amr=mfa`). Staff policies require it while `Security:RequireMfaForStaff` is on (the default; off only in local development). Staff without an authenticator can sign in but reach only the setup page. Administrators can reset a colleague's lost authenticator.
- Sign-in errors are deliberately vague, and "forgot password" always answers the same way, so neither reveals whether an email has an account.
- Email verification is soft: unverified candidates can sign in and are prompted to confirm. Whether verification should gate later steps (for example, submitting an application) is left for those milestones.

### 3. Authorization on the server, twice

Endpoint policies (`SignedIn`, `Candidate`, `CaseWorker`, `Administrator`) are the first gate. A `Guard` inside each handler is the second. The fallback policy requires a signed-in user, so a new endpoint is never public by accident. Candidate-facing endpoints never take a candidate id: they always resolve "me" from the token. Refusals (403) are written to the audit log.

Roles: Administrator and Admissions Counsellor (staff) and Candidate (the student). The BRD also named Visa Officer, Document Reviewer and Report Viewer; they were dropped once it was clear the application is used by administrators, counsellors and students only (see business-rules BR-2.1). Safeguards stop an administrator from removing their own admin role or disabling themselves, and there must always be at least one active administrator. The finer staff permissions and case-assignment policy remain an open business decision.

### 4. Audit log

`AuditEvents` is an append-only table: action, outcome (Success / Failure / Denied), actor, IP address, record reference and a small JSON details field. Enums are stored as strings, and the table has no foreign keys, so entries outlive deleted records. Details never include passwords, tokens or document contents. Opening a candidate's record is audited.

### 5. Object storage: S3-compatible, private, SeaweedFS locally

Documents will be stored through an `IObjectStorage` abstraction over the AWS S3 SDK, with metadata in PostgreSQL. Buckets are never public; downloads use short-lived presigned URLs. Locally, Docker Compose runs **SeaweedFS** in S3 mode. MinIO was the first choice, but its images could not be pulled (Docker Hub and Quay both refused access). Any S3-compatible service works in production; the provider follows the hosting decision. Uploaded files will be treated as untrusted input when the documents milestone adds uploads.

### 6. Frontend: Vite + React + TypeScript, Tailwind CSS v4, shadcn/ui

- **TanStack Query** for server state; **React Hook Form + Zod** for forms. Zod rules mirror the server's, but the server remains the authority, and its field errors are shown on the matching inputs.
- **shadcn/ui** components are copied into `src/components/ui` and themed with Nicho Tan's navy and green. Primary buttons use a darker green (`#357a1a`) so white text meets 4.5:1 contrast; the brighter logo green is used on navy only.
- The access token is held in memory only. On load, the app tries the refresh cookie once; a 401 triggers one shared refresh and a retry.
- In development, Vite proxies `/api` to the API, so the browser sees one origin, as it will in production behind a single domain. That keeps the strict same-site cookie working.
- After sign-in, `?next=` is followed only for same-site paths. Email links carry tokens in the URL, so the web app sets a `no-referrer` policy and the address is never passed to other sites.

### 7. Local environment and tests

Docker Compose runs PostgreSQL, SeaweedFS and Mailpit on non-default ports (5438, 9010, 1028/8028) to coexist with other local projects. Integration tests start the real API in memory (`WebApplicationFactory`) against a separate `nichotan_test` PostgreSQL database, with fake email and storage. Frontend tests use Vitest, Testing Library and jsdom.

## Consequences

- New capabilities follow the same pattern: a command or query plus validator in Application, a guard, audit entries for sensitive actions, an endpoint with a policy, and tests that cover both the allowed and the refused paths.
- Because UserManager saves changes itself, handlers that change both Identity and our own tables run inside a database transaction.
- Not decided here (open business decisions in CLAUDE.md): hosting and data residency, the production storage and email providers, payment handling, WhatsApp and AI providers, e-signature, retention and deletion rules, and the business time zone. Dates are stored in UTC and shown in each viewer's local time until a business time zone is chosen.
