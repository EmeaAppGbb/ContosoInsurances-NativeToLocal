#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$ConfigFile = (Join-Path $PSScriptRoot 'azure-local-params.json'),
    [switch]$SkipImageBuild,
    [switch]$SkipIngressInstall,
    [switch]$SkipMetalLbInstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$k8sSourceDir = Join-Path $repoRoot 'k8s\local-connected'
$renderedDir = Join-Path $repoRoot '.local-connected-rendered'

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

    return (($output | Out-String).Trim())
}

function Read-Config {
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        throw "Config file '$Path' was not found."
    }

    return Get-Content -Path $Path -Raw | ConvertFrom-Json -Depth 12
}

function Get-RequiredSecretValue {
    param(
        [string]$EnvironmentVariable,
        [string]$ConfigFallback = ''
    )

    $value = [Environment]::GetEnvironmentVariable($EnvironmentVariable)
    if (-not [string]::IsNullOrWhiteSpace($value)) {
        return $value
    }

    if (-not [string]::IsNullOrWhiteSpace($ConfigFallback) -and $ConfigFallback -notlike '<*' -and $ConfigFallback -notlike 'replace-*') {
        return $ConfigFallback
    }

    throw "Secret '$EnvironmentVariable' must be provided as an environment variable before deployment."
}

function Get-OptionalValue {
    param(
        [string]$EnvironmentVariable,
        [string]$Default = ''
    )

    $value = [Environment]::GetEnvironmentVariable($EnvironmentVariable)
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $Default
    }

    return $value
}

