// ============================================================================
// SQL Module — Azure SQL Database
// Using Azure SQL Database (logical server + database) for cost efficiency.
// NOTE: For production Azure Local scenarios, replace with SQL Managed Instance
// which supports full SQL Server compatibility and VNet-native deployment.
// Private endpoint ensures no public access.
// ============================================================================

@description('Azure region')
param location string

@description('Unique resource token for naming')
param resourceToken string

@description('Resource tags')
param tags object

@description('Object ID of the Entra ID admin (deploying user/service principal)')
param entraAdminObjectId string

@description('Display name for the Entra ID admin')
param entraAdminDisplayName string = 'AZD Deployer'

@description('Client ID of the managed identity used by pods to authenticate (included in connection string)')
param podIdentityClientId string = ''

@description('VNet resource ID')
param vnetId string

@description('Private endpoints subnet resource ID')
param privateEndpointsSubnetId string

@description('SQL private DNS zone resource ID')
param sqlPrivateDnsZoneId string

// ---------------------------------------------------------------------------
// Variables
// ---------------------------------------------------------------------------

var abbrs = loadJsonContent('../abbreviations.json')
var serverName = '${abbrs.sqlServer}${resourceToken}'
var databaseName = 'ContosoInsurance'

// ---------------------------------------------------------------------------
// SQL Server (logical)
// ---------------------------------------------------------------------------
// Entra-only authentication (required by org policy). The Entra admin is the
// AKS kubelet identity so pods can connect using managed identity without
// additional SQL user provisioning.
// ---------------------------------------------------------------------------

resource sqlServer 'Microsoft.Sql/servers@2024-05-01-preview' = {
  name: serverName
  location: location
  tags: tags
  properties: {
    version: '12.0'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Disabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: 'Application'
      login: entraAdminDisplayName
      sid: entraAdminObjectId
      tenantId: tenant().tenantId
      azureADOnlyAuthentication: true
    }
  }
}

// ---------------------------------------------------------------------------
// Database
// ---------------------------------------------------------------------------

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: databaseName
  location: location
  tags: tags
  sku: {
    name: 'GP_S_Gen5_2'   // General Purpose Serverless, 2 vCores
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 2
  }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    maxSizeBytes: 34359738368  // 32 GB
    autoPauseDelay: 60
    minCapacity: json('0.5')
    zoneRedundant: false
  }
}

// ---------------------------------------------------------------------------
// Private Endpoint
// ---------------------------------------------------------------------------

resource sqlPrivateEndpoint 'Microsoft.Network/privateEndpoints@2024-01-01' = {
  name: '${abbrs.privateEndpoint}sql-${resourceToken}'
  location: location
  tags: tags
  properties: {
    subnet: {
      id: privateEndpointsSubnetId
    }
    privateLinkServiceConnections: [
      {
        name: 'sql-connection'
        properties: {
          privateLinkServiceId: sqlServer.id
          groupIds: ['sqlServer']
        }
      }
    ]
  }
}

resource sqlDnsGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2024-01-01' = {
  parent: sqlPrivateEndpoint
  name: 'sql-dns-group'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'sql-dns-config'
        properties: {
          privateDnsZoneId: sqlPrivateDnsZoneId
        }
      }
    ]
  }
}

// ---------------------------------------------------------------------------
// Outputs
// ---------------------------------------------------------------------------

output serverId string = sqlServer.id
output serverName string = sqlServer.name
output serverFqdn string = sqlServer.properties.fullyQualifiedDomainName
output databaseName string = sqlDatabase.name
output connectionString string = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${databaseName};Authentication=Active Directory Managed Identity;User Id=${podIdentityClientId};Encrypt=true;TrustServerCertificate=false;Connection Timeout=30;'
