// Nichotan on Azure: one resource group holding everything. See docs/adr/0002-azure-deployment.md and docs/deployment.md.
//
//   Internet -> web (Container App, nginx: the SPA + /api proxy) -> api (Container App, internal only) -> PostgreSQL / Blob / ACS Email
//
// Deploy in two passes (the pipeline does this): first with deployApps=false to create the registry and data services,
// then, once the images are pushed to the registry, with deployApps=true.

targetScope = 'resourceGroup'

@description('Azure region for everything except Communication Services (which is global).')
param location string = resourceGroup().location

@description('Short name used as a prefix for resource names.')
@minLength(3)
@maxLength(12)
param namePrefix string = 'nichotan'

@description('Create the Container Apps. Leave false on the first pass, before the images exist in the registry.')
param deployApps bool = false

@description('Full image names, e.g. <registry>.azurecr.io/nichotan-api:<tag>. Only used when deployApps is true.')
param apiImage string = ''
param webImage string = ''

@description('Where Communication Services stores its data (e.g. Africa, Europe, United States, UK).')
param emailDataLocation string = 'Europe'

@description('Send email through an existing mailbox instead of Azure Communication Services: the SMTP server name from your email host (e.g. mail.nichotan.com). Empty = use Azure Communication Services.')
param smtpHost string = ''

@description('SMTP port. 587 (STARTTLS) is what the app supports; implicit-TLS port 465 is not.')
param smtpPort int = 587

@description('Mailbox sign-in name, usually the full address, e.g. no-reply@nichotan.com.')
param smtpUsername string = ''

@secure()
param smtpPassword string = ''

@description('Address shown as the sender of automated email (account links, notices to staff), e.g. DoNotReply@nichotan.com. Empty = the mailbox address.')
param smtpFromAddress string = ''

@description('Where replies to automated email go, e.g. info@nichotan.com, so a reply to a no-reply address is not lost. Empty = none.')
param smtpReplyTo string = ''

@description('Second sender for emails to students that invite a reply, e.g. info@nichotan.com. Empty = everything is sent from the automated address.')
param smtpInfoFromAddress string = ''

@description('Sign-in name of the Info mailbox, only if it has its own login. Empty = the automated mailbox login is used for both.')
param smtpInfoUsername string = ''

@secure()
param smtpInfoPassword string = ''

@description('Azure Communication Services only: second sender on the same verified domain (a MailFrom address), e.g. info@nichotan.com. Empty = automated address only.')
param emailInfoFromAddress string = ''

@description('Azure Communication Services only: where replies to automated email go. Empty = none.')
param emailReplyTo string = ''

var useSmtp = !empty(smtpHost)
// The Info mailbox has its own login only when BOTH name and password are given; the app ignores a half-given pair, and no placeholder is ever passed as a password.
var infoHasLogin = !empty(smtpInfoUsername) && !empty(smtpInfoPassword)

@description('Your own email sending domain, e.g. nichotan.com. Creates the domain in Azure so its DNS records can be read; leave empty to use the generated azurecomm.net sender.')
param emailCustomDomain string = ''

@description('Sender address on the VERIFIED custom domain, e.g. no-reply@nichotan.com. Empty = keep using the generated sender.')
param emailFromAddress string = ''

@description('PostgreSQL administrator login and password.')
param postgresAdminLogin string = 'nichotanadmin'
@secure()
param postgresAdminPassword string

@description('Secret used to sign access tokens: at least 32 random characters.')
@secure()
param jwtSecret string

@description('First administrator, created on first start if no administrator exists. Leave the password empty afterwards.')
param bootstrapAdminEmail string = ''
@secure()
param bootstrapAdminPassword string = ''
param bootstrapAdminFirstName string = 'Nichotan'
param bootstrapAdminLastName string = 'Administrator'

@description('Mailbox told about new enquiries and applications sent for review.')
param notifyEmail string = 'enquiries@nichotan.com'

@description('The public address people use, with no trailing slash. Leave empty to use the generated Azure address; set it once a custom domain is attached.')
param webBaseUrl string = ''

@description('Your own address for the site, e.g. app.nichotan.com (no https://). Leave empty until DNS is set up and the domain is added in the portal.')
param customDomain string = ''

