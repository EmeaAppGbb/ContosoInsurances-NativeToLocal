// ============================================================================
// Monitoring Module — Log Analytics + App Insights (Azure Local Connected Mode)
// ============================================================================
//
// MIGRATION NOTE (from main branch):
// This module is MOSTLY UNCHANGED. Log Analytics and Application Insights
// remain in Azure cloud — the Arc-enabled K8s cluster sends telemetry to
// these cloud endpoints in connected mode.
//
// KEY DIFFERENCE: In the cloud deployment, Container Insights was configured
// as an AKS addon (omsagent) inside aks.bicep. For Azure Local, Container
// Insights is configured as an Arc cluster extension in arc-kubernetes.bicep.
// This module just provides the Log Analytics workspace that the Arc
// monitoring extension targets.
//
// WHAT STAYED THE SAME:
//   - Same Log Analytics workspace (PerGB2018, 30-day retention)
//   - Same Application Insights (web type, workspace-backed)
//   - Same connection strings for application telemetry
//   - .NET Aspire's OpenTelemetry sends traces/metrics/logs the same way
// ============================================================================

@description('Azure region for cloud monitoring resources')
param location string

@description('Unique resource token for naming')
param resourceToken string

@description('Resource tags')
param tags object

// ---------------------------------------------------------------------------
// Variables
// ---------------------------------------------------------------------------

var abbrs = loadJsonContent('../abbreviations.json')

// ---------------------------------------------------------------------------
// Log Analytics Workspace
// ---------------------------------------------------------------------------
// Used by:
//   - Arc Container Insights extension (configured in arc-kubernetes.bicep)
//   - Application Insights (telemetry from .NET services)
//   - Arc SQL MI diagnostics (optional, configured separately)
// ---------------------------------------------------------------------------

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${abbrs.logAnalyticsWorkspace}${resourceToken}'
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

// ---------------------------------------------------------------------------
// Application Insights
// ---------------------------------------------------------------------------
// UNCHANGED from cloud deployment. The .NET services send telemetry via
// OpenTelemetry (configured in ServiceDefaults), which works identically
// whether the app runs on AKS in Azure or on Arc-enabled K8s on Azure Local.
// ---------------------------------------------------------------------------

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${abbrs.applicationInsights}${resourceToken}'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
    IngestionMode: 'LogAnalytics'
  }
}

// ---------------------------------------------------------------------------
// Outputs
// ---------------------------------------------------------------------------

output logAnalyticsWorkspaceId string = logAnalytics.id
output logAnalyticsWorkspaceName string = logAnalytics.name
output appInsightsId string = appInsights.id
output appInsightsName string = appInsights.name
output appInsightsConnectionString string = appInsights.properties.ConnectionString
output appInsightsInstrumentationKey string = appInsights.properties.InstrumentationKey
