// ============================================================================
// Application Gateway for Containers (AGC) — Connected Mode Ingress
// ============================================================================
//
// MIGRATION (April 2026): This module was previously a documentation-only
// module describing NGINX Ingress + MetalLB as the on-prem ingress strategy.
//
// KEY INSIGHT: With AGC supporting Arc-enabled Kubernetes (connected mode),
// we can now use AGC for BOTH cloud and on-prem deployments!
//
// WHAT CHANGED:
//   NGINX Ingress Controller (RETIRED March 2026) → AGC
//   MetalLB (no longer needed) → AGC provides the external endpoint
//   ModSecurity WAF → AGC WAF capabilities
//
// HOW AGC WORKS IN CONNECTED MODE:
//   1. The ALB Controller runs on the Arc-enabled K8s cluster (on-prem)
//   2. AGC Traffic Controller runs in Azure cloud
//   3. Traffic flow: Internet → AGC (Azure) → Arc tunnel → on-prem cluster
//   4. The Arc connectivity agent maintains a secure outbound tunnel
//   5. No inbound firewall rules needed on the Azure Local network!
//
// This is a MAJOR ADVANTAGE of connected mode over the previous NGINX/MetalLB
// approach — you get Azure-managed L7 routing, WAF, and DDoS protection
// without needing to expose on-prem IPs to the internet.
//
// DEPLOYMENT:
//   1. Bicep deploys the AGC Traffic Controller, frontend, and association (this module)
//   2. ALB Controller Arc extension syncs Gateway API resources (arc-kubernetes.bicep)
//   3. K8s Gateway + HTTPRoute resources define routing (web-deployment.yaml)
// ============================================================================

@description('Azure region for AGC (cloud resource)')
param location string

@description('Unique resource token for naming')
param resourceToken string

@description('Resource tags')
param tags object

@description('VNet subnet ID for AGC association (in Azure cloud VNet if hybrid networking is configured)')
param agcSubnetId string = ''

// ---------------------------------------------------------------------------
// Variables
// ---------------------------------------------------------------------------

var trafficControllerName = 'agc-${resourceToken}'
var frontendName = 'agc-frontend-${resourceToken}'
var associationName = 'agc-assoc-${resourceToken}'

// ---------------------------------------------------------------------------
// Traffic Controller — the AGC resource (deployed in Azure cloud)
// ---------------------------------------------------------------------------
// In connected mode, AGC runs in Azure and routes traffic through the Arc
// connectivity tunnel to the on-prem cluster. This is the same resource type
// as the cloud deployment (main branch), but traffic path is different:
//   Cloud: Internet → AGC → AKS VNet → pods
//   Connected: Internet → AGC → Arc tunnel → on-prem pods
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

resource frontend 'Microsoft.ServiceNetworking/trafficControllers/frontends@2025-01-01' = {
  parent: trafficController
  name: frontendName
  location: location
  properties: {}
}

// ---------------------------------------------------------------------------
// AGC Association — links AGC to a subnet (optional for connected mode)
// ---------------------------------------------------------------------------
// NOTE: In connected mode, the association may reference a cloud VNet subnet
// if hybrid networking (VPN/ExpressRoute) is configured, or it can be omitted
// if traffic flows entirely through the Arc tunnel.
// ---------------------------------------------------------------------------

resource association 'Microsoft.ServiceNetworking/trafficControllers/associations@2025-01-01' = if (!empty(agcSubnetId)) {
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
// Resource Lock
// ---------------------------------------------------------------------------

resource agcLock 'Microsoft.Authorization/locks@2020-05-01' = {
  name: 'agc-do-not-delete'
  scope: trafficController
  properties: {
    level: 'CanNotDelete'
    notes: 'AGC Traffic Controller is the sole ingress point for connected mode. Do not delete.'
  }
}

// ---------------------------------------------------------------------------
// Outputs
// ---------------------------------------------------------------------------

output trafficControllerId string = trafficController.id
output trafficControllerName string = trafficController.name
output frontendFqdn string = frontend.properties.fqdn
output frontendId string = frontend.id