@description('Resource id of the managed certificate for customDomain, created in the portal (see docs/deployment.md). Empty = domain added but not yet secured.')
param customDomainCertificateId string = ''

@description('Storage redundancy. Standard_LRS is cheapest; Standard_ZRS survives a datacentre failure.')
@allowed(['Standard_LRS', 'Standard_ZRS', 'Standard_GRS'])
param storageSku string = 'Standard_ZRS'

@description('PostgreSQL compute size and disk.')
param postgresSkuName string = 'Standard_B1ms'
param postgresStorageGb int = 32

var unique = uniqueString(resourceGroup().id)
var tags = { app: 'nichotan' }

var acrName = toLower('acr${namePrefix}${unique}')
var storageName = toLower('st${take(namePrefix, 8)}${unique}')
var postgresName = toLower('${namePrefix}-pg-${unique}')
var apiAppName = '${namePrefix}-api'
var webAppName = '${namePrefix}-web'
var documentsContainer = 'nichotan-documents'

// ---------------------------------------------------------------- monitoring
resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${namePrefix}-logs'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

// ---------------------------------------------------------------- identity (the apps use it to pull images and reach Blob Storage)
resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${namePrefix}-id'
  location: location
  tags: tags
}

// ---------------------------------------------------------------- container registry
resource acr 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: acrName
  location: location
  tags: tags
  sku: { name: 'Basic' }
  properties: { adminUserEnabled: false }
}

var acrPullRole = '7f951dda-4ed3-4680-a7ca-43fe172d538d'
resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(acr.id, identity.id, acrPullRole)
  scope: acr
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRole)
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// ---------------------------------------------------------------- document storage (private; no account keys, no public access)
resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageName
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: { name: storageSku }
  properties: {
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    defaultToOAuthAuthentication: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
  properties: {
    // A deleted or overwritten document can be recovered for two weeks.
    deleteRetentionPolicy: { enabled: true, days: 14 }
    containerDeleteRetentionPolicy: { enabled: true, days: 14 }
  }
}

resource documents 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: documentsContainer
  properties: { publicAccess: 'None' }
}

var blobDataContributorRole = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
var blobDelegatorRole = 'db58b8e5-c6ad-4a2a-8342-4190687cbf4a'
resource blobContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, identity.id, blobDataContributorRole)
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', blobDataContributorRole)
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}
resource blobDelegator 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, identity.id, blobDelegatorRole)
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', blobDelegatorRole)
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// ---------------------------------------------------------------- database
resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: postgresName
  location: location
  tags: tags
  sku: { name: postgresSkuName, tier: 'Burstable' }
  properties: {
    version: '17'
    administratorLogin: postgresAdminLogin
    administratorLoginPassword: postgresAdminPassword
    storage: { storageSizeGB: postgresStorageGb }
    backup: { backupRetentionDays: 14, geoRedundantBackup: 'Disabled' }
    highAvailability: { mode: 'Disabled' }
    // Reachable from Azure services only (see the firewall rule below); the connection is always TLS.
    network: { publicNetworkAccess: 'Enabled' }
  }
}

resource postgresDb 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: postgres
  name: 'nichotan'
  properties: { charset: 'UTF8', collation: 'en_US.utf8' }
}

// "0.0.0.0" is Azure's special "allow access from Azure services" rule. It is not a private network: see ADR 0002 for the trade-off.
resource postgresAzureServices 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2024-08-01' = {
  parent: postgres
  name: 'AllowAzureServices'
  properties: { startIpAddress: '0.0.0.0', endIpAddress: '0.0.0.0' }
}

// ---------------------------------------------------------------- email (Azure Communication Services)
resource emailService 'Microsoft.Communication/emailServices@2023-04-01' = {
  name: '${namePrefix}-email-${unique}'
  location: 'global'
  tags: tags
  properties: { dataLocation: emailDataLocation }
}

// An Azure-managed sender domain works immediately (…azurecomm.net). Replace it with your own domain before go-live: see docs/deployment.md.
resource emailDomain 'Microsoft.Communication/emailServices/domains@2023-04-01' = {
  parent: emailService
  name: 'AzureManagedDomain'
  location: 'global'
  tags: tags
  properties: { domainManagement: 'AzureManaged' }
}

