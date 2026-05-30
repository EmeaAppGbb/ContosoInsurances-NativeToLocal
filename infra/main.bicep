// ============================================================================
// Contoso Insurance — Main Bicep Orchestration
// Deploys all Azure infrastructure for the Contoso Insurance application.
// Architecture: AKS-hosted .NET Aspire app with private networking.
// Only the Web frontend is internet-accessible via AGC (Application Gateway for Containers).
//
// MIGRATION (April 2026):
//   - App Gateway WAF v2 → AGC (AGIC retired March 2026)
//   - K8s 1.30 → 1.35 (1.30 deprecated March 2026)
//   - Ingress API → Gateway API (Gateway + HTTPRoute CRDs)
//   - omsagent addon → Azure Monitor managed monitoring addon
// ============================================================================

targetScope = 'subscription'

// ---------------------------------------------------------------------------
// Parameters
// ---------------------------------------------------------------------------

@minLength(1)
@maxLength(64)
@description('Name of the environment (e.g., dev, staging, prod)')
param environmentName string

@description('Primary location for all resources. Sovereign branch defaults to Germany West Central for German data residency and AGC availability.')
param location string = 'germanywestcentral'

@description('Principal ID of the deploying user/service principal')
param principalId string

@description('Display name for the SQL Entra ID administrator')
param sqlEntraAdminDisplayName string = 'AZD Deployer'

@description('AKS Kubernetes version')
// MIGRATION: K8s 1.30 deprecated March 2026; updated to 1.35 (latest GA)
param kubernetesVersion string = '1.35'

@description('AKS system node pool VM size')
param aksSystemNodeSize string = 'Standard_D4s_v3'

@description('AKS user node pool VM size')
param aksUserNodeSize string = 'Standard_D4s_v3'

@description('ACR SKU')
@allowed(['Standard', 'Premium'])
param acrSku string = 'Premium'

@description('Whether to deploy AKS as a private cluster. Defaults to false for the learning lab scenario.')
param enablePrivateCluster bool = false

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
    enablePrivateCluster: enablePrivateCluster
  }
}

// Azure SQL Database
// Sovereign note: German regions do not currently support Azure SQL Database
// serverless, so the sql module automatically uses provisioned General Purpose
// compute there while keeping the same private-endpoint + managed-identity pattern.
module sql 'modules/sql.bicep' = {
  name: 'sql'
  scope: rg
  params: {
    location: location
    resourceToken: resourceToken
    tags: tags
    entraAdminObjectId: aks.outputs.kubeletIdentityObjectId
    entraAdminDisplayName: 'aks-${resourceToken}-kubelet'
    podIdentityClientId: aks.outputs.kubeletIdentityClientId
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

// Application Gateway for Containers (AGC) — the ONLY public-facing resource
// MIGRATION: Replaced App Gateway WAF v2 with AGC. AGIC was retired March 2026.
// Sovereign note: this branch targets Germany West Central by default because it
// supports AGC. Germany North should only be used after re-validating AGC regional
// availability or introducing an alternative ingress implementation.
// AGC uses Gateway API (not Ingress API) and is managed by the ALB Controller
// addon running inside AKS. Routing is defined in K8s manifests, not Bicep.
module appgateway 'modules/appgateway.bicep' = {
  name: 'appgateway'
  scope: rg
  params: {
    location: location
    resourceToken: resourceToken
    tags: tags
    agcSubnetId: networking.outputs.agcSubnetId
  }
}

// ---------------------------------------------------------------------------
// Outputs (consumed by AZD and CI/CD)
// ---------------------------------------------------------------------------

output AZURE_RESOURCE_GROUP string = rg.name
output AZURE_AKS_CLUSTER_NAME string = aks.outputs.clusterName
output AZURE_CONTAINER_REGISTRY_MANAGED_IDENTITY_ID string = aks.outputs.kubeletIdentityClientId
output AZURE_ACR_NAME string = acr.outputs.acrName
output AZURE_ACR_LOGIN_SERVER string = acr.outputs.acrLoginServer
output AZURE_CONTAINER_REGISTRY_ENDPOINT string = acr.outputs.acrLoginServer
output AZURE_KEY_VAULT_NAME string = keyvault.outputs.keyVaultName
output AZURE_LOG_ANALYTICS_WORKSPACE_ID string = monitoring.outputs.logAnalyticsWorkspaceId
output AZURE_APPLICATION_INSIGHTS_CONNECTION_STRING string = monitoring.outputs.appInsightsConnectionString
output AZURE_SQL_SERVER_FQDN string = sql.outputs.serverFqdn
// MIGRATION: Output changed from App Gateway public IP to AGC frontend FQDN.
// AGC manages its own public endpoint; use the FQDN for DNS CNAME records.
output AZURE_AGC_FRONTEND_FQDN string = appgateway.outputs.frontendFqdn
output AZURE_AGC_RESOURCE_ID string = appgateway.outputs.trafficControllerId
