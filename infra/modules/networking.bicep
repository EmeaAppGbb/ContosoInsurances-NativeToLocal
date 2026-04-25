// ============================================================================
// Networking Module - Azure Local Network Topology Documentation
// ============================================================================
//
// MIGRATION NOTE (from main branch):
// The cloud networking.bicep deployed an Azure VNet with 4 subnets, NSGs, and
// private DNS zones. Azure Local does NOT use Azure VNet - it has its own
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
// MIGRATION (April 2026): Updated for AGC (Application Gateway for Containers).
// MetalLB and NGINX Ingress Controller are NO LONGER NEEDED in connected mode.
// AGC provides the external endpoint in Azure cloud, and traffic flows through
// the Arc connectivity tunnel to the on-prem cluster. This eliminates the need
// for on-prem external IPs and simplifies firewall configuration.
//
// PHYSICAL NETWORK REQUIREMENTS for Azure Local:
// +-------------------------------------------------------------------------+
// | Network              | VLAN | Subnet          | Purpose                |
// +-------------------------------------------------------------------------+
// | Management           | 10   | 10.0.0.0/24     | Azure Local mgmt,     |
// |                      |      |                 | Arc agent comms        |
// | Compute/Workload     | 20   | 10.0.1.0/20     | K8s pods, services,   |
// |                      |      |                 | Arc SQL MI             |
// | Storage              | 30   | 10.0.16.0/24    | S2D / CSV traffic     |
// | External/Internet    | 40   | 10.0.18.0/24    | Outbound to Azure     |
// |                      |      |                 | (AGC tunnel, Arc)      |
// +-------------------------------------------------------------------------+
//
// FIREWALL RULES (equivalent to cloud NSGs):
// These must be configured on the physical firewall or Azure Local SDN:
//
// Inbound:
//   - MIGRATION: No inbound rules needed for web traffic! AGC in Azure cloud
//     handles all internet-facing traffic. The Arc tunnel is outbound-only.
//   - TCP 6443 from Management -> K8s API server
//   - All from Management subnet -> all (cluster management)
//
// Outbound (required for connected mode + AGC):
//   - TCP 443 -> *.azure.com, *.microsoft.com (Arc agent, ACR, KV, Monitor, AGC)
//   - TCP 443 -> mcr.microsoft.com (container images)
//   - TCP 443 -> *.blob.core.windows.net (Arc data upload)
//   - TCP 443 -> login.microsoftonline.com (Entra ID auth)
//   - TCP 443 -> management.azure.com (ARM API)
//   - TCP 443 -> guestnotificationservice.azure.com (Arc notifications)
//   - TCP 443 -> *.servicebus.windows.net (Arc tunnel for AGC traffic)
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
//
// MIGRATION: Removed MetalLB IP range - no longer needed with AGC.
// External network is now used only for outbound connectivity to Azure.
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
    // MIGRATION: Updated purpose - MetalLB no longer used. AGC handles ingress
    // from Azure cloud via Arc tunnel. External network is for outbound only.
    purpose: 'Outbound internet access (Arc agent, AGC tunnel, Azure PaaS)'
  }
}

// ---------------------------------------------------------------------------
// Outputs
// ---------------------------------------------------------------------------

output networkTopology object = networkTopology
output computeSubnet string = networkTopology.compute.subnet
output externalSubnet string = networkTopology.external.subnet