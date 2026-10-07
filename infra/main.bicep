// Claims Module: Azure infrastructure (resource-group scope).
//   az group create -n rg-claims-demo -l westeurope
//   az deployment group create -g rg-claims-demo -f infra/main.bicep -p sqlAdminPassword=... jwtSigningKey=... fileSigningKey=...
// infra/deploy.sh wraps this and also deploys the code.

@description('Region for the API, database and storage.')
param location string = resourceGroup().location

@description('Static Web Apps are only available in a few regions.')
@allowed(['westus2', 'centralus', 'eastus2', 'westeurope', 'eastasia'])
param staticWebAppLocation string = 'centralus'

@description('Short prefix used in resource names (3-11 chars).')
@minLength(3)
@maxLength(11)
param namePrefix string = 'claims'

@description('App Service plan size. B1 is the smallest that supports Always On, which Hangfire needs (jobs run in the API process). F1 (free) works for a quick look but sleeps when idle, so background jobs only run while the app is awake.')
@allowed(['F1', 'B1', 'B2', 'S1'])
param appServicePlanSku string = 'B1'

@description('Azure SQL tier. Basic (5 DTU) is always-on, has no cold start, and is plenty for a demo.')
@allowed(['Basic', 'S0'])
param sqlSku string = 'Basic'

param sqlAdminLogin string = 'claimsadmin'

@secure()
param sqlAdminPassword string

@secure()
@minLength(32)
param jwtSigningKey string

@secure()
@minLength(16)
param fileSigningKey string

@description('Optional fixed App Service name; it becomes <name>.azurewebsites.net and must be unique across Azure. Empty = generated.')
param apiNameOverride string = ''

@description('Must match the organisation id used by the seeded reference data.')
param organisationId string = '0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001'

@description('Shows the role switcher and password-less test logins in the UI. Turn off for anything real.')
param enableRoleSwitcher bool = true

@description('Lets anyone open /hangfire. Handy for a demo, never for production.')
param allowAnonymousHangfireDashboard bool = false

var suffix = uniqueString(resourceGroup().id)
var sqlServerName = 'sql-${namePrefix}-${suffix}'
var storageName = toLower('st${namePrefix}${take(suffix, 10)}')
var planName = 'plan-${namePrefix}-${suffix}'
var apiName = empty(apiNameOverride) ? 'api-${namePrefix}-${suffix}' : apiNameOverride
var swaName = 'swa-${namePrefix}-${suffix}'
var databaseName = 'ClaimsDb'

// ------------------------------------------------------------------ database
resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: sqlServerName
  location: location
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    version: '12.0'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

// "Allow Azure services": lets the API (and GitHub-hosted runners, for migrations) reach the server.
resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource database 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: databaseName
  location: location
  sku: {
    name: sqlSku
    tier: sqlSku
  }
  properties: {
    maxSizeBytes: sqlSku == 'Basic' ? 2147483648 : 10737418240
    requestedBackupStorageRedundancy: 'Local'
  }
}

// ------------------------------------------------------------------ document storage
resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageName
  location: location
  kind: 'StorageV2'
  sku: { name: 'Standard_LRS' }
  properties: {
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource documentsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'claim-documents'
  properties: { publicAccess: 'None' }
}

// ------------------------------------------------------------------ frontend
resource staticWebApp 'Microsoft.Web/staticSites@2023-12-01' = {
  name: swaName
  location: staticWebAppLocation
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {}
}

// ------------------------------------------------------------------ API
resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: planName
  location: location
  kind: 'linux'
  sku: { name: appServicePlanSku }
  properties: { reserved: true }
}

var sqlConnectionString = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=${databaseName};User ID=${sqlAdminLogin};Password=${sqlAdminPassword};Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;'
var storageConnectionString = 'DefaultEndpointsProtocol=https;AccountName=${storage.name};AccountKey=${storage.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'

resource api 'Microsoft.Web/sites@2023-12-01' = {
  name: apiName
  location: location
  kind: 'app,linux'
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|9.0'
      alwaysOn: appServicePlanSku != 'F1' // the free tier does not support Always On
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      http20Enabled: true
      healthCheckPath: '/health'
      appSettings: [
        { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
        { name: 'ConnectionStrings__ClaimsDb', value: sqlConnectionString }
        { name: 'Jwt__SigningKey', value: jwtSigningKey }
        { name: 'Tenant__OrganisationId', value: organisationId }
        { name: 'Storage__Provider', value: 'AzureBlob' }
        { name: 'Storage__AzureBlob__ConnectionString', value: storageConnectionString }
        { name: 'Storage__AzureBlob__ContainerName', value: 'claim-documents' }
        { name: 'Storage__LocalFileSystem__SigningKey', value: fileSigningKey }
        { name: 'Storage__LocalFileSystem__PublicBaseUrl', value: 'https://${apiName}.azurewebsites.net' }
        { name: 'Cors__AllowedOrigins__0', value: 'https://${staticWebApp.properties.defaultHostname}' }
        { name: 'Hangfire__Enabled', value: 'true' }
        { name: 'Hangfire__AllowAnonymousDashboard', value: string(allowAnonymousHangfireDashboard) }
        { name: 'Auth__EnableRoleSwitcher', value: string(enableRoleSwitcher) }
        { name: 'Database__MigrateOnStartup', value: 'false' }
      ]
    }
  }
}

// Newer subscriptions disable basic-auth publishing by default; the GitHub Actions publish-profile deploy needs it.
resource scmBasicAuth 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2023-12-01' = {
  parent: api
  name: 'scm'
  properties: { allow: true }
}

// ------------------------------------------------------------------ outputs (no secrets)
output apiName string = api.name
output apiUrl string = 'https://${api.properties.defaultHostName}'
output staticWebAppName string = staticWebApp.name
output staticWebAppUrl string = 'https://${staticWebApp.properties.defaultHostname}'
output sqlServerName string = sqlServer.name
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output storageAccountName string = storage.name
