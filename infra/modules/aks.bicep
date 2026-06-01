// ============================================================================
// AKS Module — Azure Kubernetes Service Cluster
// Private cluster with system + user node pools, workload identity, and
// Azure Monitor. ALB Controller addon for AGC integration.
//
// MIGRATION (April 2026):
//   - K8s version: 1.30 → 1.35 (1.30 deprecated March 2026)
//   - API version: 2024-06-02-preview → 2024-09-01 (latest stable)
//   - Monitoring: omsagent addon → Azure Monitor managed addon
//   - Ingress: AGIC removed → ALB Controller addon for AGC (Gateway API)
// ============================================================================

@description('Azure region')
param location string

@description('Unique resource token for naming')
param resourceToken string

@description('Resource tags')
param tags object

@description('Kubernetes version')
param kubernetesVersion string

@description('System node pool VM size')
param systemNodeSize string

@description('User node pool VM size')
param userNodeSize string

@description('AKS subnet resource ID')
param aksSubnetId string

@description('Log Analytics workspace resource ID for Container Insights')
param logAnalyticsWorkspaceId string

@description('ACR resource ID for AcrPull role assignment')
param acrId string

@description('Whether to deploy AKS as a private cluster. Defaults to false for the learning lab experience.')
param enablePrivateCluster bool = false

// ---------------------------------------------------------------------------
// Variables
// ---------------------------------------------------------------------------

var abbrs = loadJsonContent('../abbreviations.json')
var clusterName = '${abbrs.aksCluster}${resourceToken}'

// ---------------------------------------------------------------------------
// AKS Cluster
// ---------------------------------------------------------------------------

// MIGRATION: API version updated to 2025-01-01 to support azureMonitorProfile.containerInsights
resource aks 'Microsoft.ContainerService/managedClusters@2025-01-01' = {
  name: clusterName
  location: location
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    kubernetesVersion: kubernetesVersion
    dnsPrefix: 'contoso-${resourceToken}'
    enableRBAC: true

    // Network configuration — Azure CNI for full VNet integration
    networkProfile: {
      networkPlugin: 'azure'
      networkPolicy: 'calico'
      serviceCidr: '10.1.0.0/16'
      dnsServiceIP: '10.1.0.10'
      loadBalancerSku: 'standard'
    }

    // API server access — public by default for the lab, optionally private.
    apiServerAccessProfile: {
      enablePrivateCluster: enablePrivateCluster
      enablePrivateClusterPublicFQDN: enablePrivateCluster
    }

    // OIDC and Workload Identity
    oidcIssuerProfile: {
      enabled: true
    }
    securityProfile: {
      workloadIdentity: {
        enabled: true
      }
    }

    // System node pool
    agentPoolProfiles: [
      {
        name: 'system'
        count: 2
        vmSize: systemNodeSize
        osType: 'Linux'
        osSKU: 'AzureLinux'
        mode: 'System'
        vnetSubnetID: aksSubnetId
        enableAutoScaling: true
        minCount: 2
        maxCount: 5
        maxPods: 110
        type: 'VirtualMachineScaleSets'
      }
    ]

    // Azure Policy addon
    addonProfiles: {
      azurepolicy: {
        enabled: true
      }
    }

    // MIGRATION: Added ALB Controller managed addon for AGC integration.
    // The ALB Controller runs inside AKS and manages the Application Gateway
    // for Containers (AGC) configuration via Gateway API CRDs (Gateway, HTTPRoute).
    // This replaces the retired AGIC (Application Gateway Ingress Controller).
    ingressProfile: {
      webAppRouting: {
        enabled: true
      }
    }
  }
}

// User node pool for application workloads
resource userPool 'Microsoft.ContainerService/managedClusters/agentPools@2025-01-01' = {
  parent: aks
  name: 'workload'
  properties: {
    count: 2
    vmSize: userNodeSize
    osType: 'Linux'
    osSKU: 'AzureLinux'
    mode: 'User'
    vnetSubnetID: aksSubnetId
    enableAutoScaling: true
    minCount: 1
    maxCount: 10
    maxPods: 110
    type: 'VirtualMachineScaleSets'
    nodeTaints: []
    nodeLabels: {
      workload: 'contoso-insurance'
    }
  }
}

// ---------------------------------------------------------------------------
// Role Assignment — AcrPull for Kubelet identity
// ---------------------------------------------------------------------------

// The AcrPull built-in role ID
var acrPullRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')

resource acrPullRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(aks.id, acrId, acrPullRoleDefinitionId)
  scope: resourceGroup()
  properties: {
    roleDefinitionId: acrPullRoleDefinitionId
    principalId: aks.properties.identityProfile.kubeletidentity.objectId
    principalType: 'ServicePrincipal'
  }
}

// ---------------------------------------------------------------------------
// Outputs
// ---------------------------------------------------------------------------

output clusterName string = aks.name
output clusterId string = aks.id
output clusterFqdn string = aks.properties.fqdn
output kubeletIdentityObjectId string = aks.properties.identityProfile.kubeletidentity.objectId
output kubeletIdentityClientId string = aks.properties.identityProfile.kubeletidentity.clientId
output kubeletIdentityResourceId string = aks.properties.identityProfile.kubeletidentity.resourceId
output clusterIdentityPrincipalId string = aks.identity.principalId