// Your own sender domain. Declared here so a redeploy keeps it. The domain still has to be verified (DNS records) before it can be linked:
// set emailCustomDomain first, add the records, verify in the portal, then set emailFromAddress.
resource emailCustomDomainRes 'Microsoft.Communication/emailServices/domains@2023-04-01' = if (!empty(emailCustomDomain)) {
  parent: emailService
  name: emailCustomDomain
  location: 'global'
  tags: tags
  properties: { domainManagement: 'CustomerManaged' }
}

var linkCustomDomain = !empty(emailCustomDomain) && !empty(emailFromAddress)

resource communication 'Microsoft.Communication/communicationServices@2023-04-01' = {
  name: '${namePrefix}-comm-${unique}'
  location: 'global'
  tags: tags
  properties: {
    dataLocation: emailDataLocation
    linkedDomains: linkCustomDomain ? [emailDomain.id, emailCustomDomainRes.id] : [emailDomain.id]
  }
}

// ---------------------------------------------------------------- container apps
resource appEnv 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${namePrefix}-env'
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

// Addresses are derived from the environment's domain so the two apps can refer to each other without a circular dependency.
var apiHost = '${apiAppName}.internal.${appEnv.properties.defaultDomain}'
var webHost = '${webAppName}.${appEnv.properties.defaultDomain}'
var publicUrl = !empty(webBaseUrl) ? webBaseUrl : (empty(customDomain) ? 'https://${webHost}' : 'https://${customDomain}')

var registryConfig = [
  {
    server: acr.properties.loginServer
    identity: identity.id
  }
]

var apiSecrets = concat(
  [
    { name: 'db-connection', value: 'Host=${postgres.properties.fullyQualifiedDomainName};Port=5432;Database=nichotan;Username=${postgresAdminLogin};Password=${postgresAdminPassword};SSL Mode=Require' }
    { name: 'jwt-secret', value: jwtSecret }
    { name: 'acs-connection', value: communication.listKeys().primaryConnectionString }
    // Container Apps needs a non-empty value for every secret, so a placeholder is used when SMTP is not in play.
    { name: 'smtp-password', value: empty(smtpPassword) ? 'not-used' : smtpPassword }
  ],
  empty(bootstrapAdminPassword) ? [] : [{ name: 'bootstrap-admin-password', value: bootstrapAdminPassword }],
  infoHasLogin ? [{ name: 'smtp-info-password', value: smtpInfoPassword }] : []
)

