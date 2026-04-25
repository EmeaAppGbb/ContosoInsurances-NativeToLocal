// ============================================================================
// Networking Module — VNet, Subnets, NSGs, Private DNS Zones
// All services run in a private VNet. Only AGC (Application Gateway for Containers)
// has a public-facing frontend. MIGRATION: Replaced App Gateway WAF v2 with AGC.
// ============================================================================

@description('Azure region')
param location string

@description('Unique resource token for naming')
param resourceToken string

@description('Resource tags')
param tags object

// ---------------------------------------------------------------------------
// Variables
// ---------------------------------------------------------------------------

var abbrs = loadJsonContent('../abbreviations.json')
var vnetName = '${abbrs.virtualNetwork}${resourceToken}'

var vnetAddressPrefix = '10.0.0.0/16'
var subnets = {
  aks: { name: 'snet-aks', prefix: '10.0.0.0/20' }               // /20 = 4096 IPs for AKS pods (Azure CNI)
  sql: { name: 'snet-sql', prefix: '10.0.16.0/24' }              // /24 for SQL MI or private endpoints
  privateEndpoints: { name: 'snet-private-endpoints', prefix: '10.0.17.0/24' }
  // MIGRATION: Renamed from snet-appgw to snet-agc. Application Gateway WAF v2
  // was retired; replaced by Application Gateway for Containers (AGC).
  // AGC requires subnet delegation to Microsoft.ServiceNetworking/trafficControllers.
  agc: { name: 'snet-agc', prefix: '10.0.18.0/24' }             // AGC requires dedicated delegated subnet
}

// ---------------------------------------------------------------------------
// Network Security Groups
// ---------------------------------------------------------------------------

resource nsgAks 'Microsoft.Network/networkSecurityGroups@2024-01-01' = {
  name: '${abbrs.networkSecurityGroup}aks-${resourceToken}'
  location: location
  tags: tags
  properties: {
    securityRules: [
      {
        name: 'DenyAllInbound'
        properties: {
          priority: 4096
          direction: 'Inbound'
          access: 'Deny'
          protocol: '*'
          sourcePortRange: '*'
          destinationPortRange: '*'
          sourceAddressPrefix: '*'
          destinationAddressPrefix: '*'
        }
      }
      {
        name: 'AllowVNetInbound'
        properties: {
          priority: 100
          direction: 'Inbound'
          access: 'Allow'
          protocol: '*'
          sourcePortRange: '*'
          destinationPortRange: '*'
          sourceAddressPrefix: 'VirtualNetwork'
          destinationAddressPrefix: 'VirtualNetwork'
        }
      }
      {
        name: 'AllowAzureLoadBalancerInbound'
        properties: {
          priority: 110
          direction: 'Inbound'
          access: 'Allow'
          protocol: '*'
          sourcePortRange: '*'
          destinationPortRange: '*'
          sourceAddressPrefix: 'AzureLoadBalancer'
          destinationAddressPrefix: '*'
        }
      }
    ]
  }
}

resource nsgSql 'Microsoft.Network/networkSecurityGroups@2024-01-01' = {
  name: '${abbrs.networkSecurityGroup}sql-${resourceToken}'
  location: location
  tags: tags
  properties: {
    securityRules: [
      {
        name: 'DenyAllInbound'
        properties: {
          priority: 4096
          direction: 'Inbound'
          access: 'Deny'
          protocol: '*'
          sourcePortRange: '*'
          destinationPortRange: '*'
          sourceAddressPrefix: '*'
          destinationAddressPrefix: '*'
        }
      }
      {
        name: 'AllowSqlFromAks'
        properties: {
          priority: 100
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourcePortRange: '*'
          destinationPortRange: '1433'
          sourceAddressPrefix: subnets.aks.prefix
          destinationAddressPrefix: '*'
        }
      }
    ]
  }
}

resource nsgPrivateEndpoints 'Microsoft.Network/networkSecurityGroups@2024-01-01' = {
  name: '${abbrs.networkSecurityGroup}pe-${resourceToken}'
  location: location
  tags: tags
  properties: {
    securityRules: [
      {
        name: 'DenyAllInbound'
        properties: {
          priority: 4096
          direction: 'Inbound'
          access: 'Deny'
          protocol: '*'
          sourcePortRange: '*'
          destinationPortRange: '*'
          sourceAddressPrefix: '*'
          destinationAddressPrefix: '*'
        }
      }
      {
        name: 'AllowVNetInbound'
        properties: {
          priority: 100
          direction: 'Inbound'
          access: 'Allow'
          protocol: '*'
          sourcePortRange: '*'
          destinationPortRange: '*'
          sourceAddressPrefix: 'VirtualNetwork'
          destinationAddressPrefix: 'VirtualNetwork'
        }
      }
    ]
  }
}

