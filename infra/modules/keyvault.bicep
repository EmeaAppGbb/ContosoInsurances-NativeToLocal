// ============================================================================
// Key Vault Module — Azure Key Vault (Azure Local Connected Mode)
// ============================================================================
//
// MIGRATION NOTE (from main branch):
// Key Vault STAYS in Azure cloud — it's reachable from Azure Local in
// connected mode over the internet/ExpressRoute. Key changes:
//
// 1. PRIVATE ENDPOINT REMOVED — The on-premises cluster isn't in an Azure
//    VNet, so private endpoints don't apply.
//
// 2. PUBLIC ACCESS ENABLED — The Arc Key Vault Secrets Provider extension
//    on the on-premises cluster needs to reach Key Vault's public endpoint.
//    Access is controlled by RBAC (not network rules).
//
// 3. AKS KUBELET IDENTITY REMOVED — AKS had a system-assigned managed
//    identity that was granted Key Vault Secrets User role. In Arc-enabled
//    K8s, secrets are synced by the Key Vault Secrets Provider extension,
//    which uses workload identity or a service principal configured during
//    extension setup.
//
// 4. RBAC SIMPLIFIED — Only the deploying user gets admin access initially.
//    The Arc extension's identity is configured outside of Bicep (during
//    extension installation on the cluster).
//
// WHAT STAYED THE SAME:
//   - Same Key Vault resource, same secrets (SQL connection string, RabbitMQ)
//   - Same RBAC-based access model (no access policies)
//   - Soft delete and purge protection still enabled
//   - Resource lock still in place
// ============================================================================

@description('Azure region')
param location string

@description('Unique resource token for naming')
param resourceToken string

@description('Resource tags')
param tags object

@description('Principal ID of deploying user (for initial access)')
param principalId string

@secure()
@description('SQL connection string to store in Key Vault')
param sqlConnectionString string

// ---------------------------------------------------------------------------
// Variables
// ---------------------------------------------------------------------------

var abbrs = loadJsonContent('../abbreviations.json')
var vaultName = '${abbrs.keyVault}${resourceToken}'

// Built-in role IDs
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
    // CHANGE: Public access enabled (was Disabled with private endpoint)
    // The Arc Key Vault Secrets Provider extension needs to reach KV
    // over the public endpoint from the on-premises cluster.
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      defaultAction: 'Allow'
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

// REMOVED: AKS kubelet identity role assignment
// In Arc-enabled K8s, the Key Vault Secrets Provider extension uses its own
// identity configured during extension setup. You can grant it access via:
//   az role assignment create --role "Key Vault Secrets User" \
//     --assignee <extension-identity-object-id> \
//     --scope <key-vault-id>

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
// REMOVED: Private Endpoint
// ---------------------------------------------------------------------------
// The cloud deployment had a private endpoint here. It's removed because
// the on-premises cluster accesses Key Vault over the public endpoint.
// See the MIGRATION NOTE at the top of this file.
// ---------------------------------------------------------------------------

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
