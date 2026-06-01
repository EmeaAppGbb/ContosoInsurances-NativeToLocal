#!/usr/bin/env pwsh
# ============================================================================
# Contoso Insurance — Hybrid Deployment Script
# Deploys workloads to both the cloud AKS cluster and Azure Local AKS cluster.
#
# Prerequisites:
#   - az CLI authenticated
#   - kubectl configured
#   - Azure Local deployed via Jumpstart LocalBox (https://jumpstart.azure.com/azure_jumpstart_localbox)
#   - VPN/ExpressRoute connectivity between cloud VNet and Azure Local network
#   - AKS Arc cluster (localbox-aks) registered with Azure Arc
#
# References:
#   - LocalBox deployment: https://jumpstart.azure.com/azure_jumpstart_localbox/deployment_az
#   - azure_arc repo: https://github.com/microsoft/azure_arc
# ============================================================================

param(
    [Parameter(Mandatory = $true)]
    [string]$EnvironmentName,

    [Parameter(Mandatory = $true)]
    [string]$CloudClusterName,

    [Parameter(Mandatory = $false)]
    [string]$LocalClusterName = "localbox-aks",

    [Parameter(Mandatory = $true)]
    [string]$ResourceGroup,

    [Parameter(Mandatory = $false)]
    [string]$LocalResourceGroup = $ResourceGroup,

    [Parameter(Mandatory = $false)]
    [string]$Tag = "latest",

    [Parameter(Mandatory = $false)]
    [switch]$UseArcProxy
)

$ErrorActionPreference = "Stop"

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host " Contoso Insurance — Hybrid Deployment" -ForegroundColor Cyan
Write-Host " Cloud Cluster: $CloudClusterName" -ForegroundColor Cyan
Write-Host " Local Cluster: $LocalClusterName" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

# ---------------------------------------------------------------------------
# Get Azure resource details
# ---------------------------------------------------------------------------
Write-Host "`n[1/8] Retrieving Azure resource details..." -ForegroundColor Yellow

$acrName = az acr list --resource-group $ResourceGroup --query '[0].name' -o tsv
$acrLoginServer = az acr show --name $acrName --query loginServer -o tsv
$appInsightsCs = az monitor app-insights component show --resource-group $ResourceGroup --query '[0].connectionString' -o tsv
$kvName = az keyvault list --resource-group $ResourceGroup --query '[0].name' -o tsv
$rabbitmqPassword = az keyvault secret show --vault-name $kvName --name rabbitmq-password --query value -o tsv
$sqlSaPassword = az keyvault secret show --vault-name $kvName --name sql-sa-password --query value -o tsv 2>$null
if (-not $sqlSaPassword) {
    $sqlSaPassword = [System.Guid]::NewGuid().ToString() + "!Aa1"
    az keyvault secret set --vault-name $kvName --name sql-sa-password --value $sqlSaPassword | Out-Null
    Write-Host "  Generated new SQL SA password and stored in Key Vault" -ForegroundColor DarkGray
}

# AGC resource ID (for Gateway API annotation)
$agcId = az network traffic-controller list --resource-group $ResourceGroup --query '[0].id' -o tsv 2>$null

Write-Host "  ACR: $acrLoginServer" -ForegroundColor DarkGray
Write-Host "  AGC: $($agcId ? 'Found' : 'Not found')" -ForegroundColor DarkGray

# ---------------------------------------------------------------------------
# Deploy to Cloud AKS cluster
# ---------------------------------------------------------------------------
Write-Host "`n[2/8] Connecting to cloud AKS cluster..." -ForegroundColor Yellow
az aks get-credentials --resource-group $ResourceGroup --name $CloudClusterName --overwrite-existing

Write-Host "`n[3/8] Deploying cloud workloads..." -ForegroundColor Yellow

# Install Gateway API CRDs
kubectl apply -f https://github.com/kubernetes-sigs/gateway-api/releases/download/v1.2.1/standard-install.yaml 2>&1 | Out-Null

