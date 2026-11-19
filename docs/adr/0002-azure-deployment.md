# ADR 0002: Deploying Nichotan on Azure

Status: accepted (2026-11-19). Decisions confirmed with the product owner: Container Apps, Azure Blob Storage, Azure Communication Services Email, Bicep + GitHub Actions.

## Context

Nichotan is a .NET 10 API (with a built-in reminder worker), a React single-page app, PostgreSQL, private document storage and outgoing email. ADR 0001 chose the stack and kept document storage and email behind interfaces. Production needs to run on Azure.

## Decision

```
Internet ──HTTPS──> web   (Container App, nginx: serves the SPA, forwards /api)
                      │
                      └──> api  (Container App, internal ingress only; 1 replica)
                              ├──> Azure Database for PostgreSQL – Flexible Server
                              ├──> Azure Blob Storage  (private container, managed identity)
                              └──> Azure Communication Services Email
```

| Concern | Choice | Why |
| --- | --- | --- |
| API + reminder worker | Azure Container Apps, one replica | Managed containers with HTTPS and secrets. The worker lives inside the API, so exactly one replica keeps reminders from running twice. The `SentReminders` claim table would also stop duplicates, so this is belt and braces. |
| Web app | Second Container App running nginx | The SPA talks to `/api` on its own origin so the sign-in cookie (`SameSite=Strict`, path `/api/auth`) stays same-site. nginx serves the app and forwards `/api` to the API; the API is never exposed publicly. |
| Database | PostgreSQL Flexible Server, Burstable | Same engine as development. 14-day point-in-time restore. |
| Documents | Azure Blob Storage, new `AzureBlobObjectStorage` behind `IObjectStorage` | Azure Blob is not S3-compatible, so a second implementation was written and the provider is chosen by `Storage:Provider`. Private container, no account keys (`allowSharedKeyAccess: false`), no public access, 14-day soft delete for deleted or overwritten files. Local development keeps SeaweedFS (S3); the Blob code is tested against the Azurite emulator. |
| Email | Azure Communication Services, new `AcsEmailSender` behind `IEmailSender`, chosen by `Email:Provider` | Stays in Azure. It sits inside the existing message-log decorator, so sends and failures are still recorded (BR-11.3). SMTP remains available. |
| Identity to Azure | One user-assigned managed identity | Pulls images (`AcrPull`) and reads/writes documents (`Storage Blob Data Contributor`, `Storage Blob Delegator`). No registry or storage credentials exist. |
| Secrets | Container Apps secrets, fed from GitHub secrets at deploy time | Simple. Key Vault is the upgrade path if secrets must be rotated centrally. |
| Infrastructure | Bicep (`infra/main.bicep`), applied in two passes | Pass 1 creates the registry and data services; the pipeline then pushes images; pass 2 creates the apps. |
| Pipeline | GitHub Actions (`.github/workflows/deploy.yml`), Azure sign-in by OIDC | Tests and Bicep compile on every pull request. Deploy runs on `main`, behind the `production` environment (which can require approval). Rollback = re-run with an earlier image tag. |
| Migrations | The API applies them on start (`Bootstrap:MigrateOnStartup`) | Safe with a single replica. If the API is ever scaled out, move migrations to a one-off job first. |

### Application changes this required

- `Storage:Provider` (`S3` default, or `AzureBlob`) and `AzureBlobObjectStorage`; `IStorageInitializer` replaces the direct S3 reference at start-up.
- `Email:Provider` (`Smtp` default, or `Acs`) and `AcsEmailSender`.
- `ForwardedHeaders:ProxyCount`. Behind Container Apps the real client address arrives in `X-Forwarded-For`, and ASP.NET ignores that header unless told how many proxies to trust. Without this every visitor would share one IP, breaking the per-IP sign-in and email rate limits. It is set to 2 (Container Apps ingress + nginx). Off by default, so local behaviour is unchanged.
- Dockerfiles for both apps; an Azurite service in `docker-compose.yml`.

## Trade-offs and known gaps (decide before go-live)

1. **Database network access.** The database accepts connections from any Azure service that has the credentials ("Allow Azure services"), over TLS, with a strong password. It is not on a private network. A private design (virtual network, delegated subnet, private DNS) is the stronger option and is worth doing before real student data is held; it needs the Container Apps environment moved into a virtual network. The connection uses `SSL Mode=Require` (encrypted, server certificate not verified); `VerifyFull` is stricter once confirmed against the image's trust store.
2. **No scaling out.** One API replica is a deliberate limit. Revisit with a job-based migration step and a leader-elected worker before scaling.
3. **Sender domain.** The generated `…azurecomm.net` address works at once but looks generic and may be filtered. Attach Nichotan's own domain (DNS records) before launch.
4. **Public address.** Until a custom domain is attached, the site is on a generated `*.azurecontainerapps.io` address.
5. **Malware scanning of uploads** is still open from earlier work. Microsoft Defender for Storage can scan blobs; enabling it is a subscription decision.
6. **Bootstrap administrator.** The first administrator comes from a GitHub secret. Remove that secret after the first sign-in.
7. **Bicep was not compiled locally** (no Azure CLI on the development machine); the pipeline's "Check infrastructure files" job compiles it on every push. Expect to fix small issues on the first real deployment.
8. **Region.** Defaults to the resource group's region. South Africa North is nearest to Zimbabwe, but confirm Container Apps, PostgreSQL Flexible Server and Communication Services are available there for your subscription before choosing it.
