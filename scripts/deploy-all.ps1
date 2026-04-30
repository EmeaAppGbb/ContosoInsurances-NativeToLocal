#Requires -Version 7.0
<#
.SYNOPSIS
    Combined post-provision + deploy hook for AKS.
.DESCRIPTION
    Called by AZD after infrastructure provisioning completes.
    Uses 'az aks command invoke' to run kubectl against private AKS cluster.
    Environment variables from Bicep outputs are automatically available.
    This script is IDEMPOTENT — safe to run multiple times.
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$k8sDir = Join-Path $repoRoot "k8s"
$tempDir = Join-Path $repoRoot ".k8s-deploy"

$rg = $env:AZURE_RESOURCE_GROUP
$aksName = $env:AZURE_AKS_CLUSTER_NAME

function Invoke-AksCommand {
    param([string]$Command, [string]$FilePath)
    
    if ($FilePath) {
        $result = az aks command invoke `
            --resource-group $rg `
            --name $aksName `
            --command $Command `
            --file $FilePath `
            2>&1
    } else {
        $result = az aks command invoke `
            --resource-group $rg `
            --name $aksName `
            --command $Command `
            2>&1
    }
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host ($result | Out-String) -ForegroundColor Red
        throw "AKS command invoke failed: $Command"
    }
    Write-Host ($result | Out-String)
}

# ---------------------------------------------------------------------------
# 1. Authenticate to ACR
# ---------------------------------------------------------------------------
Write-Host "=== Step 1: Authenticating to ACR ===" -ForegroundColor Cyan

az acr login --name $env:AZURE_ACR_NAME
if ($LASTEXITCODE -ne 0) { throw "Failed to login to ACR" }

# ---------------------------------------------------------------------------
# 2. Build and push container images
# ---------------------------------------------------------------------------
Write-Host "`n=== Step 2: Building container images ===" -ForegroundColor Cyan

$tag = "latest"
$acrServer = $env:AZURE_ACR_LOGIN_SERVER

$images = @(
    @{ Name = "contoso-insurance/api";         Dockerfile = "src/ContosoInsurance.Api/Dockerfile" }
    @{ Name = "contoso-insurance/webfrontend"; Dockerfile = "src/ContosoInsurance.Web/Dockerfile" }
    @{ Name = "contoso-insurance/worker";      Dockerfile = "src/ContosoInsurance.Worker/Dockerfile" }
)

foreach ($img in $images) {
    $fullTag = "${acrServer}/$($img.Name):${tag}"
    Write-Host "`nBuilding $fullTag ..."
    docker build -t $fullTag -f (Join-Path $repoRoot $img.Dockerfile) $repoRoot
    if ($LASTEXITCODE -ne 0) { throw "Docker build failed for $($img.Name)" }

    Write-Host "Pushing $fullTag ..."
    docker push $fullTag
    if ($LASTEXITCODE -ne 0) { throw "Docker push failed for $($img.Name)" }
}

# ---------------------------------------------------------------------------
# 3. Retrieve secrets
# ---------------------------------------------------------------------------
Write-Host "`n=== Step 3: Retrieving secrets ===" -ForegroundColor Cyan

# Build SQL connection string with explicit managed identity (kubelet identity)
$kubeletClientId = $env:AZURE_CONTAINER_REGISTRY_MANAGED_IDENTITY_ID
$sqlServerFqdn = az sql server list --resource-group $env:AZURE_RESOURCE_GROUP --query "[0].fullyQualifiedDomainName" -o tsv
if ($LASTEXITCODE -ne 0) { throw "Failed to retrieve SQL server FQDN" }
$sqlConnectionString = "Server=tcp:${sqlServerFqdn},1433;Database=ContosoInsurance;Authentication=Active Directory Managed Identity;User Id=${kubeletClientId};Encrypt=true;TrustServerCertificate=false;Connection Timeout=30;"

# Generate a deterministic RabbitMQ password (idempotent per environment)
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
Write-Host "`n=== Step 4: Preparing K8s manifests ===" -ForegroundColor Cyan

if (Test-Path $tempDir) { Remove-Item -Recurse -Force $tempDir }
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

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
# 5. Install Gateway API CRDs via az aks command invoke
# ---------------------------------------------------------------------------
Write-Host "`n=== Step 5: Installing Gateway API CRDs ===" -ForegroundColor Cyan

Invoke-AksCommand -Command "kubectl apply -f https://github.com/kubernetes-sigs/gateway-api/releases/download/v1.2.1/standard-install.yaml"

# ---------------------------------------------------------------------------
# 6. Apply manifests in order via az aks command invoke
# ---------------------------------------------------------------------------
Write-Host "`n=== Step 6: Applying K8s manifests ===" -ForegroundColor Cyan

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
        Invoke-AksCommand -Command "kubectl apply -f $manifest" -FilePath $filePath
    } else {
        Write-Warning "Manifest not found: $manifest (skipped)"
    }
}

# ---------------------------------------------------------------------------
# 7. Cleanup
# ---------------------------------------------------------------------------
Remove-Item -Recurse -Force $tempDir

# ---------------------------------------------------------------------------
# 8. Status check
# ---------------------------------------------------------------------------
Write-Host "`n=== Deployment Status ===" -ForegroundColor Green
Invoke-AksCommand -Command "kubectl get pods -n contoso-insurance"
Invoke-AksCommand -Command "kubectl get svc -n contoso-insurance"

Write-Host "`n=== Deployment complete ===" -ForegroundColor Green
