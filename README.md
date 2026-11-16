# Nichotan

Student recruitment and visa management for Nicho Tan Consulting: candidates apply to study abroad (Canada and Germany first), and staff manage each application from registration to pre-departure.

**Status: milestone 1 (foundation).** This milestone delivers accounts, sign-in, roles, candidate self-registration, staff accounts, the candidate profile and the audit log. Applications, documents, visas, reports, messaging and AI briefings are later milestones (see [CLAUDE.md](CLAUDE.md) for the order). The design reference is `Nicho Tan Student Journey Showcase.png`. Architecture decisions are in [docs/adr](docs/adr).

## Layout

| Path | What it is |
| --- | --- |
| `backend/` | ASP.NET Core API (.NET 10), solution `Nichotan.slnx` |
| `backend/src/Nichotan.Domain` | Entities and rules, no dependencies |
| `backend/src/Nichotan.Application` | Use cases (Mediator commands and queries), validation, authorization checks |
| `backend/src/Nichotan.Infrastructure` | EF Core + PostgreSQL, ASP.NET Core Identity, email, S3 storage |
| `backend/src/Nichotan.Api` | HTTP endpoints, authentication, error handling, health checks |
| `backend/tests/Nichotan.Tests` | Unit and integration tests (real PostgreSQL) |
| `frontend/` | React + TypeScript web app (Vite, Tailwind CSS, shadcn/ui) |
| `docker-compose.yml` | Local PostgreSQL, S3-compatible storage and a mail catcher |

## Requirements

- .NET SDK 10.0.302 or later 10.0 SDK
- Node.js 24 and npm
- Docker Desktop
- EF Core tools, only to change the database schema: `dotnet tool install --global dotnet-ef`

## Run it locally

```bash
# 1. Services: PostgreSQL, SeaweedFS (S3) and Mailpit
docker compose up -d

# 2. API on http://localhost:5210 (applies migrations and creates the first administrator on start)
cd backend
dotnet run --project src/Nichotan.Api --launch-profile http

# 3. Web app on http://localhost:5175 (proxies /api to the API)
cd frontend
npm install
npm run dev
```

Local ports avoid the usual defaults, so Nichotan can run beside other projects:

| Service | Address |
| --- | --- |
| Web app | http://localhost:5175 |
| API | http://localhost:5210 (Swagger at `/swagger` in Development) |
| PostgreSQL | localhost:5438, database/user `nichotan` |
| S3 storage | http://localhost:9010, bucket `nichotan-documents` |
| Mailpit (all outgoing email) | http://localhost:8028 |

The local administrator, created on first start from `appsettings.Development.json`, is `admin@nichotan.local` with password `Local-Admin-2026`. These are local-only values. In Development, staff may skip the authenticator app (`Security:RequireMfaForStaff` is `false`); everywhere else it's required.

Candidates register at `/register`. Staff are invited by an administrator at **Staff accounts**; the invitation email (in Mailpit locally) links to `/accept-invitation`.

## Checks

```bash
# Backend: build and all tests (needs `docker compose up -d`; tests use a separate nichotan_test database)
cd backend
dotnet build
dotnet test

# Frontend
cd frontend
npm run typecheck
npm run lint
npm test
npm run build
```

## Database changes

```bash
cd backend
dotnet ef migrations add <Name> --project src/Nichotan.Infrastructure --startup-project src/Nichotan.Api --output-dir Persistence/Migrations
dotnet ef database update --project src/Nichotan.Infrastructure --startup-project src/Nichotan.Api
```

With `Bootstrap:MigrateOnStartup` on (local default), the API applies pending migrations when it starts.

## Configuration and secrets

Settings live in `backend/src/Nichotan.Api/appsettings.json` (no secrets) and are overridden per environment. [.env.example](.env.example) lists every setting as an environment variable name. Real values belong in the host's secret store, never in the repository. `appsettings.Development.json` holds local-only Docker values.

## Security model (summary)

- Access tokens last 15 minutes and are kept in memory by the web app, never in browser storage. A rotating refresh token lives in an HttpOnly, Secure, SameSite=Strict cookie limited to `/api/auth`. Replaying an old refresh token ends that whole sign-in everywhere.
- Staff need an authenticator app (TOTP) with recovery codes; candidates may turn it on.
- Every endpoint is authorized on the server. Candidates can reach only their own records. Opening a candidate's record, access refusals, sign-ins and account changes are written to an append-only audit log.
- Email links (verification, password reset, staff invitation) are single-use and stored only as hashes.
- Document storage is private. Files are reached only through short-lived signed URLs issued by the API.

## Deployment

Not set up yet: the hosting provider, data residency and recovery targets are open business decisions (see CLAUDE.md). Deployment and rollback steps will be documented here once those are decided.