var apiEnv = concat(
  [
    { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
    { name: 'AZURE_CLIENT_ID', value: identity.properties.clientId }
    { name: 'ConnectionStrings__Database', secretRef: 'db-connection' }
    { name: 'Jwt__Secret', secretRef: 'jwt-secret' }
    { name: 'Jwt__Issuer', value: 'Nichotan' }
    { name: 'Jwt__Audience', value: 'Nichotan' }
    { name: 'App__WebBaseUrl', value: publicUrl }
    { name: 'Cors__AllowedOrigins__0', value: publicUrl }
    { name: 'Security__RequireMfaForStaff', value: 'true' }
    { name: 'Admissions__NotifyEmail', value: notifyEmail }
    { name: 'Enquiries__NotifyEmail', value: notifyEmail }
    { name: 'Reminders__Enabled', value: 'true' }
    { name: 'Reminders__TimeZone', value: 'UTC' }
    { name: 'Email__Provider', value: useSmtp ? 'Smtp' : 'Acs' }
    { name: 'Email__Acs__ConnectionString', secretRef: 'acs-connection' }
    { name: 'Email__Acs__FromAddress', value: linkCustomDomain ? emailFromAddress : 'DoNotReply@${emailDomain.properties.mailFromSenderDomain}' }
    { name: 'Email__Acs__InfoFromAddress', value: emailInfoFromAddress }
    { name: 'Email__Acs__ReplyTo', value: emailReplyTo }
    { name: 'Smtp__ReplyTo', value: smtpReplyTo }
    { name: 'Smtp__Info__FromAddress', value: smtpInfoFromAddress }
    { name: 'Smtp__Host', value: smtpHost }
    { name: 'Smtp__Port', value: string(smtpPort) }
    { name: 'Smtp__EnableSsl', value: 'true' }
    { name: 'Smtp__Username', value: smtpUsername }
    { name: 'Smtp__Password', secretRef: 'smtp-password' }
    { name: 'Smtp__FromAddress', value: empty(smtpFromAddress) ? smtpUsername : smtpFromAddress }
    { name: 'Smtp__FromName', value: 'Nichotan' }
    { name: 'Storage__Provider', value: 'AzureBlob' }
    { name: 'Storage__Bucket', value: documentsContainer }
    { name: 'Storage__AzureBlob__ServiceUri', value: storage.properties.primaryEndpoints.blob }
    // The web app's nginx and Container Apps' own ingress are the two proxies in front of the API.
    { name: 'ForwardedHeaders__ProxyCount', value: '2' }
    { name: 'Bootstrap__MigrateOnStartup', value: 'true' }
    { name: 'Bootstrap__AdminEmail', value: bootstrapAdminEmail }
    { name: 'Bootstrap__AdminFirstName', value: bootstrapAdminFirstName }
    { name: 'Bootstrap__AdminLastName', value: bootstrapAdminLastName }
  ],
  empty(bootstrapAdminPassword) ? [] : [{ name: 'Bootstrap__AdminPassword', secretRef: 'bootstrap-admin-password' }],
  infoHasLogin ? [
    { name: 'Smtp__Info__Username', value: smtpInfoUsername }
    { name: 'Smtp__Info__Password', secretRef: 'smtp-info-password' }
  ] : []
)

resource apiApp 'Microsoft.App/containerApps@2024-03-01' = if (deployApps) {
  name: apiAppName
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identity.id}': {} }
  }
  properties: {
    managedEnvironmentId: appEnv.id
    configuration: {
      activeRevisionsMode: 'Single'
      // Internal only: the public site reaches it through the web app's /api proxy.
      ingress: { external: false, targetPort: 8080, transport: 'auto', allowInsecure: false }
      registries: registryConfig
      secrets: apiSecrets
    }
    template: {
      containers: [
        {
          name: 'api'
          image: apiImage
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: apiEnv
          probes: [
            { type: 'Startup', httpGet: { path: '/health/live', port: 8080 }, periodSeconds: 5, failureThreshold: 30 }
            { type: 'Liveness', httpGet: { path: '/health/live', port: 8080 }, periodSeconds: 30 }
            { type: 'Readiness', httpGet: { path: '/health/ready', port: 8080 }, periodSeconds: 15 }
          ]
        }
      ]
      // Exactly one replica: the reminder worker runs inside the API, and the first start applies database migrations.
      scale: { minReplicas: 1, maxReplicas: 1 }
    }
  }
  dependsOn: [acrPull, blobContributor, blobDelegator, postgresDb, postgresAzureServices, documents]
}

resource webApp 'Microsoft.App/containerApps@2024-03-01' = if (deployApps) {
  name: webAppName
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identity.id}': {} }
  }
  properties: {
    managedEnvironmentId: appEnv.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
        // Must be repeated on every deployment, or the domain would be removed from the app.
        customDomains: empty(customDomain) ? [] : [
          {
            name: customDomain
            bindingType: empty(customDomainCertificateId) ? 'Disabled' : 'SniEnabled'
            certificateId: empty(customDomainCertificateId) ? null : customDomainCertificateId
          }
        ]
      }
      registries: registryConfig
    }
    template: {
      containers: [
        {
          name: 'web'
          image: webImage
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: [
            { name: 'API_HOST', value: apiHost }
            { name: 'API_SCHEME', value: 'https' }
          ]
          probes: [
            { type: 'Liveness', httpGet: { path: '/healthz', port: 8080 }, periodSeconds: 30 }
            { type: 'Readiness', httpGet: { path: '/healthz', port: 8080 }, periodSeconds: 10 }
          ]
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 2 }
    }
  }
  dependsOn: [acrPull]
}

output registryName string = acr.name
output registryLoginServer string = acr.properties.loginServer
output webUrl string = publicUrl
output generatedWebUrl string = 'https://${webHost}'
output postgresServer string = postgres.properties.fullyQualifiedDomainName
output storageAccount string = storage.name
output emailSenderDomain string = emailDomain.properties.mailFromSenderDomain
output apiAppName string = apiAppName
output webAppName string = webAppName
