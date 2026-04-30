#Requires -Version 7.0
<#
.SYNOPSIS
    Deploy hook: builds images, pushes to ACR, substitutes K8s manifests, applies to AKS.
.DESCRIPTION
    Called by AZD as the deploy hook. Replaces the default Aspire-based deploy.
    All Bicep outputs are available as environment variables.
    This script is IDEMPOTENT — safe to run multiple times.
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$k8sDir = Join-Path $repoRoot "k8s"
$tempDir = Join-Path $repoRoot ".k8s-deploy"

Write-Host "=== Deploy: Building and deploying to AKS ===" -ForegroundColor Cyan

# ---------------------------------------------------------------------------
# 1. Authenticate
# ---------------------------------------------------------------------------
Write-Host "`n--- Authenticating to AKS and ACR ---" -ForegroundColor Yellow

az aks get-credentials `
    --resource-group $env:AZURE_RESOURCE_GROUP `
    --name $env:AZURE_AKS_CLUSTER_NAME `
    --overwrite-existing

az acr login --name $env:AZURE_ACR_NAME

# ---------------------------------------------------------------------------
# 2. Build and push container images
# ---------------------------------------------------------------------------
Write-Host "`n--- Building container images ---" -ForegroundColor Yellow

$tag = "latest"
$acrServer = $env:AZURE_ACR_LOGIN_SERVER

$images = @(
    @{ Name = "contoso-insurance/api";         Dockerfile = "src/ContosoInsurance.Api/Dockerfile" }
    @{ Name = "contoso-insurance/webfrontend"; Dockerfile = "src/ContosoInsurance.Web/Dockerfile" }
    @{ Name = "contoso-insurance/worker";      Dockerfile = "src/ContosoInsurance.Worker/Dockerfile" }
)

foreach ($img in $images) {
    $fullTag = "${acrServer}/$($img.Name):${tag}"
    Write-Host "Building $fullTag ..."
    docker build -t $fullTag -f (Join-Path $repoRoot $img.Dockerfile) $repoRoot
    if ($LASTEXITCODE -ne 0) { throw "Docker build failed for $($img.Name)" }

    Write-Host "Pushing $fullTag ..."
    docker push $fullTag
    if ($LASTEXITCODE -ne 0) { throw "Docker push failed for $($img.Name)" }
}

# ---------------------------------------------------------------------------
# 3. Retrieve secrets
# ---------------------------------------------------------------------------
Write-Host "`n--- Retrieving secrets ---" -ForegroundColor Yellow

$sqlConnectionString = az keyvault secret show `
    --vault-name $env:AZURE_KEY_VAULT_NAME `
    --name "sql-connection-string" `
    --query "value" -o tsv
if ($LASTEXITCODE -ne 0) { throw "Failed to retrieve SQL connection string from Key Vault" }

# Generate a deterministic but unique RabbitMQ password (idempotent per environment)
# Use a hash of resource group name so it's consistent across deploys
$rabbitmqPassword = [Convert]::ToBase64String(
    [System.Security.Cryptography.SHA256]::HashData(
        [System.Text.Encoding]::UTF8.GetBytes("rabbitmq-$($env:AZURE_RESOURCE_GROUP)")
    )
).Substring(0, 24)

$appInsightsConnStr = $env:AZURE_APPLICATION_INSIGHTS_CONNECTION_STRING
$agcResourceId = $env:AZURE_AGC_RESOURCE_ID

# ---------------------------------------------------------------------------
# 4. Substitute placeholders in K8s manifests
# ---------------------------------------------------------------------------
Write-Host "`n--- Preparing K8s manifests ---" -ForegroundColor Yellow

# Clean and recreate temp directory
if (Test-Path $tempDir) { Remove-Item -Recurse -Force $tempDir }
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

# Copy all manifests to temp
Get-ChildItem -Path $k8sDir -Filter "*.yaml" | ForEach-Object {
    $content = Get-Content $_.FullName -Raw
    $content = $content -replace '__ACR_LOGIN_SERVER__', $acrServer
    $content = $content -replace '__TAG__', $tag
    $content = $content -replace '__AGC_RESOURCE_ID__', $agcResourceId
    $content = $content -replace '__APPINSIGHTS_CONNECTION_STRING__', $appInsightsConnStr
    $content = $content -replace '__SQL_CONNECTION_STRING__', $sqlConnectionString
    $content = $content -replace '__RABBITMQ_PASSWORD__', $rabbitmqPassword
    Set-Content -Path (Join-Path $tempDir $_.Name) -Value $content -NoNewline
}

# ---------------------------------------------------------------------------
# 5. Apply manifests in order
# ---------------------------------------------------------------------------
Write-Host "`n--- Applying K8s manifests ---" -ForegroundColor Yellow

$manifestOrder = @(
    "namespace.yaml"
    "configmap.yaml"
    "secrets.yaml"
    "rabbitmq-deployment.yaml"
    "api-deployment.yaml"
    "worker-deployment.yaml"
    "web-deployment.yaml"
    "network-policies.yaml"
)

foreach ($manifest in $manifestOrder) {
    $filePath = Join-Path $tempDir $manifest
    if (Test-Path $filePath) {
        Write-Host "Applying $manifest ..."
        kubectl apply -f $filePath
        if ($LASTEXITCODE -ne 0) { throw "kubectl apply failed for $manifest" }
    } else {
        Write-Warning "Manifest not found: $manifest (skipped)"
    }
}

# ---------------------------------------------------------------------------
# 6. Cleanup temp directory
# ---------------------------------------------------------------------------
Write-Host "`n--- Cleanup ---" -ForegroundColor Yellow
Remove-Item -Recurse -Force $tempDir

# ---------------------------------------------------------------------------
# 7. Status check
# ---------------------------------------------------------------------------
Write-Host "`n--- Deployment status ---" -ForegroundColor Yellow
kubectl get pods -n contoso-insurance
kubectl get svc -n contoso-insurance
kubectl get gateway -n contoso-insurance 2>$null

Write-Host "`n=== Deploy complete ===" -ForegroundColor Green
