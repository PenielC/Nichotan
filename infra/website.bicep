// The public marketing website (www.nichotan.com) on Azure Static Web Apps. See docs/deployment.md, "The public website".
//
// This is separate from main.bicep on purpose: the website has its own pipeline (.github/workflows/website.yml) and can be
// deployed, changed or rolled back without touching the app, the API or the database.
//
// The free plan is enough for this site (static files, a free managed HTTPS certificate, up to two custom domains).
// Custom domains are attached after DNS is set up (see the docs), not here, because Azure checks DNS before it accepts one.

targetScope = 'resourceGroup'

@description('Name of the Static Web App.')
param siteName string = 'nichotan-website'

@description('Static Web Apps is only available in a few regions (this is where its settings are kept; the pages themselves are served from Azure\'s edge worldwide).')
@allowed([
  'westeurope'
  'centralus'
  'eastus2'
  'westus2'
  'eastasia'
])
// East US 2, because Azure was not accepting new Static Web Apps in West Europe for this subscription (error RequestDisallowedByAzure).
// A Static Web App cannot be moved between regions, so keep this value the same once it exists.
param location string = 'eastus2'

resource site 'Microsoft.Web/staticSites@2023-12-01' = {
  name: siteName
  location: location
  tags: {
    app: 'nichotan'
    part: 'website'
  }
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {
    // Deployed from the pipeline with a token, not linked to a repository from Azure's side.
    allowConfigFileUpdates: true
    stagingEnvironmentPolicy: 'Enabled'
  }
}

@description('The address Azure gives the site before a custom domain is attached. Use it to check a deployment, and as the CNAME target for www.')
output defaultHostname string = site.properties.defaultHostname

output siteName string = site.name
