// ============================================================================
// Azure Kubernetes Fleet Manager
// Manages the hybrid cluster topology: cloud AKS + Azure Local AKS.
// Fleet provides unified management, policy, and workload placement.
// ============================================================================

@description('Azure region for the Fleet Manager hub')
param location string

@description('Resource token for unique naming')
param resourceToken string

@description('Tags to apply to resources')
param tags object

@description('Resource ID of the cloud AKS cluster')
param cloudAksClusterId string

@description('Resource ID of the Azure Local AKS cluster (Arc-enabled)')
param localAksClusterId string

// ---------------------------------------------------------------------------
// Fleet Manager Hub
// ---------------------------------------------------------------------------

resource fleet 'Microsoft.ContainerService/fleets@2024-04-01' = {
  name: 'fleet-${resourceToken}'
  location: location
  tags: tags
  properties: {
    hubProfile: {
      dnsPrefix: 'fleet-${resourceToken}'
    }
  }
}

// ---------------------------------------------------------------------------
// Fleet Members
// ---------------------------------------------------------------------------

resource cloudMember 'Microsoft.ContainerService/fleets/members@2024-04-01' = {
  parent: fleet
  name: 'aks-cloud'
  properties: {
    clusterResourceId: cloudAksClusterId
  }
}

resource localMember 'Microsoft.ContainerService/fleets/members@2024-04-01' = {
  parent: fleet
  name: 'aks-local'
  properties: {
    clusterResourceId: localAksClusterId
  }
}

// ---------------------------------------------------------------------------
// Outputs
// ---------------------------------------------------------------------------

output fleetName string = fleet.name
output fleetId string = fleet.id
output fleetHubFqdn string = fleet.properties.hubProfile.fqdn
