#Requires -Version 7.0
# Deploy hook: build, push, and deploy Contoso Insurance workloads to AKS.

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

    $output = & $ScriptBlock 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw $FailureMessage
    }

    # Filter out any WARNING lines from Azure CLI extensions
    $lines = ($output | Out-String).Trim() -split "`n" | Where-Object { $_ -notmatch '^WARNING:' }
    return ($lines -join "`n").Trim()
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

function Get-OptionalEnvValue {
    param(
        [string]$Name,
        [string]$Default = ''
    )

    $value = [Environment]::GetEnvironmentVariable($Name)
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $Default
    }

    return $value
}

function Get-FirstNonEmptyValue {
    param(
        [string[]]$Values,
        [string]$Default = ''
    )

    foreach ($value in $Values) {
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            return $value
        }
    }

    return $Default
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

function Resolve-ProjectSelection {
    param([string[]]$ProjectCandidates)

    for ($index = 0; $index -lt $ProjectCandidates.Count; $index++) {
        $candidate = Join-Path $repoRoot $ProjectCandidates[$index]
        if (Test-Path $candidate) {
            return [pscustomobject]@{
                Path = (Resolve-Path $candidate).Path
                Candidate = $ProjectCandidates[$index]
                IsFallback = ($index -gt 0)
            }
        }
    }

    throw "None of the candidate projects were found: $($ProjectCandidates -join ', ')"
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

function Publish-WorkloadImage {
    param(
        [string]$WorkloadName,
        [string[]]$ProjectCandidates,
        [string]$Repository,
        [string]$Tag,
        [string]$Registry
    )

    $selection = Resolve-ProjectSelection -ProjectCandidates $ProjectCandidates
    if ($selection.IsFallback) {
        Write-Host "Using fallback project '$($selection.Candidate)' for workload '$WorkloadName'." -ForegroundColor Yellow
    }

    Publish-ContainerImage -ProjectPath $selection.Path -Repository $Repository -Tag $Tag -Registry $Registry
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

function Remove-LegacyResource {
    param(
        [string]$Kind,
        [string]$Name
    )

    & kubectl delete $Kind $Name --namespace $namespace --ignore-not-found | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to delete legacy ${Kind}/${Name}."
    }
}

Push-Location $repoRoot

foreach ($commandName in @('azd', 'az', 'dotnet', 'kubectl', 'git', 'helm')) {
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
    $sqlConnectionString = Get-OptionalEnvValue -Name 'SQL_CONNECTION_STRING'
}
if ([string]::IsNullOrWhiteSpace($sqlConnectionString)) {
    $sqlConnectionString = "Server=tcp:${sqlServerFqdn},1433;Database=ContosoInsurance;Authentication=Active Directory Managed Identity;Encrypt=true;TrustServerCertificate=false;Connection Timeout=30;"
}

$rabbitmqPassword = Try-GetKeyVaultSecretValue -VaultName $keyVaultName -SecretName 'rabbitmq-password'
if ([string]::IsNullOrWhiteSpace($rabbitmqPassword)) {
    $rabbitmqPassword = Get-OptionalEnvValue -Name 'RABBITMQ_PASSWORD'
}
if ([string]::IsNullOrWhiteSpace($rabbitmqPassword)) {
    throw 'RabbitMQ password was not found in Key Vault or environment variables.'
}

$backendPortalClientSecret = Get-FirstNonEmptyValue -Values @(
    (Try-GetKeyVaultSecretValue -VaultName $keyVaultName -SecretName 'backend-portal-azuread-client-secret'),
    (Try-GetKeyVaultSecretValue -VaultName $keyVaultName -SecretName 'backend-portal-client-secret'),
    (Get-OptionalEnvValue -Name 'AZURE_AD_BACKEND_PORTAL_CLIENT_SECRET'),
    (Get-OptionalEnvValue -Name 'AZURE_AD_CLIENT_SECRET')
)
$azureAdInstance = Get-OptionalEnvValue -Name 'AZURE_AD_INSTANCE' -Default 'https://login.microsoftonline.com/'
$azureAdTenantId = Get-OptionalEnvValue -Name 'AZURE_AD_TENANT_ID' -Default 'common'
$azureAdDomain = Get-OptionalEnvValue -Name 'AZURE_AD_DOMAIN' -Default 'contoso.onmicrosoft.com'
$backendPortalClientId = Get-FirstNonEmptyValue -Values @(
    (Get-OptionalEnvValue -Name 'AZURE_AD_BACKEND_PORTAL_CLIENT_ID'),
    (Get-OptionalEnvValue -Name 'AZURE_AD_CLIENT_ID')
)
$backendPortalCallbackPath = Get-OptionalEnvValue -Name 'AZURE_AD_BACKEND_PORTAL_CALLBACK_PATH' -Default '/signin-oidc'
$backendApiClientId = Get-FirstNonEmptyValue -Values @(
    (Get-OptionalEnvValue -Name 'AZURE_AD_BACKEND_API_CLIENT_ID'),
    $backendPortalClientId
)
$backendApiAudience = Get-FirstNonEmptyValue -Values @(
    (Get-OptionalEnvValue -Name 'AZURE_AD_BACKEND_API_AUDIENCE'),
    $backendApiClientId,
    'api://backendapi'
)

$workloads = @(
    [pscustomobject]@{ Name = 'publicapi'; Repository = 'contoso-insurance/publicapi'; ProjectCandidates = @('src\ContosoInsurance.PublicApi\ContosoInsurance.PublicApi.csproj', 'src\ContosoInsurance.Api\ContosoInsurance.Api.csproj') },
    [pscustomobject]@{ Name = 'webfrontend'; Repository = 'contoso-insurance/webfrontend'; ProjectCandidates = @('src\ContosoInsurance.Web\ContosoInsurance.Web.csproj') },
    [pscustomobject]@{ Name = 'backendapi'; Repository = 'contoso-insurance/backendapi'; ProjectCandidates = @('src\ContosoInsurance.BackendApi\ContosoInsurance.BackendApi.csproj', 'src\ContosoInsurance.Api\ContosoInsurance.Api.csproj') },
    [pscustomobject]@{ Name = 'backendportal'; Repository = 'contoso-insurance/backendportal'; ProjectCandidates = @('src\ContosoInsurance.BackendPortal\ContosoInsurance.BackendPortal.csproj') },
    [pscustomobject]@{ Name = 'worker-claims'; Repository = 'contoso-insurance/worker-claims'; ProjectCandidates = @('src\ContosoInsurance.Worker.Claims\ContosoInsurance.Worker.Claims.csproj', 'src\ContosoInsurance.Worker\ContosoInsurance.Worker.csproj') },
    [pscustomobject]@{ Name = 'worker-quotes'; Repository = 'contoso-insurance/worker-quotes'; ProjectCandidates = @('src\ContosoInsurance.Worker.Quotes\ContosoInsurance.Worker.Quotes.csproj', 'src\ContosoInsurance.Worker\ContosoInsurance.Worker.csproj') },
    [pscustomobject]@{ Name = 'worker-projections'; Repository = 'contoso-insurance/worker-projections'; ProjectCandidates = @('src\ContosoInsurance.Worker.Projections\ContosoInsurance.Worker.Projections.csproj', 'src\ContosoInsurance.Worker\ContosoInsurance.Worker.csproj') }
)

try {
    Write-Step 'Logging into ACR (token-based, no Docker required)'
    $acrTokenJson = & az acr login --name $acrName --expose-token --output json 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to get ACR access token for '$acrName'."
    }

    $acrToken = $acrTokenJson | ConvertFrom-Json
    $env:SDK_CONTAINER_REGISTRY_UNAME = '00000000-0000-0000-0000-000000000000'
    $env:SDK_CONTAINER_REGISTRY_PWORD = $acrToken.accessToken

    Write-Step 'Publishing container images'
    foreach ($workload in $workloads) {
        Publish-WorkloadImage -WorkloadName $workload.Name -ProjectCandidates $workload.ProjectCandidates -Repository $workload.Repository -Tag $tag -Registry $acrLoginServer
    }

    Write-Step 'Getting AKS credentials'
    Invoke-ExternalCommand -FailureMessage "Failed to get AKS credentials for cluster '$aksClusterName'." -ScriptBlock {
        az aks get-credentials --resource-group $resourceGroup --name $aksClusterName --overwrite-existing
    }

    Write-Step 'Installing Gateway API CRDs'
    Invoke-ExternalCommand -FailureMessage 'Failed to install Gateway API CRDs.' -ScriptBlock {
        kubectl apply -f https://github.com/kubernetes-sigs/gateway-api/releases/download/v1.2.1/standard-install.yaml
    }

    Write-Step 'Installing ALB Controller for AGC'
    $kubeletClientId = Get-CommandOutput -FailureMessage 'Failed to get kubelet identity client ID.' -ScriptBlock {
        az aks show --resource-group $resourceGroup --name $aksClusterName --query "identityProfile.kubeletidentity.clientId" -o tsv
    }
    $kubeletObjectId = Get-CommandOutput -FailureMessage 'Failed to get kubelet identity object ID.' -ScriptBlock {
        az aks show --resource-group $resourceGroup --name $aksClusterName --query "identityProfile.kubeletidentity.objectId" -o tsv
    }
    $kubeletIdName = Get-CommandOutput -FailureMessage 'Failed to get kubelet identity name.' -ScriptBlock {
        az aks show --resource-group $resourceGroup --name $aksClusterName --query "identityProfile.kubeletidentity.resourceId" -o tsv
    }
    $mcResourceGroup = Get-CommandOutput -FailureMessage 'Failed to get MC resource group.' -ScriptBlock {
        az aks show --resource-group $resourceGroup --name $aksClusterName --query "nodeResourceGroup" -o tsv
    }
    $oidcIssuer = Get-CommandOutput -FailureMessage 'Failed to get OIDC issuer.' -ScriptBlock {
        az aks show --resource-group $resourceGroup --name $aksClusterName --query "oidcIssuerProfile.issuerUrl" -o tsv
    }
    $identityName = ($kubeletIdName -split '/')[-1]

    $existingFedCred = & az identity federated-credential show --name 'alb-controller-fedcred' --identity-name $identityName --resource-group $mcResourceGroup 2>$null
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'Creating federated credential for ALB Controller...'
        Invoke-ExternalCommand -FailureMessage 'Failed to create federated credential.' -ScriptBlock {
            az identity federated-credential create --name 'alb-controller-fedcred' --identity-name $identityName --resource-group $mcResourceGroup --issuer $oidcIssuer --subject 'system:serviceaccount:azure-alb-system:alb-controller-sa' --audiences 'api://AzureADTokenExchange'
        }
    }
    else {
        Write-Host 'Federated credential already exists.'
    }

    Write-Host 'Ensuring RBAC: Reader on resource group...'
    & az role assignment create --assignee-object-id $kubeletObjectId --assignee-principal-type ServicePrincipal --role 'Reader' --scope "/subscriptions/$((az account show --query id -o tsv 2>$null))/resourceGroups/$resourceGroup" 2>$null | Out-Null
    Write-Host 'Ensuring RBAC: AppGw for Containers Configuration Manager on AGC...'
    & az role assignment create --assignee-object-id $kubeletObjectId --assignee-principal-type ServicePrincipal --role 'AppGw for Containers Configuration Manager' --scope $agcResourceId 2>$null | Out-Null

    $helmInstalled = & helm list -n azure-alb-system -q 2>$null
    if ($helmInstalled -notcontains 'alb-controller') {
        Write-Host 'Installing ALB Controller Helm chart...'
        Invoke-ExternalCommand -FailureMessage 'Failed to install ALB Controller.' -ScriptBlock {
            helm install alb-controller oci://mcr.microsoft.com/application-lb/charts/alb-controller --version 1.3.7 --set albController.namespace=azure-alb-system --set "albController.podIdentity.clientID=$kubeletClientId" --create-namespace --namespace azure-alb-system --skip-schema-validation
        }
    }
    else {
        Write-Host 'ALB Controller already installed.'
    }

    Write-Host 'Waiting for ALB Controller pods...'
    Invoke-ExternalCommand -FailureMessage 'ALB Controller did not become ready.' -ScriptBlock {
        kubectl rollout status deployment/alb-controller --namespace azure-alb-system --timeout=120s
    }

    Write-Step 'Rendering Kubernetes manifests'
    Render-Manifests -Tokens @{
        '__ACR_LOGIN_SERVER__' = $acrLoginServer
        '__TAG__' = $tag
        '__AGC_RESOURCE_ID__' = $agcResourceId
        '__SQL_CONNECTION_STRING__' = $sqlConnectionString
        '__APPINSIGHTS_CONNECTION_STRING__' = $appInsightsConnectionString
        '__RABBITMQ_PASSWORD__' = $rabbitmqPassword
        '__AZURE_AD_INSTANCE__' = $azureAdInstance
        '__AZURE_AD_TENANT_ID__' = $azureAdTenantId
        '__AZURE_AD_DOMAIN__' = $azureAdDomain
        '__BACKEND_PORTAL_AZURE_AD_CLIENT_ID__' = $backendPortalClientId
        '__BACKEND_PORTAL_AZURE_AD_CALLBACK_PATH__' = $backendPortalCallbackPath
        '__BACKEND_PORTAL_AZURE_AD_CLIENT_SECRET__' = $backendPortalClientSecret
        '__BACKEND_API_AZURE_AD_CLIENT_ID__' = $backendApiClientId
        '__BACKEND_API_AZURE_AD_AUDIENCE__' = $backendApiAudience
    }

    Write-Step 'Applying Kubernetes manifests'
    $manifestOrder = @(
        'namespace.yaml'
        'secrets.yaml'
        'network-policies.yaml'
        'rabbitmq-deployment.yaml'
        'api-deployment.yaml'
        'backend-api-deployment.yaml'
        'worker-claims-deployment.yaml'
        'worker-quotes-deployment.yaml'
        'worker-projections-deployment.yaml'
        'backend-portal-deployment.yaml'
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

    foreach ($deploymentName in @('publicapi', 'backendapi', 'worker-claims', 'worker-quotes', 'worker-projections', 'backendportal', 'webfrontend')) {
        Invoke-ExternalCommand -FailureMessage "Deployment '$deploymentName' did not become ready in time." -ScriptBlock {
            kubectl rollout status "deployment/$deploymentName" --namespace $namespace --timeout=300s
        }
    }

    Write-Step 'Cleaning up legacy workloads'
    foreach ($legacyResource in @(
        @{ Kind = 'deployment'; Name = 'api' },
        @{ Kind = 'service'; Name = 'api' },
        @{ Kind = 'deployment'; Name = 'worker' },
        @{ Kind = 'service'; Name = 'rabbitmq-service' }
    )) {
        Remove-LegacyResource -Kind $legacyResource.Kind -Name $legacyResource.Name
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
