#Requires -Version 7.0
# Postprovision hook: Build, push, and deploy Contoso Insurance to AKS
# Called by azd after infrastructure provisioning completes.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$k8sDir = Join-Path $repoRoot 'k8s'
$renderedManifestDir = Join-Path $repoRoot '.azd-deploy'
$namespace = 'contoso-insurance'

function Write-Step {
    param([string]$Message)

    Write-Host "`n=== $Message ===" -ForegroundColor Cyan
}

function Invoke-ExternalCommand {
    param(
        [scriptblock]$ScriptBlock,
        [string]$FailureMessage
    )

    & $ScriptBlock
    if ($LASTEXITCODE -ne 0) {
        throw $FailureMessage
    }
}

function Get-CommandOutput {
    param(
        [scriptblock]$ScriptBlock,
        [string]$FailureMessage
    )

    $output = & $ScriptBlock 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "$FailureMessage`n$($output | Out-String)"
    }

    return ($output | Out-String).Trim()
}

function Import-AzdEnvironment {
    $envLines = & azd env get-values 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to load AZD environment values.`n$($envLines | Out-String)"
    }

    foreach ($line in $envLines) {
        if ([string]::IsNullOrWhiteSpace($line) -or -not ($line -match '^([^=]+)=(.*)$')) {
            continue
        }

        $name = $matches[1].Trim()
        $value = $matches[2].Trim()

        if ($value.Length -ge 2 -and $value.StartsWith('"') -and $value.EndsWith('"')) {
            $value = $value.Substring(1, $value.Length - 2)
        }

        Set-Item -Path "Env:$name" -Value $value
    }
}

function Get-RequiredEnvValue {
    param([string]$Name)

    $value = [Environment]::GetEnvironmentVariable($Name)
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "Required environment variable '$Name' is missing."
    }

    return $value
}

function Try-GetKeyVaultSecretValue {
    param(
        [string]$VaultName,
        [string]$SecretName
    )

    $output = & az keyvault secret show --vault-name $VaultName --name $SecretName --query value -o tsv 2>$null
    if ($LASTEXITCODE -ne 0 -or $null -eq $output) {
        return $null
    }

    return ($output | Out-String).Trim()
}

function Publish-ContainerImage {
    param(
        [string]$ProjectPath,
        [string]$Repository,
        [string]$Tag,
        [string]$Registry
    )

    Write-Host "Publishing ${Registry}/${Repository}:${Tag}"
    Invoke-ExternalCommand -FailureMessage "Failed to publish container image '${Repository}:${Tag}'." -ScriptBlock {
        dotnet publish $ProjectPath --configuration Release --os linux --arch x64 /t:PublishContainer "/p:ContainerRegistry=$Registry" "/p:ContainerRepository=$Repository" "/p:ContainerImageTags=$Tag"
    }
}

function Render-Manifests {
    param([hashtable]$Tokens)

    if (Test-Path $renderedManifestDir) {
        Remove-Item -Path $renderedManifestDir -Recurse -Force
    }

    New-Item -ItemType Directory -Path $renderedManifestDir -Force | Out-Null

    foreach ($manifest in Get-ChildItem -Path $k8sDir -Filter '*.yaml' | Sort-Object Name) {
        $content = Get-Content -Path $manifest.FullName -Raw

        foreach ($token in $Tokens.GetEnumerator()) {
            $content = $content.Replace($token.Key, $token.Value)
        }

        Set-Content -Path (Join-Path $renderedManifestDir $manifest.Name) -Value $content -Encoding utf8NoBOM
    }
}

Push-Location $repoRoot

foreach ($commandName in @('azd', 'az', 'dotnet', 'kubectl', 'git')) {
    if (-not (Get-Command $commandName -ErrorAction SilentlyContinue)) {
        throw "Required command '$commandName' is not available on PATH."
    }
}

Write-Step 'Loading AZD environment'
Import-AzdEnvironment

$resourceGroup = Get-RequiredEnvValue 'AZURE_RESOURCE_GROUP'
$aksClusterName = Get-RequiredEnvValue 'AZURE_AKS_CLUSTER_NAME'
$acrName = Get-RequiredEnvValue 'AZURE_ACR_NAME'
$acrLoginServer = Get-RequiredEnvValue 'AZURE_ACR_LOGIN_SERVER'
$keyVaultName = Get-RequiredEnvValue 'AZURE_KEY_VAULT_NAME'
$appInsightsConnectionString = Get-RequiredEnvValue 'AZURE_APPLICATION_INSIGHTS_CONNECTION_STRING'
$agcResourceId = Get-RequiredEnvValue 'AZURE_AGC_RESOURCE_ID'
$agcFrontendFqdn = Get-RequiredEnvValue 'AZURE_AGC_FRONTEND_FQDN'
$sqlServerFqdn = Get-RequiredEnvValue 'AZURE_SQL_SERVER_FQDN'

$timestamp = Get-Date -Format 'yyyyMMddHHmmss'
$shortCommit = Get-CommandOutput -FailureMessage 'Failed to determine git commit hash for image tagging.' -ScriptBlock {
    git -C $repoRoot rev-parse --short HEAD
}
$tag = "$timestamp-$shortCommit"

$sqlConnectionString = Try-GetKeyVaultSecretValue -VaultName $keyVaultName -SecretName 'sql-connection-string'
if ([string]::IsNullOrWhiteSpace($sqlConnectionString)) {
    $sqlConnectionString = [Environment]::GetEnvironmentVariable('SQL_CONNECTION_STRING')
}
if ([string]::IsNullOrWhiteSpace($sqlConnectionString)) {
    $sqlConnectionString = "Server=tcp:${sqlServerFqdn},1433;Database=ContosoInsurance;Authentication=Active Directory Managed Identity;Encrypt=true;TrustServerCertificate=false;Connection Timeout=30;"
}

$rabbitmqPassword = Try-GetKeyVaultSecretValue -VaultName $keyVaultName -SecretName 'rabbitmq-password'
if ([string]::IsNullOrWhiteSpace($rabbitmqPassword)) {
    $rabbitmqPassword = [Environment]::GetEnvironmentVariable('RABBITMQ_PASSWORD')
}
if ([string]::IsNullOrWhiteSpace($rabbitmqPassword)) {
    throw "RabbitMQ password was not found in Key Vault or environment variables."
}

try {
    Write-Step 'Logging into ACR (token-based, no Docker required)'
    $acrTokenJson = & az acr login --name $acrName --expose-token --output json 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to get ACR access token for '$acrName'."
    }
    $acrToken = $acrTokenJson | ConvertFrom-Json
    # Set environment variables for .NET SDK container publish to authenticate with ACR
    $env:SDK_CONTAINER_REGISTRY_UNAME = '00000000-0000-0000-0000-000000000000'
    $env:SDK_CONTAINER_REGISTRY_PWORD = $acrToken.accessToken

    Write-Step 'Publishing container images'
    Publish-ContainerImage -ProjectPath (Join-Path $repoRoot 'src\ContosoInsurance.Api\ContosoInsurance.Api.csproj') -Repository 'contoso-insurance/api' -Tag $tag -Registry $acrLoginServer
    Publish-ContainerImage -ProjectPath (Join-Path $repoRoot 'src\ContosoInsurance.Web\ContosoInsurance.Web.csproj') -Repository 'contoso-insurance/webfrontend' -Tag $tag -Registry $acrLoginServer
    Publish-ContainerImage -ProjectPath (Join-Path $repoRoot 'src\ContosoInsurance.Worker\ContosoInsurance.Worker.csproj') -Repository 'contoso-insurance/worker' -Tag $tag -Registry $acrLoginServer

    Write-Step 'Getting AKS credentials'
    Invoke-ExternalCommand -FailureMessage "Failed to get AKS credentials for cluster '$aksClusterName'." -ScriptBlock {
        az aks get-credentials --resource-group $resourceGroup --name $aksClusterName --overwrite-existing
    }

    Write-Step 'Installing Gateway API CRDs'
    Invoke-ExternalCommand -FailureMessage 'Failed to install Gateway API CRDs.' -ScriptBlock {
        kubectl apply -f https://github.com/kubernetes-sigs/gateway-api/releases/download/v1.2.1/standard-install.yaml
    }

    Write-Step 'Rendering Kubernetes manifests'
    Render-Manifests -Tokens @{
        '__ACR_LOGIN_SERVER__' = $acrLoginServer
        '__TAG__' = $tag
        '__AGC_RESOURCE_ID__' = $agcResourceId
        '__SQL_CONNECTION_STRING__' = $sqlConnectionString
        '__APPINSIGHTS_CONNECTION_STRING__' = $appInsightsConnectionString
        '__RABBITMQ_PASSWORD__' = $rabbitmqPassword
    }

    Write-Step 'Applying Kubernetes manifests'
    $manifestOrder = @(
        'namespace.yaml'
        'configmap.yaml'
        'secrets.yaml'
        'network-policies.yaml'
        'rabbitmq-deployment.yaml'
        'api-deployment.yaml'
        'worker-deployment.yaml'
        'web-deployment.yaml'
    )

    foreach ($manifest in $manifestOrder) {
        $manifestPath = Join-Path $renderedManifestDir $manifest
        if (-not (Test-Path $manifestPath)) {
            throw "Expected manifest '$manifest' was not found in '$renderedManifestDir'."
        }

        Write-Host "Applying $manifest"
        Invoke-ExternalCommand -FailureMessage "Failed to apply manifest '$manifest'." -ScriptBlock {
            kubectl apply -f $manifestPath
        }
    }

    Write-Step 'Waiting for workloads'
    Invoke-ExternalCommand -FailureMessage 'RabbitMQ did not become ready in time.' -ScriptBlock {
        kubectl rollout status statefulset/rabbitmq --namespace $namespace --timeout=300s
    }
    Invoke-ExternalCommand -FailureMessage 'API deployment did not become ready in time.' -ScriptBlock {
        kubectl rollout status deployment/api --namespace $namespace --timeout=300s
    }
    Invoke-ExternalCommand -FailureMessage 'Worker deployment did not become ready in time.' -ScriptBlock {
        kubectl rollout status deployment/worker --namespace $namespace --timeout=300s
    }
    Invoke-ExternalCommand -FailureMessage 'Web deployment did not become ready in time.' -ScriptBlock {
        kubectl rollout status deployment/webfrontend --namespace $namespace --timeout=300s
    }

    Write-Host "`nDeployment complete." -ForegroundColor Green
    Write-Host "Image tag: $tag" -ForegroundColor Green
    Write-Host "Application URL: http://$agcFrontendFqdn" -ForegroundColor Green
}
finally {
    if (Test-Path $renderedManifestDir) {
        Remove-Item -Path $renderedManifestDir -Recurse -Force
    }

    Pop-Location
}
