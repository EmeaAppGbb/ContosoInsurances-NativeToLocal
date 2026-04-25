// ============================================================================
// Contoso Insurance — Main Bicep Orchestration
// Deploys all Azure infrastructure for the Contoso Insurance application.
// Architecture: AKS-hosted .NET Aspire app with private networking.
// Only the Web frontend is internet-accessible via Application Gateway.
// ============================================================================

targetScope = 'subscription'

// ---------------------------------------------------------------------------
// Parameters
// ---------------------------------------------------------------------------

@minLength(1)
@maxLength(64)
@description('Name of the environment (e.g., dev, staging, prod)')
param environmentName string

@description('Primary location for all resources')
param location string

@description('Principal ID of the deploying user/service principal')
param principalId string

@secure()
@description('SQL administrator login name')
param sqlAdminLogin string = 'sqladmin'

@secure()
@description('SQL administrator password')
param sqlAdminPassword string

@description('AKS Kubernetes version')
param kubernetesVersion string = '1.30'

@description('AKS system node pool VM size')
param aksSystemNodeSize string = 'Standard_D4s_v3'

@description('AKS user node pool VM size')
param aksUserNodeSize string = 'Standard_D4s_v3'

@description('ACR SKU')
@allowed(['Standard', 'Premium'])
param acrSku string = 'Premium'

// ---------------------------------------------------------------------------
// Variables
// ---------------------------------------------------------------------------

var abbrs = loadJsonContent('./abbreviations.json')
var resourceToken = toLower(uniqueString(subscription().id, environmentName, location))
var tags = {
  'azd-env-name': environmentName
  application: 'contoso-insurance'
  environment: environmentName
  managedBy: 'bicep'
}

// ---------------------------------------------------------------------------
// Resource Group
// ---------------------------------------------------------------------------

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: '${abbrs.resourceGroup}${environmentName}'
  location: location
  tags: tags
}

// ---------------------------------------------------------------------------
// Modules
// ---------------------------------------------------------------------------

// Monitoring — deployed first as other modules reference Log Analytics
module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  scope: rg
  params: {
    location: location
    resourceToken: resourceToken
    tags: tags
  }
}

// Networking — VNet, subnets, NSGs, private DNS zones
module networking 'modules/networking.bicep' = {
  name: 'networking'
  scope: rg
  params: {
    location: location
    resourceToken: resourceToken
    tags: tags
  }
}

// Azure Container Registry
module acr 'modules/acr.bicep' = {
  name: 'acr'
  scope: rg
  params: {
    location: location
    resourceToken: resourceToken
    tags: tags
    sku: acrSku
    vnetId: networking.outputs.vnetId
    privateEndpointsSubnetId: networking.outputs.privateEndpointsSubnetId
    acrPrivateDnsZoneId: networking.outputs.acrPrivateDnsZoneId
  }
}

// AKS Cluster
module aks 'modules/aks.bicep' = {
  name: 'aks'
  scope: rg
  params: {
    location: location
    resourceToken: resourceToken
    tags: tags
    kubernetesVersion: kubernetesVersion
    systemNodeSize: aksSystemNodeSize
    userNodeSize: aksUserNodeSize
    aksSubnetId: networking.outputs.aksSubnetId
    logAnalyticsWorkspaceId: monitoring.outputs.logAnalyticsWorkspaceId
    acrId: acr.outputs.acrId
  }
}

// Azure SQL Database
module sql 'modules/sql.bicep' = {
  name: 'sql'
  scope: rg
  params: {
    location: location
    resourceToken: resourceToken
    tags: tags
    adminLogin: sqlAdminLogin
    adminPassword: sqlAdminPassword
    vnetId: networking.outputs.vnetId
    privateEndpointsSubnetId: networking.outputs.privateEndpointsSubnetId
    sqlPrivateDnsZoneId: networking.outputs.sqlPrivateDnsZoneId
  }
}

// Key Vault
module keyvault 'modules/keyvault.bicep' = {
  name: 'keyvault'
  scope: rg
  params: {
    location: location
    resourceToken: resourceToken
    tags: tags
    principalId: principalId
    aksKubeletIdentityObjectId: aks.outputs.kubeletIdentityObjectId
    vnetId: networking.outputs.vnetId
    privateEndpointsSubnetId: networking.outputs.privateEndpointsSubnetId
    kvPrivateDnsZoneId: networking.outputs.kvPrivateDnsZoneId
    sqlConnectionString: sql.outputs.connectionString
  }
}

// Application Gateway — the ONLY public-facing resource
module appgateway 'modules/appgateway.bicep' = {
  name: 'appgateway'
  scope: rg
  params: {
    location: location
    resourceToken: resourceToken
    tags: tags
    appGwSubnetId: networking.outputs.appGwSubnetId
  }
}

// ---------------------------------------------------------------------------
// Outputs (consumed by AZD and CI/CD)
// ---------------------------------------------------------------------------

output AZURE_RESOURCE_GROUP string = rg.name
output AZURE_AKS_CLUSTER_NAME string = aks.outputs.clusterName
output AZURE_ACR_NAME string = acr.outputs.acrName
output AZURE_ACR_LOGIN_SERVER string = acr.outputs.acrLoginServer
output AZURE_KEY_VAULT_NAME string = keyvault.outputs.keyVaultName
output AZURE_LOG_ANALYTICS_WORKSPACE_ID string = monitoring.outputs.logAnalyticsWorkspaceId
output AZURE_APPLICATION_INSIGHTS_CONNECTION_STRING string = monitoring.outputs.appInsightsConnectionString
output AZURE_SQL_SERVER_FQDN string = sql.outputs.serverFqdn
output AZURE_APP_GATEWAY_PUBLIC_IP string = appgateway.outputs.publicIpAddress