# Apply namespace and config (with substitutions)
$cloudNs = Get-Content "k8s/cloud/namespace.yaml" -Raw
$cloudNs = $cloudNs -replace "__APPINSIGHTS_CONNECTION_STRING__", $appInsightsCs
$cloudNs = $cloudNs -replace "__RABBITMQ_PASSWORD__", $rabbitmqPassword
$cloudNs = $cloudNs -replace "__RABBITMQ_PRIVATE_ENDPOINT__", $env:RABBITMQ_PRIVATE_ENDPOINT

# SQL connection string for cloud (points to on-prem SQL via VPN)
$sqlCs = "Server=$($env:SQL_PRIVATE_ENDPOINT),1433;Database=insurancedb;User Id=sa;Password=$sqlSaPassword;TrustServerCertificate=true"
$cloudNs = $cloudNs -replace "__SQL_CONNECTION_STRING__", $sqlCs
$cloudNs | kubectl apply -f - 2>&1

# Deploy web frontend
$webManifest = Get-Content "k8s/cloud/web-deployment.yaml" -Raw
$webManifest = $webManifest -replace "__ACR_LOGIN_SERVER__", $acrLoginServer
$webManifest = $webManifest -replace "__TAG__", $Tag
$webManifest = $webManifest -replace "__AGC_RESOURCE_ID__", $agcId
$webManifest | kubectl apply -f - 2>&1

# Deploy public API
$apiManifest = Get-Content "k8s/cloud/api-deployment.yaml" -Raw
$apiManifest = $apiManifest -replace "__ACR_LOGIN_SERVER__", $acrLoginServer
$apiManifest = $apiManifest -replace "__TAG__", $Tag
$apiManifest | kubectl apply -f - 2>&1

# Network policies
kubectl apply -f k8s/cloud/network-policies.yaml 2>&1

Write-Host "  Cloud workloads deployed ✓" -ForegroundColor Green

# ---------------------------------------------------------------------------
# Deploy to Azure Local AKS cluster
# ---------------------------------------------------------------------------
Write-Host "`n[4/8] Connecting to Azure Local AKS cluster ($LocalClusterName)..." -ForegroundColor Yellow
if ($UseArcProxy) {
    # For Jumpstart LocalBox: connect via Azure Arc proxy
    Write-Host "  Using Arc proxy to connect to $LocalClusterName" -ForegroundColor DarkGray
    az connectedk8s proxy -n $LocalClusterName -g $LocalResourceGroup &
    Start-Sleep -Seconds 10
} else {
    az aks get-credentials --resource-group $LocalResourceGroup --name $LocalClusterName --overwrite-existing
}

Write-Host "`n[5/8] Deploying local workloads..." -ForegroundColor Yellow

# Apply namespace and config
$localNs = Get-Content "k8s/local/namespace.yaml" -Raw
$localNs = $localNs -replace "__APPINSIGHTS_CONNECTION_STRING__", $appInsightsCs
$localNs = $localNs -replace "__RABBITMQ_PASSWORD__", $rabbitmqPassword
$localNs = $localNs -replace "__SQL_CONNECTION_STRING__", "Server=sqlserver;Database=insurancedb;User Id=sa;Password=$sqlSaPassword;TrustServerCertificate=true"
$localNs = $localNs -replace "__AZURE_AD_INSTANCE__", "https://login.microsoftonline.com/"
$localNs = $localNs -replace "__AZURE_AD_TENANT_ID__", (az account show --query tenantId -o tsv)
$localNs = $localNs -replace "__AZURE_AD_DOMAIN__", ""
$localNs = $localNs -replace "__BACKEND_PORTAL_AZURE_AD_CLIENT_ID__", ""
$localNs = $localNs -replace "__BACKEND_PORTAL_AZURE_AD_CALLBACK_PATH__", "/signin-oidc"
$localNs = $localNs -replace "__BACKEND_API_AZURE_AD_CLIENT_ID__", ""
$localNs = $localNs -replace "__BACKEND_API_AZURE_AD_AUDIENCE__", ""
$localNs = $localNs -replace "__BACKEND_PORTAL_AZURE_AD_CLIENT_SECRET__", ""
$localNs | kubectl apply -f - 2>&1

