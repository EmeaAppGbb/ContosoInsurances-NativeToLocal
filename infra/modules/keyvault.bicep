// ============================================================================
// Key Vault Module — Azure Key Vault for Secrets Management
// Stores SQL connection string, RabbitMQ credentials, and other secrets.
// Private endpoint ensures no public access. RBAC-based access control.
// ============================================================================

@description('Azure region')
param location string

@description('Unique resource token for naming')
param resourceToken string

@description('Resource tags')
param tags object

@description('Principal ID of deploying user (for initial access)')
param principalId string

@description('AKS kubelet managed identity object ID')
param aksKubeletIdentityObjectId string

@description('VNet resource ID')
param vnetId string

@description('Private endpoints subnet resource ID')
param privateEndpointsSubnetId string

@description('Key Vault private DNS zone resource ID')
param kvPrivateDnsZoneId string

@secure()
@description('SQL connection string to store in Key Vault')
param sqlConnectionString string

// ---------------------------------------------------------------------------
// Variables
// ---------------------------------------------------------------------------

var abbrs = loadJsonContent('../abbreviations.json')
var vaultName = '${abbrs.keyVault}${resourceToken}'

// Built-in role IDs
var keyVaultSecretsUserRoleId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
var keyVaultAdminRoleId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '00482a5a-887f-4fb3-b363-3b7fe8e74483')

// ---------------------------------------------------------------------------
// Key Vault
// ---------------------------------------------------------------------------

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: vaultName
  location: location
  tags: tags
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    enablePurgeProtection: true
    publicNetworkAccess: 'Disabled'
    networkAcls: {
      defaultAction: 'Deny'
      bypass: 'AzureServices'
    }
  }
}

// ---------------------------------------------------------------------------
// RBAC Role Assignments
// ---------------------------------------------------------------------------

// Deploying user gets admin access
resource deployerAdminRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, principalId, keyVaultAdminRoleId)
  scope: keyVault
  properties: {
    roleDefinitionId: keyVaultAdminRoleId
    principalId: principalId
    principalType: 'User'
  }
}

// AKS kubelet identity gets secrets reader
resource aksSecretsRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, aksKubeletIdentityObjectId, keyVaultSecretsUserRoleId)
  scope: keyVault
  properties: {
    roleDefinitionId: keyVaultSecretsUserRoleId
    principalId: aksKubeletIdentityObjectId
    principalType: 'ServicePrincipal'
  }
}

// ---------------------------------------------------------------------------
// Secrets
// ---------------------------------------------------------------------------

resource sqlConnStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'sql-connection-string'
  properties: {
    value: sqlConnectionString
  }
}

resource rabbitmqPasswordSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'rabbitmq-password'
  properties: {
    value: uniqueString(keyVault.id, 'rabbitmq')
  }
}

// ---------------------------------------------------------------------------
// Private Endpoint
// ---------------------------------------------------------------------------

resource kvPrivateEndpoint 'Microsoft.Network/privateEndpoints@2024-01-01' = {
  name: '${abbrs.privateEndpoint}kv-${resourceToken}'
  location: location
  tags: tags
  properties: {
    subnet: {
      id: privateEndpointsSubnetId
    }
    privateLinkServiceConnections: [
      {
        name: 'kv-connection'
        properties: {
          privateLinkServiceId: keyVault.id
          groupIds: ['vault']
        }
      }
    ]
  }
}

resource kvDnsGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2024-01-01' = {
  parent: kvPrivateEndpoint
  name: 'kv-dns-group'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'kv-dns-config'
        properties: {
          privateDnsZoneId: kvPrivateDnsZoneId
        }
      }
    ]
  }
}

// ---------------------------------------------------------------------------
// Resource Lock (production protection)
// ---------------------------------------------------------------------------

resource vaultLock 'Microsoft.Authorization/locks@2020-05-01' = {
  name: 'kv-do-not-delete'
  scope: keyVault
  properties: {
    level: 'CanNotDelete'
    notes: 'Key Vault contains critical application secrets. Do not delete.'
  }
}

// ---------------------------------------------------------------------------
// Outputs
// ---------------------------------------------------------------------------

output keyVaultId string = keyVault.id
output keyVaultName string = keyVault.name
output keyVaultUri string = keyVault.properties.vaultUri
