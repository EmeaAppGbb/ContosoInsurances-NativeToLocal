// ============================================================================
// ACR Module — Azure Container Registry
// Premium SKU with private endpoint. Admin access disabled — uses managed
// identity via AcrPull role assignment (handled in AKS module).
// ============================================================================

@description('Azure region')
param location string

@description('Unique resource token for naming')
param resourceToken string

@description('Resource tags')
param tags object

@allowed(['Standard', 'Premium'])
@description('ACR SKU — Premium required for private endpoints')
param sku string

@description('VNet resource ID')
param vnetId string

@description('Private endpoints subnet resource ID')
param privateEndpointsSubnetId string

@description('ACR private DNS zone resource ID')
param acrPrivateDnsZoneId string

// ---------------------------------------------------------------------------
// Variables
// ---------------------------------------------------------------------------

var abbrs = loadJsonContent('../abbreviations.json')
// ACR names must be alphanumeric only, 5-50 chars
var acrName = '${abbrs.containerRegistry}${resourceToken}'

// ---------------------------------------------------------------------------
// Container Registry
// ---------------------------------------------------------------------------

resource acr 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' = {
  name: acrName
  location: location
  tags: tags
  sku: {
    name: sku
  }
  properties: {
    adminUserEnabled: false
    publicNetworkAccess: sku == 'Premium' ? 'Disabled' : 'Enabled'
    networkRuleBypassOptions: 'AzureServices'
  }
}

// ---------------------------------------------------------------------------
// Private Endpoint (Premium SKU only)
// ---------------------------------------------------------------------------

resource acrPrivateEndpoint 'Microsoft.Network/privateEndpoints@2024-01-01' = if (sku == 'Premium') {
  name: '${abbrs.privateEndpoint}acr-${resourceToken}'
  location: location
  tags: tags
  properties: {
    subnet: {
      id: privateEndpointsSubnetId
    }
    privateLinkServiceConnections: [
      {
        name: 'acr-connection'
        properties: {
          privateLinkServiceId: acr.id
          groupIds: ['registry']
        }
      }
    ]
  }
}

resource acrDnsGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2024-01-01' = if (sku == 'Premium') {
  parent: acrPrivateEndpoint
  name: 'acr-dns-group'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'acr-dns-config'
        properties: {
          privateDnsZoneId: acrPrivateDnsZoneId
        }
      }
    ]
  }
}

// ---------------------------------------------------------------------------
// Outputs
// ---------------------------------------------------------------------------

output acrId string = acr.id
output acrName string = acr.name
output acrLoginServer string = acr.properties.loginServer