# Deploy SQL Server
$sqlManifest = Get-Content "k8s/local/sqlserver-deployment.yaml" -Raw
$sqlManifest = $sqlManifest -replace "__SQL_SA_PASSWORD__", $sqlSaPassword
$sqlManifest | kubectl apply -f - 2>&1

# Deploy RabbitMQ
kubectl apply -f k8s/local/rabbitmq-deployment.yaml 2>&1

# Deploy backend workloads
foreach ($manifest in @("backend-api-deployment.yaml", "backend-portal-deployment.yaml", "workers-deployment.yaml")) {
    $content = Get-Content "k8s/local/$manifest" -Raw
    $content = $content -replace "__ACR_LOGIN_SERVER__", $acrLoginServer
    $content = $content -replace "__TAG__", $Tag
    $content | kubectl apply -f - 2>&1
}

# Network policies
kubectl apply -f k8s/local/network-policies.yaml 2>&1

Write-Host "  Local workloads deployed ✓" -ForegroundColor Green

# ---------------------------------------------------------------------------
# Wait for SQL Server and run init job
# ---------------------------------------------------------------------------
Write-Host "`n[6/8] Waiting for SQL Server to be ready..." -ForegroundColor Yellow
kubectl wait --for=condition=ready pod -l app=sqlserver -n contoso-insurance --timeout=120s 2>&1

Write-Host "`n[7/8] Running database initialization..." -ForegroundColor Yellow
kubectl wait --for=condition=complete job/sqlserver-init -n contoso-insurance --timeout=120s 2>&1

# ---------------------------------------------------------------------------
# Verify rollouts
# ---------------------------------------------------------------------------
Write-Host "`n[8/8] Verifying rollouts..." -ForegroundColor Yellow

# Verify local cluster
Write-Host "  Local cluster:" -ForegroundColor DarkGray
kubectl rollout status statefulset/rabbitmq -n contoso-insurance --timeout=120s 2>&1 | Out-Null
kubectl rollout status statefulset/sqlserver -n contoso-insurance --timeout=120s 2>&1 | Out-Null
kubectl rollout status deployment/backendapi -n contoso-insurance --timeout=180s 2>&1 | Out-Null
kubectl rollout status deployment/backendportal -n contoso-insurance --timeout=180s 2>&1 | Out-Null
kubectl rollout status deployment/worker-claims -n contoso-insurance --timeout=180s 2>&1 | Out-Null
Write-Host "    All local deployments ready ✓" -ForegroundColor Green

# Switch back to cloud cluster and verify
az aks get-credentials --resource-group $ResourceGroup --name $CloudClusterName --overwrite-existing
Write-Host "  Cloud cluster:" -ForegroundColor DarkGray
kubectl rollout status deployment/webfrontend -n contoso-insurance --timeout=180s 2>&1 | Out-Null
kubectl rollout status deployment/publicapi -n contoso-insurance --timeout=180s 2>&1 | Out-Null
Write-Host "    All cloud deployments ready ✓" -ForegroundColor Green

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
Write-Host "`n============================================================" -ForegroundColor Cyan
Write-Host " Hybrid Deployment Complete!" -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host " Cloud cluster ($CloudClusterName):" -ForegroundColor White
Write-Host "   • Web Frontend (internet-facing via AGC)" -ForegroundColor DarkGray
Write-Host "   • Public API (intake, connects to on-prem via VPN)" -ForegroundColor DarkGray
Write-Host ""
Write-Host " Local cluster ($LocalClusterName):" -ForegroundColor White
Write-Host "   • Backend Portal (staff-only, internal LB)" -ForegroundColor DarkGray
Write-Host "   • Backend API (sensitive business logic)" -ForegroundColor DarkGray
Write-Host "   • Workers: claims, quotes, projections" -ForegroundColor DarkGray
Write-Host "   • RabbitMQ (messaging)" -ForegroundColor DarkGray
Write-Host "   • SQL Server (data sovereignty)" -ForegroundColor DarkGray
Write-Host ""
Write-Host " Cross-cluster connectivity: VPN/ExpressRoute" -ForegroundColor DarkGray
Write-Host " Fleet Manager: Unified management" -ForegroundColor DarkGray
Write-Host "============================================================" -ForegroundColor Cyan