// MIGRATION: NSG updated for AGC. Application Gateway required GatewayManager
// inbound on ports 65200-65535. AGC does NOT require those ports — it uses
// the ALB Controller inside the cluster. NSG allows HTTP/HTTPS from internet.
resource nsgAgc 'Microsoft.Network/networkSecurityGroups@2024-01-01' = {
  name: '${abbrs.networkSecurityGroup}agc-${resourceToken}'
  location: location
  tags: tags
  properties: {
    securityRules: [
      {
        name: 'AllowHttpsInbound'
        properties: {
          priority: 100
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourcePortRange: '*'
          destinationPortRange: '443'
          sourceAddressPrefix: 'Internet'
          destinationAddressPrefix: '*'
        }
      }
      {
        name: 'AllowHttpInbound'
        properties: {
          priority: 110
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourcePortRange: '*'
          destinationPortRange: '80'
          sourceAddressPrefix: 'Internet'
          destinationAddressPrefix: '*'
        }
      }
      {
        // MIGRATION: Removed AllowGatewayManager rule (ports 65200-65535).
        // App Gateway WAF v2 required this for Azure control plane management.
        // AGC does not need it — the ALB Controller runs inside the K8s cluster.
        name: 'DenyAllInbound'
        properties: {
          priority: 4096
          direction: 'Inbound'
          access: 'Deny'
          protocol: '*'
          sourcePortRange: '*'
          destinationPortRange: '*'
          sourceAddressPrefix: '*'
          destinationAddressPrefix: '*'
        }
      }
    ]
  }
}

// ---------------------------------------------------------------------------
// Virtual Network
// ---------------------------------------------------------------------------

resource vnet 'Microsoft.Network/virtualNetworks@2024-01-01' = {
  name: vnetName
  location: location
  tags: tags
  properties: {
    addressSpace: {
      addressPrefixes: [vnetAddressPrefix]
    }
    subnets: [
      {
        name: subnets.aks.name
        properties: {
          addressPrefix: subnets.aks.prefix
          networkSecurityGroup: { id: nsgAks.id }
        }
      }
      {
        name: subnets.sql.name
        properties: {
          addressPrefix: subnets.sql.prefix
          networkSecurityGroup: { id: nsgSql.id }
        }
      }
      {
        name: subnets.privateEndpoints.name
        properties: {
          addressPrefix: subnets.privateEndpoints.prefix
          networkSecurityGroup: { id: nsgPrivateEndpoints.id }
          privateEndpointNetworkPolicies: 'Enabled'
        }
      }
      {
        // MIGRATION: Subnet renamed from snet-appgw to snet-agc.
        // Added delegation to Microsoft.ServiceNetworking/trafficControllers
        // which is REQUIRED for AGC association. Without this delegation,
        // the AGC association will fail to deploy.
        name: subnets.agc.name
        properties: {
          addressPrefix: subnets.agc.prefix
          networkSecurityGroup: { id: nsgAgc.id }
          delegations: [
            {
              name: 'agc-delegation'
              properties: {
                serviceName: 'Microsoft.ServiceNetworking/trafficControllers'
              }
            }
          ]
        }
      }
    ]
  }
}

// ---------------------------------------------------------------------------
// Private DNS Zones
// ---------------------------------------------------------------------------

resource sqlPrivateDnsZone 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  name: 'privatelink${environment().suffixes.sqlServerHostname}'
  location: 'global'
  tags: tags
}

resource acrPrivateDnsZone 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  name: 'privatelink.azurecr.io'
  location: 'global'
  tags: tags
}

resource kvPrivateDnsZone 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  name: 'privatelink.vaultcore.azure.net'
  location: 'global'
  tags: tags
}

// Link DNS zones to VNet
resource sqlDnsLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = {
  parent: sqlPrivateDnsZone
  name: 'sql-vnet-link'
  location: 'global'
  properties: {
    virtualNetwork: { id: vnet.id }
    registrationEnabled: false
  }
}

resource acrDnsLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = {
  parent: acrPrivateDnsZone
  name: 'acr-vnet-link'
  location: 'global'
  properties: {
    virtualNetwork: { id: vnet.id }
    registrationEnabled: false
  }
}

resource kvDnsLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = {
  parent: kvPrivateDnsZone
  name: 'kv-vnet-link'
  location: 'global'
  properties: {
    virtualNetwork: { id: vnet.id }
    registrationEnabled: false
  }
}

// ---------------------------------------------------------------------------
// Outputs
// ---------------------------------------------------------------------------

output vnetId string = vnet.id
output vnetName string = vnet.name
output aksSubnetId string = vnet.properties.subnets[0].id
output sqlSubnetId string = vnet.properties.subnets[1].id
output privateEndpointsSubnetId string = vnet.properties.subnets[2].id
output agcSubnetId string = vnet.properties.subnets[3].id
output sqlPrivateDnsZoneId string = sqlPrivateDnsZone.id
output acrPrivateDnsZoneId string = acrPrivateDnsZone.id
output kvPrivateDnsZoneId string = kvPrivateDnsZone.id