function Resolve-ProjectSelection {
    param([string[]]$ProjectCandidates)

    foreach ($candidate in $ProjectCandidates) {
        $path = Join-Path $repoRoot $candidate
        if (Test-Path $path) {
            return (Resolve-Path $path).Path
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

    Invoke-ExternalCommand -FailureMessage "Failed to publish image '${Repository}:$Tag'." -ScriptBlock {
        dotnet publish $ProjectPath --configuration Release --os linux --arch x64 /t:PublishContainer "/p:ContainerRegistry=$Registry" "/p:ContainerRepository=$Repository" "/p:ContainerImageTags=$Tag"
    }
}

function Render-Templates {
    param([hashtable]$Tokens)

    if (Test-Path $renderedDir) {
        Remove-Item -Path $renderedDir -Recurse -Force
    }

    New-Item -ItemType Directory -Path $renderedDir -Force | Out-Null

    foreach ($file in Get-ChildItem -Path $k8sSourceDir -File) {
        $content = Get-Content -Path $file.FullName -Raw
        foreach ($token in $Tokens.GetEnumerator()) {
            $content = $content.Replace($token.Key, $token.Value)
        }

        Set-Content -Path (Join-Path $renderedDir $file.Name) -Value $content -Encoding utf8NoBOM
    }
}

function Ensure-AksArcCredentials {
    param([pscustomobject]$Config)

    Invoke-ExternalCommand -FailureMessage "Failed to get AKS Arc credentials for '$($Config.azureLocal.connectedClusterName)'." -ScriptBlock {
        az aksarc get-credentials --resource-group $Config.resourceGroupName --name $Config.azureLocal.connectedClusterName --overwrite-existing --only-show-errors
    }
}

function Ensure-HelmRelease {
    param(
        [string]$Name,
        [string]$Namespace,
        [string]$Chart,
        [string[]]$Arguments
    )

    $existing = & helm list --namespace $Namespace -q 2>$null
    if ($existing -contains $Name) {
        Invoke-ExternalCommand -FailureMessage "Failed to upgrade Helm release '$Name'." -ScriptBlock {
            helm upgrade $Name $Chart --namespace $Namespace @Arguments
        }
        return
    }

    Invoke-ExternalCommand -FailureMessage "Failed to install Helm release '$Name'." -ScriptBlock {
        helm install $Name $Chart --namespace $Namespace --create-namespace @Arguments
    }
}

function Wait-ForServiceExternalIp {
    param(
        [string]$Namespace,
        [string]$ServiceName,
        [int]$Attempts = 30
    )

    for ($index = 0; $index -lt $Attempts; $index++) {
        $ip = & kubectl get service $ServiceName --namespace $Namespace --output jsonpath='{.status.loadBalancer.ingress[0].ip}' 2>$null
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($ip)) {
            return $ip.Trim()
        }

        Start-Sleep -Seconds 10
    }

    return ''
}

foreach ($commandName in @('az', 'kubectl', 'helm', 'dotnet', 'git')) {
    if (-not (Get-Command $commandName -ErrorAction SilentlyContinue)) {
        throw "Required command '$commandName' is not available on PATH."
    }
}

Push-Location $repoRoot
try {
    $config = Read-Config -Path $ConfigFile

    $sqlSaPassword = Get-RequiredSecretValue -EnvironmentVariable 'CONTOSO_SQL_SA_PASSWORD'
    $rabbitMqPassword = Get-RequiredSecretValue -EnvironmentVariable 'CONTOSO_RABBITMQ_PASSWORD'
    $backendPortalClientSecret = Get-RequiredSecretValue -EnvironmentVariable 'CONTOSO_BACKEND_PORTAL_CLIENT_SECRET'

    $authority = '{0}{1}' -f $config.identity.azureAdInstance, $config.identity.tenantId
    $sqlConnectionString = "Server=tcp:$($config.application.sqlServerServiceName).$($config.application.namespace).svc.cluster.local,1433;Initial Catalog=$($config.application.sqlDatabaseName);User ID=$($config.application.sqlSaUser);Password=$sqlSaPassword;Encrypt=False;TrustServerCertificate=True;Connection Timeout=30;"
    $repositoryPrefix = $config.containerRegistry.repositoryPrefix

    Write-Step 'Getting AKS Arc credentials'
    Ensure-AksArcCredentials -Config $config

    Write-Step 'Logging into ACR (token-based, no Docker required)'
    $acrTokenJson = Get-CommandOutput -FailureMessage "Failed to get ACR access token for '$($config.containerRegistry.acrName)'." -ScriptBlock {
        az acr login --name $config.containerRegistry.acrName --expose-token --output json
    }
    $acrToken = $acrTokenJson | ConvertFrom-Json
    $env:SDK_CONTAINER_REGISTRY_UNAME = '00000000-0000-0000-0000-000000000000'
    $env:SDK_CONTAINER_REGISTRY_PWORD = $acrToken.accessToken

    $timestamp = Get-Date -Format 'yyyyMMddHHmmss'
    $shortCommit = Get-CommandOutput -FailureMessage 'Failed to determine git commit hash.' -ScriptBlock {
        git -C $repoRoot rev-parse --short HEAD
    }
    $tag = "$timestamp-$shortCommit"

    if (-not $SkipImageBuild) {
        Write-Step 'Publishing container images'
        $workloads = @(
            @{ Repository = "$repositoryPrefix/publicapi"; ProjectCandidates = @('src\ContosoInsurance.PublicApi\ContosoInsurance.PublicApi.csproj', 'src\ContosoInsurance.Api\ContosoInsurance.Api.csproj') },
            @{ Repository = "$repositoryPrefix/webfrontend"; ProjectCandidates = @('src\ContosoInsurance.Web\ContosoInsurance.Web.csproj') },
            @{ Repository = "$repositoryPrefix/backendapi"; ProjectCandidates = @('src\ContosoInsurance.BackendApi\ContosoInsurance.BackendApi.csproj') },
            @{ Repository = "$repositoryPrefix/backendportal"; ProjectCandidates = @('src\ContosoInsurance.BackendPortal\ContosoInsurance.BackendPortal.csproj') },
            @{ Repository = "$repositoryPrefix/worker-claims"; ProjectCandidates = @('src\ContosoInsurance.Worker.Claims\ContosoInsurance.Worker.Claims.csproj') },
            @{ Repository = "$repositoryPrefix/worker-quotes"; ProjectCandidates = @('src\ContosoInsurance.Worker.Quotes\ContosoInsurance.Worker.Quotes.csproj') },
            @{ Repository = "$repositoryPrefix/worker-projections"; ProjectCandidates = @('src\ContosoInsurance.Worker.Projections\ContosoInsurance.Worker.Projections.csproj') }
        )

        foreach ($workload in $workloads) {
            $projectPath = Resolve-ProjectSelection -ProjectCandidates $workload.ProjectCandidates
            Publish-ContainerImage -ProjectPath $projectPath -Repository $workload.Repository -Tag $tag -Registry $config.containerRegistry.acrLoginServer
        }
    }

    Write-Step 'Creating namespace and ACR pull secret'
    & kubectl create namespace $config.application.namespace 2>$null | Out-Null
    & kubectl delete secret acr-pull-secret --namespace $config.application.namespace --ignore-not-found | Out-Null
    Invoke-ExternalCommand -FailureMessage 'Failed to create ACR pull secret.' -ScriptBlock {
        kubectl create secret docker-registry acr-pull-secret --namespace $config.application.namespace --docker-server $config.containerRegistry.acrLoginServer --docker-username '00000000-0000-0000-0000-000000000000' --docker-password $acrToken.accessToken --docker-email 'noreply@contoso.local'
    }

    if (-not $SkipIngressInstall) {
        Write-Step 'Installing ingress-nginx'
        Invoke-ExternalCommand -FailureMessage 'Failed to add ingress-nginx Helm repository.' -ScriptBlock {
            helm repo add ingress-nginx https://kubernetes.github.io/ingress-nginx
        }
        Invoke-ExternalCommand -FailureMessage 'Failed to refresh Helm repositories.' -ScriptBlock {
            helm repo update
        }
        Ensure-HelmRelease -Name 'ingress-nginx' -Namespace 'ingress-nginx' -Chart 'ingress-nginx/ingress-nginx' -Arguments @('--values', (Join-Path $k8sSourceDir 'ingress-nginx-values.yaml'))
    }

    if (-not $SkipMetalLbInstall) {
        Write-Step 'Installing MetalLB'
        Invoke-ExternalCommand -FailureMessage 'Failed to add MetalLB Helm repository.' -ScriptBlock {
            helm repo add metallb https://metallb.github.io/metallb
        }
        Invoke-ExternalCommand -FailureMessage 'Failed to refresh Helm repositories.' -ScriptBlock {
            helm repo update
        }
        Ensure-HelmRelease -Name 'metallb' -Namespace 'metallb-system' -Chart 'metallb/metallb' -Arguments @()
    }

    Write-Step 'Rendering Kubernetes manifests'
    Render-Templates -Tokens @{
        '__NAMESPACE__' = $config.application.namespace
        '__ACR_LOGIN_SERVER__' = $config.containerRegistry.acrLoginServer
        '__REPOSITORY_PREFIX__' = $repositoryPrefix
        '__TAG__' = $tag
        '__SQL_SERVICE_NAME__' = $config.application.sqlServerServiceName
        '__SQL_CONNECTION_STRING__' = $sqlConnectionString
        '__SQL_SA_PASSWORD__' = $sqlSaPassword
        '__RABBITMQ_USERNAME__' = $config.application.rabbitMqUser
        '__RABBITMQ_PASSWORD__' = $rabbitMqPassword
        '__APPINSIGHTS_CONNECTION_STRING__' = $config.observability.applicationInsightsConnectionString
        '__AZURE_AD_INSTANCE__' = $config.identity.azureAdInstance
        '__AZURE_AD_TENANT_ID__' = $config.identity.tenantId
        '__AZURE_AD_DOMAIN__' = $config.identity.azureAdDomain
        '__BACKEND_PORTAL_AZURE_AD_CLIENT_ID__' = $config.identity.backendPortalClientId
        '__BACKEND_PORTAL_AZURE_AD_CALLBACK_PATH__' = $config.identity.backendPortalCallbackPath
        '__BACKEND_PORTAL_AZURE_AD_CLIENT_SECRET__' = $backendPortalClientSecret
        '__AUTHORITY__' = $authority
        '__BACKEND_API_AUDIENCE__' = $config.identity.backendApiAudience
        '__BACKEND_API_SCOPE__' = $config.identity.backendApiAudience
        '__WEB_HOSTNAME__' = $config.application.webHostname
        '__BACKEND_PORTAL_HOSTNAME__' = $config.application.backendPortalHostname
        '__STORAGE_CLASS__' = $config.storage.storageClassName
        '__SQL_STORAGE_SIZE__' = $config.storage.sqlStorageSize
        '__RABBITMQ_STORAGE_SIZE__' = $config.storage.rabbitMqStorageSize
        '__METALLB_START_IP__' = $config.network.loadBalancerStartIp
        '__METALLB_END_IP__' = $config.network.loadBalancerEndIp
    }

    Write-Step 'Applying MetalLB address pool'
    Invoke-ExternalCommand -FailureMessage 'Failed to apply MetalLB configuration.' -ScriptBlock {
        kubectl apply -f (Join-Path $renderedDir 'metallb-config.yaml')
    }

    Write-Step 'Deploying Contoso Insurance workloads'
    Invoke-ExternalCommand -FailureMessage 'Failed to apply Azure Local manifests.' -ScriptBlock {
        kubectl apply -k $renderedDir
    }

    Write-Step 'Waiting for stateful services'
    Invoke-ExternalCommand -FailureMessage 'SQL Server did not become ready in time.' -ScriptBlock {
        kubectl rollout status "statefulset/$($config.application.sqlServerServiceName)" --namespace $config.application.namespace --timeout=300s
    }
    Invoke-ExternalCommand -FailureMessage 'RabbitMQ did not become ready in time.' -ScriptBlock {
        kubectl rollout status statefulset/rabbitmq --namespace $config.application.namespace --timeout=300s
    }

    Write-Step 'Waiting for application deployments'
    foreach ($deploymentName in @('publicapi', 'backendapi', 'worker-claims', 'worker-quotes', 'worker-projections', 'backendportal', 'webfrontend')) {
        Invoke-ExternalCommand -FailureMessage "Deployment '$deploymentName' did not become ready in time." -ScriptBlock {
            kubectl rollout status "deployment/$deploymentName" --namespace $config.application.namespace --timeout=300s
        }
    }

    Write-Step 'Validating ingress exposure'
    $ingressIp = Wait-ForServiceExternalIp -Namespace 'ingress-nginx' -ServiceName 'ingress-nginx-controller'
    if ([string]::IsNullOrWhiteSpace($ingressIp)) {
        Write-Warning 'ingress-nginx did not receive a LoadBalancer IP within the expected time window.'
    }
    else {
        Write-Host "ingress-nginx external IP: $ingressIp" -ForegroundColor Green
    }

    Write-Host "Web URL: http://$($config.application.webHostname)" -ForegroundColor Green
    Write-Host "Backend portal URL: http://$($config.application.backendPortalHostname)" -ForegroundColor Green
    Write-Host "Image tag: $tag" -ForegroundColor Green
}
finally {
    if (Test-Path $renderedDir) {
        Remove-Item -Path $renderedDir -Recurse -Force
    }

    Pop-Location
}
