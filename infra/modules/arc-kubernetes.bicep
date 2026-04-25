// ============================================================================
// Arc Kubernetes Module — Azure Arc-enabled Kubernetes Configuration
// ============================================================================
//
// REPLACES: aks.bicep (cloud deployment on main branch)
//
// KEY DIFFERENCE: In the cloud deployment, aks.bicep CREATED an AKS cluster.
// Here, the Kubernetes cluster already exists on Azure Local hardware and is
// registered with Azure Arc. This module configures Arc extensions on top of
// that existing cluster:
//
//   1. Azure Monitor Container Insights (replaces AKS OMS agent addon)
//   2. Azure Policy (replaces AKS Azure Policy addon)
//   3. Key Vault Secrets Provider (for syncing Azure KV secrets to K8s)
//   4. Flux GitOps (for declarative, git-driven deployments)
//
// The actual K8s cluster lifecycle (create, scale, upgrade) is managed by
// Azure Local / Windows Admin Center, NOT by Bicep.
//
// WHAT STAYED THE SAME:
//   - Container images are the same
//   - K8s manifests are nearly identical (updated for Ingress vs App Gateway)
//   - RBAC is still enforced
//   - Network policies still work (Calico or equivalent CNI on Azure Local)
// ============================================================================

@description('Resource ID of the Arc-enabled Connected Cluster')
param connectedClusterId string

@description('Log Analytics workspace ID for Container Insights')
param logAnalyticsWorkspaceId string

@description('Resource tags')
param tags object

// ---------------------------------------------------------------------------
// Variables
// ---------------------------------------------------------------------------

// Extract the cluster name from the resource ID for naming extensions
var clusterName = last(split(connectedClusterId, '/'))

// ---------------------------------------------------------------------------
// Arc Extension: Azure Monitor Container Insights
// ---------------------------------------------------------------------------
// MIGRATION NOTE: In the cloud AKS deployment, Container Insights was enabled
// via the omsagent addon in the AKS cluster properties. For Arc-enabled K8s,
// it is installed as a cluster extension instead.
//
// This extension deploys the Azure Monitor agent to the on-prem cluster,
// which sends container logs, metrics, and Prometheus data to the Log
// Analytics workspace in Azure.
// ---------------------------------------------------------------------------

resource monitoringExtension 'Microsoft.KubernetesConfiguration/extensions@2023-05-01' = {
  name: 'azuremonitor-containers'
  scope: connectedCluster
  properties: {
    extensionType: 'Microsoft.AzureMonitor.Containers'
    autoUpgradeMinorVersion: true
    releaseTrain: 'Stable'
    configurationSettings: {
      logAnalyticsWorkspaceResourceID: logAnalyticsWorkspaceId
      'omsagent.resources.daemonset.limits.cpu': '500m'
      'omsagent.resources.daemonset.limits.memory': '750Mi'
    }
  }
}

// ---------------------------------------------------------------------------
// Arc Extension: Azure Policy
// ---------------------------------------------------------------------------
// MIGRATION NOTE: In cloud AKS, Azure Policy was an addon (azurepolicy).
// For Arc-enabled K8s, it is a cluster extension that installs Gatekeeper
// and syncs policies from Azure Policy service.
// ---------------------------------------------------------------------------

resource policyExtension 'Microsoft.KubernetesConfiguration/extensions@2023-05-01' = {
  name: 'azurepolicy'
  scope: connectedCluster
  properties: {
    extensionType: 'Microsoft.PolicyInsights'
    autoUpgradeMinorVersion: true
    releaseTrain: 'Stable'
  }
}

// ---------------------------------------------------------------------------
// Arc Extension: Key Vault Secrets Provider
// ---------------------------------------------------------------------------
// MIGRATION NOTE: In cloud AKS, the CSI Secrets Store driver was available
// via AKS addon. For Arc-enabled K8s, we install it as a cluster extension.
// This allows K8s pods to mount Azure Key Vault secrets as volumes or sync
// them to Kubernetes Secrets.
// ---------------------------------------------------------------------------

resource kvSecretsExtension 'Microsoft.KubernetesConfiguration/extensions@2023-05-01' = {
  name: 'akvsecretsprovider'
  scope: connectedCluster
  properties: {
    extensionType: 'Microsoft.AzureKeyVaultSecretsProvider'
    autoUpgradeMinorVersion: true
    releaseTrain: 'Stable'
    configurationSettings: {
      'secrets-store-csi-driver.syncSecret.enabled': 'true'
      'secrets-store-csi-driver.enableSecretRotation': 'true'
      'secrets-store-csi-driver.rotationPollInterval': '2m'
    }
  }
}

// ---------------------------------------------------------------------------
// Flux GitOps Configuration
// ---------------------------------------------------------------------------
// MIGRATION NOTE: In the cloud deployment, CI/CD applied K8s manifests via
// kubectl directly to the AKS cluster. For Azure Local connected mode,
// we use Flux GitOps for declarative, pull-based deployments. The Arc-enabled
// cluster watches the Git repository and automatically applies changes.
//
// This is more robust for on-premises clusters that may have intermittent
// connectivity — Flux retries until it can pull the latest manifests.
// ---------------------------------------------------------------------------

resource fluxExtension 'Microsoft.KubernetesConfiguration/extensions@2023-05-01' = {
  name: 'flux'
  scope: connectedCluster
  properties: {
    extensionType: 'Microsoft.Flux'
    autoUpgradeMinorVersion: true
    releaseTrain: 'Stable'
  }
}

resource fluxConfig 'Microsoft.KubernetesConfiguration/fluxConfigurations@2023-05-01' = {
  name: 'contoso-insurance-gitops'
  scope: connectedCluster
  dependsOn: [fluxExtension]
  properties: {
    scope: 'cluster'
    namespace: 'flux-system'
    sourceKind: 'GitRepository'
    gitRepository: {
      // The Git repo URL and branch are configured here.
      // In production, use a private repo with SSH key or token auth.
      url: 'https://github.com/EmeaAppGbb/ContosoInsurances-NativeToLocal.git'
      repositoryRef: {
        branch: 'local-connected'
      }
      syncIntervalInSeconds: 120
      timeoutInSeconds: 600
    }
    kustomizations: {
      'contoso-k8s': {
        path: './k8s'
        syncIntervalInSeconds: 120
        timeoutInSeconds: 600
        prune: true
        force: false
        dependsOn: []
      }
    }
  }
}

// ---------------------------------------------------------------------------
// Existing Resource Reference
// ---------------------------------------------------------------------------
// The connected cluster is NOT created by Bicep — it already exists on
// Azure Local and was registered with Arc. We reference it here.
// ---------------------------------------------------------------------------

resource connectedCluster 'Microsoft.Kubernetes/connectedClusters@2024-01-01' existing = {
  name: clusterName
}

// ---------------------------------------------------------------------------
// Outputs
// ---------------------------------------------------------------------------

output connectedClusterName string = connectedCluster.name
output monitoringExtensionId string = monitoringExtension.id
output fluxConfigName string = fluxConfig.name
