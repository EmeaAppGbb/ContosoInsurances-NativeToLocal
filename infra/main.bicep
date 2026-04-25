// ============================================================================
// Contoso Insurance — Main Bicep Orchestration (Azure Local — Connected Mode)
// ============================================================================
//
// MIGRATION NOTE (from main branch):
// This file was restructured from subscription-scoped (cloud) to
// resource-group-scoped (Azure Local connected). Key differences:
//
// 1. targetScope = 'resourceGroup' — Azure Local resources deploy into an
//    existing RG that already contains the Arc-connected cluster.
// 2. AKS → Arc-enabled Kubernetes — the K8s cluster runs on-premises on
//    Azure Local hardware; Bicep configures its Azure Arc projection.
// 3. Azure SQL DB → Arc-enabled SQL Managed Instance — runs on Azure Local
//    hardware via a Custom Location.
// 4. App Gateway → NGINX Ingress + MetalLB — Azure Local has no PaaS L7 LB.
// 5. Private endpoints REMOVED — in connected mode the on-prem cluster
//    reaches Azure PaaS (ACR, Key Vault, Monitor) over the internet or
//    ExpressRoute, not via VNet private link.
// 6. Networking module becomes documentation of the physical/logical network.
//
// Azure services that STAY in the cloud (connected mode):
//   • Azure Container Registry (ACR) — image storage
//   • Azure Key Vault — secrets management
//   • Azure Monitor / Log Analytics / Application Insights — telemetry
//
// Azure services that MOVE to Azure Local:
//   • Kubernetes (Arc-enabled) — container orchestration
//   • SQL Managed Instance (Arc-enabled) — relational database
//   • Ingress (NGINX + MetalLB) — load balancing / WAF
// ============================================================================

targetScope = 'resourceGroup'

// ---------------------------------------------------------------------------
// Parameters
// ---------------------------------------------------------------------------

@minLength(1)
@maxLength(64)
@description('Name of the environment (e.g., dev, staging, prod)')
param environmentName string

@description('Primary location for Azure cloud resources (ACR, Key Vault, Monitor)')
param location string

@description('Principal ID of the deploying user/service principal')
param principalId string

// --- Azure Local / Arc parameters ---

@description('Resource ID of the Arc-enabled Connected Cluster running on Azure Local. Example: /subscriptions/.../providers/Microsoft.Kubernetes/connectedClusters/my-cluster')
param connectedClusterId string

@description('Name of the Azure Arc Custom Location representing the Azure Local cluster. This is the deployment target for Arc-enabled services.')
param customLocationName string

@description('Namespace in the Arc-enabled K8s cluster where Arc data services are deployed')
param arcDataServicesNamespace string = 'arc-data'

// --- SQL parameters ---

@secure()
@description('SQL administrator login name')
param sqlAdminLogin string

@secure()
@description('SQL administrator password')
param sqlAdminPassword string

@description('Number of vCores for Arc SQL MI')
@allowed([2, 4, 8, 16])
param sqlVCores int = 4

@description('Memory limit in GB for Arc SQL MI')
param sqlMemoryGb int = 8

@description('Data storage size in GB for Arc SQL MI')
param sqlDataStorageGb int = 32

// --- ACR parameters ---

@description('ACR SKU — Standard is sufficient when not using private endpoints')
@allowed(['Standard', 'Premium'])
param acrSku string = 'Standard'

// ---------------------------------------------------------------------------
// Variables
// ---------------------------------------------------------------------------

var resourceToken = toLower(uniqueString(subscription().id, environmentName, location))
var tags = {
  'azd-env-name': environmentName
  application: 'contoso-insurance'
  environment: environmentName
  managedBy: 'bicep'
  deploymentModel: 'azure-local-connected'
  arcDataNamespace: arcDataServicesNamespace
}

// ---------------------------------------------------------------------------
// Modules
// ---------------------------------------------------------------------------

// Monitoring — deployed first; Log Analytics workspace ID is referenced by Arc extensions.
// CHANGE: Same module, but outputs are consumed by Arc extensions instead of AKS addons.
module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  params: {
    location: location
    resourceToken: resourceToken
    tags: tags
  }
}

