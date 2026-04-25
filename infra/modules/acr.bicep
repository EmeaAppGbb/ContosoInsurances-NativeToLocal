// ============================================================================
// ACR Module — Azure Container Registry (Azure Local Connected Mode)
// ============================================================================
//
// MIGRATION NOTE (from main branch):
// In the cloud deployment, ACR used Premium SKU with a private endpoint so
// the AKS cluster pulled images over the private VNet. In Azure Local
// connected mode:
//
// 1. PRIVATE ENDPOINT REMOVED — The on-premises K8s cluster is NOT in an
//    Azure VNet, so private endpoints don't apply. ACR is accessed over the
//    internet or ExpressRoute.
//
// 2. SKU CHANGED TO STANDARD — Private endpoints required Premium. Without
//    them, Standard is sufficient and cheaper.
//
// 3. PUBLIC ACCESS ENABLED — The Arc-enabled K8s cluster needs to pull
//    images from ACR's public endpoint. Secure with:
//    - Admin user disabled (use token or service principal)
//    - IP-based firewall rules (optional, restrict to Azure Local egress IPs)
//    - Pull secret in K8s namespace (see k8s/acr-pull-secret.yaml)
//
// 4. IMAGE PULL AUTH — Instead of AKS's managed AcrPull role (which used
//    the kubelet's managed identity), the Arc K8s cluster authenticates
//    using a Kubernetes imagePullSecret with ACR credentials.
//
// WHAT STAYED THE SAME:
//   - Same container images, same tags, same registry structure
//   - Same CI/CD pipeline builds and pushes images
//   - Admin user still disabled (best practice)
// ============================================================================

@description('Azure region')
param location string

@description('Unique resource token for naming')
param resourceToken string

@description('Resource tags')
param tags object

@allowed(['Standard', 'Premium'])
@description('ACR SKU — Standard is sufficient without private endpoints')
param sku string = 'Standard'

// ---------------------------------------------------------------------------
// Variables
// ---------------------------------------------------------------------------

var abbrs = loadJsonContent('../abbreviations.json')
// ACR names must be alphanumeric only, 5-50 chars
var acrName = '${abbrs.containerRegistry}${resourceToken}'

// ---------------------------------------------------------------------------
// Container Registry
// ---------------------------------------------------------------------------

resource acr 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' = {
  name: acrName
  location: location
  tags: tags
  sku: {
    name: sku
  }
  properties: {
    // Admin user disabled — use service principal or token-based auth
    // The Arc-enabled K8s cluster uses imagePullSecret for authentication
    adminUserEnabled: false
    // CHANGE: Public access enabled (was Disabled with Premium + private endpoint)
    // The on-premises cluster pulls images over the internet/ExpressRoute
    publicNetworkAccess: 'Enabled'
    // Allow Azure services (e.g., Azure DevOps, GitHub Actions) to push
    networkRuleBypassOptions: 'AzureServices'
  }
}

// ---------------------------------------------------------------------------
// REMOVED: Private Endpoint
// ---------------------------------------------------------------------------
// The cloud deployment had a private endpoint here. It's removed because:
//   - Azure Local K8s cluster is NOT in an Azure VNet
//   - Private endpoints require VNet integration
//   - ACR is accessed over the public internet (or ExpressRoute)
//   - Authentication is via imagePullSecret instead of managed identity
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// Outputs
// ---------------------------------------------------------------------------

output acrId string = acr.id
output acrName string = acr.name
output acrLoginServer string = acr.properties.loginServer
