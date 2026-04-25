// ============================================================================
// AKS Module — Azure Kubernetes Service Cluster
// Private cluster with system + user node pools, workload identity, and
// Container Insights. All pods run in the AKS subnet (Azure CNI).
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

// ---------------------------------------------------------------------------
// Variables
// ---------------------------------------------------------------------------

var abbrs = loadJsonContent('../abbreviations.json')
var clusterName = '${abbrs.aksCluster}${resourceToken}'

// ---------------------------------------------------------------------------
// AKS Cluster
// ---------------------------------------------------------------------------

resource aks 'Microsoft.ContainerService/managedClusters@2024-06-02-preview' = {
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

    // API server access — private with authorized IP ranges
    apiServerAccessProfile: {
      enablePrivateCluster: true
      enablePrivateClusterPublicFQDN: true
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

    // Addons
    addonProfiles: {
      omsagent: {
        enabled: true
        config: {
          logAnalyticsWorkspaceResourceID: logAnalyticsWorkspaceId
        }
      }
      azurepolicy: {
        enabled: true
      }
    }
  }
}

// User node pool for application workloads
resource userPool 'Microsoft.ContainerService/managedClusters/agentPools@2024-06-02-preview' = {
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
output clusterFqdn string = aks.properties.fqdn
output kubeletIdentityObjectId string = aks.properties.identityProfile.kubeletidentity.objectId
output clusterIdentityPrincipalId string = aks.identity.principalId
