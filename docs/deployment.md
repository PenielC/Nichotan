# Deploying Nichotan to Azure

How the pieces fit is in [ADR 0002](adr/0002-azure-deployment.md). This is the step-by-step.

You need: an Azure subscription where you are **Owner**, the [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) (`az`), and admin rights on the GitHub repository.

## One-time setup

### 1. Choose names and a region

```bash
az login
az account set --subscription "<subscription id or name>"
LOCATION=southafricanorth        # confirm the services in the ADR are available there; otherwise e.g. westeurope
RG=nichotan-prod
```

### 2. Enable the Azure services used

```bash
for p in Microsoft.App Microsoft.ContainerRegistry Microsoft.DBforPostgreSQL Microsoft.Communication \
         Microsoft.Storage Microsoft.OperationalInsights Microsoft.ManagedIdentity; do az provider register --namespace $p; done
```

### 3. Create the resource group

```bash
az group create --name $RG --location $LOCATION
```

### 4. Let GitHub deploy without a stored password (OIDC)

```bash
APP_ID=$(az ad app create --display-name nichotan-github-deploy --query appId -o tsv)
az ad sp create --id $APP_ID
SUB_ID=$(az account show --query id -o tsv)
# Owner on this one resource group only: the deployment creates role assignments (image pull, storage access), which Contributor cannot.
az role assignment create --assignee $APP_ID --role Owner --scope /subscriptions/$SUB_ID/resourceGroups/$RG
# Trust runs of the "production" environment in this repository
az ad app federated-credential create --id $APP_ID --parameters '{
  "name": "github-production",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:PenielC/Nichotan:environment:production",
  "audiences": ["api://AzureADTokenExchange"]
}'
echo "client id: $APP_ID   tenant: $(az account show --query tenantId -o tsv)   subscription: $SUB_ID"
```

### 5. Configure GitHub

Repository → Settings → **Environments** → create `production` (optionally add yourself as a required reviewer so each deployment waits for approval).

Under that environment (or repository) add:

| Kind | Name | Value |
| --- | --- | --- |
| Variable | `AZURE_CLIENT_ID` | the `client id` printed above |
| Variable | `AZURE_TENANT_ID` | the tenant id |
| Variable | `AZURE_SUBSCRIPTION_ID` | the subscription id |
| Variable | `AZURE_RESOURCE_GROUP` | `nichotan-prod` |
| Variable | `BOOTSTRAP_ADMIN_EMAIL` | the first administrator's email |
| Variable | `NOTIFY_EMAIL` | mailbox told about enquiries and applications (default `enquiries@nichotan.com`) |
| Variable | `EMAIL_DATA_LOCATION` | where email data is kept: `Africa`, `Europe`, `UK`, `United States`… (default `Europe`) |
| Variable | `WEB_BASE_URL` | normally leave empty (the address is taken from `CUSTOM_DOMAIN`) |
| Variable | `CUSTOM_DOMAIN`, `CUSTOM_DOMAIN_CERT_ID` | added later: see "Using Nichotan's own domain" |
| Secret | `POSTGRES_ADMIN_PASSWORD` | a long random password (`openssl rand -base64 32`) |
| Secret | `JWT_SECRET` | a different long random value (at least 32 characters) |
| Secret | `BOOTSTRAP_ADMIN_PASSWORD` | the first administrator's temporary password (see "After the first deployment") |

Keep `POSTGRES_ADMIN_PASSWORD` and `JWT_SECRET` stable: changing `JWT_SECRET` signs everyone out.

## Deploying

Push to `main` (or Actions → *Build, test and deploy to Azure* → Run workflow). The pipeline:

1. runs the backend tests, frontend checks and a compile of the Bicep file (on every pull request too);
2. provisions the registry, database, storage and email (first run takes about 10–15 minutes);
3. builds both images, tags them with the commit, and pushes them;
4. creates/updates the two apps, then checks the site answers through the web app to the API.

