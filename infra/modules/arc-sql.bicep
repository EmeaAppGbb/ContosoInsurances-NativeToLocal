// ============================================================================
// Arc SQL Module — Azure Arc-enabled SQL Managed Instance
// ============================================================================
//
// REPLACES: sql.bicep (cloud deployment on main branch)
//
// KEY DIFFERENCES from the cloud sql.bicep:
//
// 1. RESOURCE TYPE: The cloud module used Microsoft.Sql/servers + databases
//    (Azure SQL Database serverless). This module uses
//    Microsoft.AzureArcData/sqlManagedInstances (Arc-enabled SQL MI).
//
// 2. LOCATION: Cloud SQL deployed to an Azure region. Arc SQL MI deploys to
//    a Custom Location backed by Azure Local hardware.
//
// 3. NETWORKING: Cloud SQL used a private endpoint in a VNet subnet. Arc SQL MI
//    runs directly on the Azure Local network — no private endpoints needed.
//    The K8s pods and SQL MI are on the same physical network.
//
// 4. STORAGE: Cloud SQL used Azure-managed storage. Arc SQL MI uses the
//    Azure Local integrated storage (CSV/Storage Spaces Direct) via
//    Kubernetes persistent volumes.
//
// 5. HA: Cloud SQL had built-in geo-redundancy options. Arc SQL MI supports
//    Always On availability groups (configured separately, requires 3+ replicas).
//
// WHAT STAYED THE SAME:
//   - Same database name (ContosoInsurance)
//   - Same connection string format (consumed by EF Core identically)
//   - Same admin credentials pattern
//   - Application code needs ZERO changes
// ============================================================================

@description('Unique resource token for naming')
param resourceToken string

@description('Custom Location resource ID (Azure Local cluster projection)')
param customLocationId string

@description('Location for the SQL MI ARM resource (must match connected cluster region)')
param location string

@secure()
@description('SQL administrator login')
param adminLogin string

@secure()
@description('SQL administrator password')
param adminPassword string

@description('Number of vCores for SQL MI')
@allowed([2, 4, 8, 16])
param vCores int = 4

@description('Memory limit in GB')
param memoryGb int = 8

@description('Data storage size in GB')
param dataStorageGb int = 32

@description('Log storage size in GB')
param logStorageGb int = 5

@description('Backup retention period in days')
param backupRetentionDays int = 7

@description('SQL MI license type')
@allowed(['BasePrice', 'LicenseIncluded', 'DisasterRecovery'])
param licenseType string = 'LicenseIncluded'

@description('Service tier')
@allowed(['GeneralPurpose', 'BusinessCritical'])
param tier string = 'GeneralPurpose'

@description('Resource tags')
param tags object

// ---------------------------------------------------------------------------
// Variables
// ---------------------------------------------------------------------------

var abbrs = loadJsonContent('../abbreviations.json')
var instanceName = '${abbrs.arcSqlManagedInstance}${resourceToken}'
var databaseName = 'ContosoInsurance'

// The FQDN for Arc SQL MI follows the pattern:
// <instance-name>.<namespace>.svc.cluster.local
// This is resolvable within the K8s cluster where the MI runs.
var instanceFqdn = '${instanceName}.arc-data.svc.cluster.local'

// ---------------------------------------------------------------------------
// Arc-enabled SQL Managed Instance
// ---------------------------------------------------------------------------
// This resource is deployed to the Custom Location (Azure Local cluster).
// Under the hood, the Arc data controller running on K8s provisions a
// SQL Server container with the specified resources.
//
// The Arc data controller must be pre-installed on the cluster via:
//   az arcdata dc create --name <name> --k8s-namespace arc-data ...
// ---------------------------------------------------------------------------

resource sqlMi 'Microsoft.AzureArcData/sqlManagedInstances@2024-01-01' = {
  name: instanceName
  // extendedLocation tells ARM to deploy this on Azure Local, not in Azure
  extendedLocation: {
    type: 'CustomLocation'
    name: customLocationId
  }
  // Location is inherited from the Custom Location / connected cluster
  location: location
  tags: tags
  sku: {
    name: 'vCore'
    tier: tier
  }
  properties: {
    admin: adminLogin
    basicLoginInformation: {
      username: adminLogin
      password: adminPassword
    }
    licenseType: licenseType
    // K8s scheduling configuration
    k8sRaw: {
      spec: {
        scheduling: {
          default: {
            resources: {
              requests: {
                cpu: '${vCores}'
                memory: '${memoryGb}Gi'
              }
              limits: {
                cpu: '${vCores}'
                memory: '${memoryGb}Gi'
              }
            }
          }
        }
        storage: {
          data: {
            volumes: [
              {
                size: '${dataStorageGb}Gi'
                // Uses the default storage class on Azure Local
                // Typically 'default' or a CSV-backed storage class
                className: 'default'
              }
            ]
          }
          logs: {
            volumes: [
              {
                size: '${logStorageGb}Gi'
                className: 'default'
              }
            ]
          }
          backups: {
            volumes: [
              {
                size: '${dataStorageGb}Gi'
                className: 'default'
              }
            ]
          }
        }
        settings: {
          collation: 'SQL_Latin1_General_CP1_CI_AS'
        }
        backup: {
          retentionPeriodInDays: backupRetentionDays
        }
      }
    }
  }
}

// ---------------------------------------------------------------------------
// Outputs
// ---------------------------------------------------------------------------
// MIGRATION NOTE: The output shape matches the cloud sql.bicep module so that
// downstream consumers (Key Vault, CI/CD) work without changes.
// The connection string format is identical — EF Core connects the same way.
// ---------------------------------------------------------------------------

output instanceId string = sqlMi.id
output instanceName string = sqlMi.name
output serverFqdn string = instanceFqdn
output databaseName string = databaseName
// Connection string output — note: contains admin login reference.
// In production, use Key Vault references instead of outputting secrets directly.
#disable-next-line outputs-should-not-contain-secrets
output connectionString string = 'Server=tcp:${instanceFqdn},1433;Database=${databaseName};User ID=${adminLogin};Encrypt=true;TrustServerCertificate=true;Connection Timeout=30;'
