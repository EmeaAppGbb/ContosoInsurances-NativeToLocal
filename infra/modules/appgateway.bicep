// ============================================================================
// Application Gateway for Containers (AGC) — Public-Facing Ingress
// ============================================================================
//
// MIGRATION: Replaced Application Gateway WAF v2 with Application Gateway
// for Containers (AGC). Reasons:
//   1. AGIC (Application Gateway Ingress Controller) was RETIRED March 2026.
//   2. AGC is the GA successor (since Nov 2025) using Gateway API instead of
//      the legacy Ingress API.
//   3. AGC uses the ALB Controller (runs inside AKS as a managed addon) to
//      manage configuration declaratively via Gateway + HTTPRoute K8s resources.
//   4. AGC supports WAF policies, public + private frontends, and works with
//      both AKS and Arc-enabled Kubernetes (connected mode).
//
// Resource type: Microsoft.ServiceNetworking/trafficControllers
// Requires: A dedicated subnet delegated to Microsoft.ServiceNetworking/trafficControllers
// K8s side: Gateway API CRDs + ALB Controller addon (see aks.bicep)
//
// KEY ARCHITECTURE CHANGE:
//   Before: App Gateway WAF v2 → backend pool → AKS internal LoadBalancer
//   After:  AGC Traffic Controller → ALB Controller in AKS → Gateway API routes
//   Routing is now declarative in K8s manifests (Gateway + HTTPRoute), NOT in Bicep.
// ============================================================================

@description('Azure region')
param location string

@description('Unique resource token for naming')
param resourceToken string

@description('Resource tags')
param tags object

@description('AGC dedicated subnet resource ID (must be delegated to Microsoft.ServiceNetworking/trafficControllers)')
param agcSubnetId string

// ---------------------------------------------------------------------------
// Variables
// ---------------------------------------------------------------------------

var trafficControllerName = 'agc-${resourceToken}'
var frontendName = 'agc-frontend-${resourceToken}'
var associationName = 'agc-assoc-${resourceToken}'

// ---------------------------------------------------------------------------
// Traffic Controller — the AGC resource
// ---------------------------------------------------------------------------
// MIGRATION: This replaces the Microsoft.Network/applicationGateways resource.
// The Traffic Controller is the control plane for AGC. It manages frontends
// (public/private IPs) and associations (subnet links). Routing rules are
// defined in K8s via Gateway API resources (Gateway + HTTPRoute), NOT in Bicep.
// ---------------------------------------------------------------------------

resource trafficController 'Microsoft.ServiceNetworking/trafficControllers@2025-01-01' = {
  name: trafficControllerName
  location: location
  tags: tags
  properties: {}
}

// ---------------------------------------------------------------------------
// AGC Frontend — public-facing endpoint
// ---------------------------------------------------------------------------
// MIGRATION: Replaces the Application Gateway public IP + frontend config.
// AGC manages its own FQDN; you don't create a separate public IP resource.
// The frontend FQDN is auto-generated and used for DNS CNAME records.
// ---------------------------------------------------------------------------

resource frontend 'Microsoft.ServiceNetworking/trafficControllers/frontends@2025-01-01' = {
  parent: trafficController
  name: frontendName
  location: location
  properties: {}
}

// ---------------------------------------------------------------------------
// AGC Association — links AGC to the VNet subnet
// ---------------------------------------------------------------------------
// MIGRATION: Replaces the Application Gateway gatewayIPConfigurations subnet
// reference. The association tells AGC which subnet to use for data-plane
// traffic. The subnet MUST be delegated to Microsoft.ServiceNetworking/trafficControllers.
// ---------------------------------------------------------------------------

resource association 'Microsoft.ServiceNetworking/trafficControllers/associations@2025-01-01' = {
  parent: trafficController
  name: associationName
  location: location
  properties: {
    associationType: 'subnets'
    subnet: {
      id: agcSubnetId
    }
  }
}

// ---------------------------------------------------------------------------
// Resource Lock (production protection)
// ---------------------------------------------------------------------------

resource agcLock 'Microsoft.Authorization/locks@2020-05-01' = {
  name: 'agc-do-not-delete'
  scope: trafficController
  properties: {
    level: 'CanNotDelete'
    notes: 'AGC Traffic Controller is the sole ingress point. Do not delete.'
  }
}

// ---------------------------------------------------------------------------
// Outputs
// ---------------------------------------------------------------------------

output trafficControllerId string = trafficController.id
output trafficControllerName string = trafficController.name
output frontendFqdn string = frontend.properties.fqdn
output frontendId string = frontend.id