The site address is printed by the last step (`https://nichotan-web.<random>.<region>.azurecontainerapps.io`).

**Rollback:** Actions → Run workflow → give an earlier commit SHA in `rollback_tag`. Images are kept in the registry, so nothing is rebuilt. Database migrations are not undone: a release that changed the schema needs a forward fix instead.

## After the first deployment

1. Open the site and sign in as the bootstrap administrator. Staff must set up an authenticator app (MFA) at first sign-in.
2. **Delete the `BOOTSTRAP_ADMIN_PASSWORD` secret** in GitHub and run the workflow again, so the temporary password is no longer stored. Change the password first if you wish.
3. In the Messages page, check that a test email is sent (register a test student, or trigger a password reset). The sender is `DoNotReply@<generated>.azurecomm.net` until you attach your own domain.

## Using Nichotan's own domain

**Website** (for example `app.nichotan.com`). The domain has to be recorded in the deployment as well as in the portal, otherwise the next deployment removes it.

1. Push the version of `infra/main.bicep` and the workflow that know about `customDomain` (they do from 2026-11-19) and let it deploy once.
2. Find the two values DNS needs:
   ```powershell
   az containerapp env show -g nichotan-prod -n nichotan-env --query "{ip:properties.staticIp, verificationId:properties.customDomainConfiguration.customDomainVerificationId}" -o json
   ```
3. At the domain provider create: an **A** record `app` -> the `ip`, and a **TXT** record `asuid.app` -> the `verificationId`. (A **CNAME** `app` -> the web app's generated address works instead of the A record, and does not break if the IP ever changes.) Wait until it resolves: `nslookup app.nichotan.com`.
4. In the portal: Container App `nichotan-web` -> Custom domains -> Add custom domain -> your domain -> **Managed certificate** -> validate -> Add. Wait for the certificate to show *Succeeded*.
5. Get the certificate's resource id:
   ```powershell
   az containerapp env certificate list -g nichotan-prod -n nichotan-env --query "[].{name:name, id:id}" -o table
   ```
6. In GitHub (environment `production`) add variables `CUSTOM_DOMAIN` = `app.nichotan.com` and `CUSTOM_DOMAIN_CERT_ID` = that id. Re-run the workflow: from now on every deployment keeps the domain, and email links and the allowed origin use `https://app.nichotan.com`.

**Email.** In Azure: the Email Communication Service → Domains → Add custom domain → add the SPF, DKIM and verification DNS records shown → verify. Connect it to the Communication Service, then change the sender: set `Email__Acs__FromAddress` (in `infra/main.bicep`, `Email__Acs__FromAddress`) to an address on the verified domain, e.g. `no-reply@yourdomain`.

## Running it day to day

- **Logs:** Azure portal → the Container App → Log stream, or Log Analytics (`ContainerAppConsoleLogs_CL`). Failed emails are also in the app's Messages page.
- **Health:** the API's `/health/ready` checks database and storage; the platform restarts the container if `/health/live` fails.
- **Backups:** PostgreSQL keeps 14 days of point-in-time restore (portal → the server → Restore). Documents have 14-day soft delete (storage account → Data protection).
- **Cost:** the always-on items are the PostgreSQL server, the container registry, and one API replica plus one or two web replicas. Check *Cost Management* after the first week.
- **Delete everything:** `az group delete --name $RG` removes the whole environment and all data. There is no undo.

## Before real student data goes in

See "Trade-offs and known gaps" in the ADR: private networking for the database, malware scanning of uploads, your own sender domain, and confirming the QuestPDF licence and the Canada/Germany document lists.

## Trying the containers locally

```bash
docker compose up -d                      # Postgres, S3, Azurite, Mailpit
docker build -t nichotan-api backend
docker build -t nichotan-web frontend
```

Run the API with the environment variables in `.env.example`; run the web image with `-e API_HOST=<api host:port> -e API_SCHEME=http` to forward `/api` to a local API.