// Custom Location — references the Arc-connected cluster's Custom Location.
// NEW: This module did not exist in the cloud deployment.
// The Custom Location is the Azure Local cluster's projection in Azure, used as
// the deployment target for Arc-enabled services (SQL MI, K8s workloads).
module customLocation 'modules/custom-location.bicep' = {
  name: 'custom-location'
  params: {
    customLocationName: customLocationName
    connectedClusterId: connectedClusterId
    location: location
    tags: tags
  }
}

// Arc-enabled Kubernetes — configures the existing on-prem K8s cluster via Azure Arc.
// CHANGE: Replaces aks.bicep. No cluster is created; we configure Arc extensions
// (monitoring, policy, Key Vault secrets provider) and GitOps on the existing cluster.
module arcKubernetes 'modules/arc-kubernetes.bicep' = {
  name: 'arc-kubernetes'
  params: {
    connectedClusterId: connectedClusterId
    logAnalyticsWorkspaceId: monitoring.outputs.logAnalyticsWorkspaceId
    tags: tags
  }
}

// Azure Container Registry — still in Azure cloud.
// CHANGE: Removed private endpoint (on-prem cluster can't use Azure VNet private link).
// ACR is accessed over internet/ExpressRoute. Pull secrets configured in K8s manifests.
module acr 'modules/acr.bicep' = {
  name: 'acr'
  params: {
    location: location
    resourceToken: resourceToken
    tags: tags
    sku: acrSku
  }
}

// Arc-enabled SQL Managed Instance — runs on Azure Local hardware.
// CHANGE: Replaces sql.bicep (Azure SQL Database serverless).
// Deployed to the Custom Location; provides the same output shape (connectionString, serverFqdn).
module arcSql 'modules/arc-sql.bicep' = {
  name: 'arc-sql'
  params: {
    resourceToken: resourceToken
    customLocationId: customLocation.outputs.customLocationId
    location: location
    adminLogin: sqlAdminLogin
    adminPassword: sqlAdminPassword
    vCores: sqlVCores
    memoryGb: sqlMemoryGb
    dataStorageGb: sqlDataStorageGb
    tags: tags
  }
}

// Key Vault — still in Azure cloud (reachable in connected mode).
// CHANGE: Removed private endpoint and VNet params. Removed AKS kubelet identity ref.
// Arc Key Vault secrets provider extension handles secret sync to the on-prem cluster.
module keyvault 'modules/keyvault.bicep' = {
  name: 'keyvault'
  params: {
    location: location
    resourceToken: resourceToken
    tags: tags
    principalId: principalId
    sqlConnectionString: arcSql.outputs.connectionString
  }
}

// Networking — documentation module for Azure Local network topology.
// CHANGE: No longer deploys Azure VNet/subnets/NSGs. Azure Local uses its own
// logical networks (SDN or physical). This module documents the expected topology.
module networking 'modules/networking.bicep' = {
  name: 'networking'
  params: {
    resourceToken: resourceToken
    tags: tags
  }
}

// NOTE: appgateway.bicep is REMOVED. Azure Local does not have Application Gateway.
// Ingress is handled by NGINX Ingress Controller + MetalLB deployed as K8s workloads.
// See k8s/ingress-nginx.yaml and k8s/metallb-config.yaml for the replacement.

// ---------------------------------------------------------------------------
// Outputs (consumed by AZD and CI/CD)
// ---------------------------------------------------------------------------

// CHANGE: Outputs adapted for Arc-based deployment. AKS-specific outputs removed.
output AZURE_RESOURCE_GROUP string = resourceGroup().name
output AZURE_CONNECTED_CLUSTER_ID string = connectedClusterId
output AZURE_CUSTOM_LOCATION_ID string = customLocation.outputs.customLocationId
output AZURE_ACR_NAME string = acr.outputs.acrName
output AZURE_ACR_LOGIN_SERVER string = acr.outputs.acrLoginServer
output AZURE_KEY_VAULT_NAME string = keyvault.outputs.keyVaultName
output AZURE_LOG_ANALYTICS_WORKSPACE_ID string = monitoring.outputs.logAnalyticsWorkspaceId
output AZURE_APPLICATION_INSIGHTS_CONNECTION_STRING string = monitoring.outputs.appInsightsConnectionString
output AZURE_ARC_SQL_FQDN string = arcSql.outputs.serverFqdn
output AZURE_ARC_SQL_CONNECTION_STRING string = arcSql.outputs.connectionString
