// ============================================================================
// Networking Module — Azure Local Network Topology Documentation
// ============================================================================
//
// MIGRATION NOTE (from main branch):
// The cloud networking.bicep deployed an Azure VNet with 4 subnets, NSGs, and
// private DNS zones. Azure Local does NOT use Azure VNet — it has its own
// networking stack:
//
//   - Azure Local SDN (Software Defined Networking) or physical switches
//   - Logical networks defined in Windows Admin Center / Azure Local portal
//   - No private endpoints needed (services run on the same physical network)
//   - No private DNS zones needed (K8s internal DNS handles service discovery)
//
// This module now serves as DOCUMENTATION of the expected network layout and
// deploys a minimal set of tags/metadata for tracking purposes.
//
// PHYSICAL NETWORK REQUIREMENTS for Azure Local:
// ┌─────────────────────────────────────────────────────────────────────┐
// │ Network              │ VLAN │ Subnet          │ Purpose             │
// ├─────────────────────────────────────────────────────────────────────┤
// │ Management           │ 10   │ 10.0.0.0/24     │ Azure Local mgmt,   │
// │                      │      │                 │ Arc agent comms     │
// │ Compute/Workload     │ 20   │ 10.0.1.0/20     │ K8s pods, services, │
// │                      │      │                 │ Arc SQL MI          │
// │ Storage              │ 30   │ 10.0.16.0/24    │ S2D / CSV traffic   │
// │ External/Internet    │ 40   │ 10.0.18.0/24    │ Ingress (MetalLB),  │
// │                      │      │                 │ outbound to Azure   │
// └─────────────────────────────────────────────────────────────────────┘
//
// FIREWALL RULES (equivalent to cloud NSGs):
// These must be configured on the physical firewall or Azure Local SDN:
//
// Inbound:
//   - TCP 80, 443 from Internet → MetalLB external IP (ingress)
//   - TCP 6443 from Management → K8s API server
//   - All from Management subnet → all (cluster management)
//
// Outbound (required for connected mode):
//   - TCP 443 → *.azure.com, *.microsoft.com (Arc agent, ACR, KV, Monitor)
//   - TCP 443 → mcr.microsoft.com (container images)
//   - TCP 443 → *.blob.core.windows.net (Arc data upload)
//   - TCP 443 → login.microsoftonline.com (Entra ID auth)
//   - TCP 443 → management.azure.com (ARM API)
//   - TCP 443 → guestnotificationservice.azure.com (Arc notifications)
//
// NSG-EQUIVALENT RULES for pod-to-pod traffic:
//   - Handled by Kubernetes NetworkPolicy (see k8s/network-policies.yaml)
//   - Same zero-trust model as cloud: default-deny + explicit allow
// ============================================================================

@description('Unique resource token for naming')
param resourceToken string

@description('Resource tags')
param tags object

// ---------------------------------------------------------------------------
// Network Topology Documentation (as Bicep variables for reference)
// ---------------------------------------------------------------------------
// These variables document the expected network layout. They are not deployed
// as Azure resources but serve as a single source of truth for the team.
// ---------------------------------------------------------------------------

var networkTopology = {
  management: {
    name: 'Management Network'
    vlan: 10
    subnet: '10.0.0.0/24'
    gateway: '10.0.0.1'
    purpose: 'Azure Local cluster management, Arc agent communication, Windows Admin Center'
  }
  compute: {
    name: 'Compute/Workload Network'
    vlan: 20
    subnet: '10.0.1.0/20'
    gateway: '10.0.1.1'
    purpose: 'Kubernetes pods, services, Arc SQL MI endpoints'
  }
  storage: {
    name: 'Storage Network'
    vlan: 30
    subnet: '10.0.16.0/24'
    gateway: '10.0.16.1'
    purpose: 'Storage Spaces Direct (S2D) / CSV replication traffic'
  }
  external: {
    name: 'External Network'
    vlan: 40
    subnet: '10.0.18.0/24'
    gateway: '10.0.18.1'
    purpose: 'MetalLB external IPs for ingress, outbound internet access'
  }
}

// MetalLB IP range for LoadBalancer services (carved from external network)
var metalLbIpRange = '10.0.18.100-10.0.18.200'

// ---------------------------------------------------------------------------
// Outputs
// ---------------------------------------------------------------------------
// Expose network topology as outputs so other modules or scripts can reference
// the expected addresses. These are documentation/reference values.
// ---------------------------------------------------------------------------

output networkTopology object = networkTopology
output metalLbIpRange string = metalLbIpRange
output computeSubnet string = networkTopology.compute.subnet
output externalSubnet string = networkTopology.external.subnet
