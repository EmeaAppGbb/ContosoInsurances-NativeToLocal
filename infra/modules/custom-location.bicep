// ============================================================================
// Custom Location Module — Azure Arc Custom Location
// ============================================================================
//
// NEW MODULE (not present in cloud deployment on main branch).
//
// A Custom Location is the Azure Local cluster's projection in Azure Resource
// Manager. It acts as a deployment target for Arc-enabled services:
//   - Arc-enabled SQL Managed Instance
//   - Arc-enabled data services
//   - Any resource that needs to run "on" the Azure Local cluster
//
// The Custom Location is backed by:
//   1. An Arc-enabled Connected Cluster (the K8s cluster on Azure Local)
//   2. A set of Cluster Extensions (e.g., Arc data services)
//
// In connected mode, the Custom Location is visible in the Azure portal and
// can be targeted by ARM/Bicep deployments just like any Azure region.
// ============================================================================

@description('Name for the Custom Location resource')
param customLocationName string

@description('Resource ID of the Arc-enabled Connected Cluster on Azure Local')
param connectedClusterId string

@description('Location for the Custom Location resource (must match connected cluster region)')
param location string

@description('Resource tags')
param tags object

// ---------------------------------------------------------------------------
// Custom Location
// ---------------------------------------------------------------------------
// The Custom Location references an existing Arc-connected cluster.
// It serves as the "location" for deploying Arc-enabled services.
//
// IMPORTANT: The Connected Cluster must already exist and be registered
// with Azure Arc before this resource can be created. This is typically
// done during Azure Local cluster setup via:
//   az connectedk8s connect --name <name> --resource-group <rg>
// ---------------------------------------------------------------------------

resource customLocation 'Microsoft.ExtendedLocation/customLocations@2021-08-15' = {
  name: customLocationName
  location: location
  tags: tags
  properties: {
    hostResourceId: connectedClusterId
    namespace: 'arc-data'
    clusterExtensionIds: []
    // displayName is shown in the Azure portal
    displayName: customLocationName
  }
}

// ---------------------------------------------------------------------------
// Outputs
// ---------------------------------------------------------------------------

output customLocationId string = customLocation.id
output customLocationName string = customLocation.name
