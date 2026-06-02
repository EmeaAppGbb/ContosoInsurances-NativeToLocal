#!/usr/bin/env pwsh
# ============================================================================
# Contoso Insurance — Hybrid Deployment Script
# Deploys workloads to both the cloud AKS cluster and Azure Local AKS cluster.
# ============================================================================

[CmdletBinding()]
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
    [string]$CloudResourceGroup = $ResourceGroup,

    [Parameter(Mandatory = $false)]
    [string]$LocalResourceGroup = $ResourceGroup,

    [Parameter(Mandatory = $false)]
    [string]$SubscriptionId,

    [Parameter(Mandatory = $false)]
    [string]$AcrLoginServer,

    [Parameter(Mandatory = $false)]
    [string]$CloudSqlConnectionString,

    [Parameter(Mandatory = $false)]
    [string]$CloudSqlPrivateEndpoint = $env:SQL_PRIVATE_ENDPOINT,

    [Parameter(Mandatory = $false)]
    [string]$CloudRabbitMqHostName = $env:RABBITMQ_PRIVATE_ENDPOINT,

    [Parameter(Mandatory = $false)]
    [string]$RabbitMqPassword,

    [Parameter(Mandatory = $false)]
    [string]$SqlSaPassword,

    [Parameter(Mandatory = $false)]
    [string]$Tag = "latest",

    [Parameter(Mandatory = $false)]
    [string]$ImagePullSecretName = "acr-pull",

    [Parameter(Mandatory = $false)]
    [string]$Namespace = "contoso-insurance",

    [Parameter(Mandatory = $false)]
    [string]$GatewayApiCrdUrl = "https://github.com/kubernetes-sigs/gateway-api/releases/download/v1.2.1/standard-install.yaml",

    [Parameter(Mandatory = $false)]
    [switch]$UseArcProxy,

    [Parameter(Mandatory = $false)]
    [switch]$CreateLocalCluster,

    [Parameter(Mandatory = $false)]
    [string]$LocalCustomLocationId,

    [Parameter(Mandatory = $false)]
    [string]$LocalCustomLocationName = "jumpstart",

    [Parameter(Mandatory = $false)]
    [string]$LocalCustomLocationResourceGroup,

    [Parameter(Mandatory = $false)]
    [string]$LocalLogicalNetworkId,

    [Parameter(Mandatory = $false)]
    [string]$LocalLogicalNetworkName = "localboxcluster-InfraLNET",

    [Parameter(Mandatory = $false)]
    [string]$LocalControlPlaneIp,

    [Parameter(Mandatory = $false)]
    [string]$LocalLocation,

    [Parameter(Mandatory = $false)]
    [string]$LocalKubernetesVersion,

    [Parameter(Mandatory = $false)]
    [string]$LocalNodeVmSize,

    [Parameter(Mandatory = $false)]
    [string]$LocalControlPlaneVmSize,

    [Parameter(Mandatory = $false)]
    [int]$LocalNodeCount = 1,

    [Parameter(Mandatory = $false)]
    [int]$LocalControlPlaneCount = 1,

    [Parameter(Mandatory = $false)]
    [int]$LocalLoadBalancerCount = 1,

    [Parameter(Mandatory = $false)]
    [int]$LocalClusterWaitTimeoutMinutes = 90,

    [Parameter(Mandatory = $false)]
    [int]$LocalClusterPollIntervalSeconds = 30,

    [Parameter(Mandatory = $false)]
    [string]$AzureAdInstance = "https://login.microsoftonline.com/",

    [Parameter(Mandatory = $false)]
    [string]$AzureAdTenantId,

    [Parameter(Mandatory = $false)]
    [string]$AzureAdDomain = "",

    [Parameter(Mandatory = $false)]
    [string]$BackendPortalAzureAdClientId = "",

    [Parameter(Mandatory = $false)]
    [string]$BackendPortalAzureAdCallbackPath = "/signin-oidc",

    [Parameter(Mandatory = $false)]
    [string]$BackendApiAzureAdClientId = "",

    [Parameter(Mandatory = $false)]
    [string]$BackendApiAzureAdAudience = "",

    [Parameter(Mandatory = $false)]
    [string]$BackendPortalAzureAdClientSecret = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$script:proxyJob = $null

function Write-Step {
    param([string]$Message)

    Write-Host "`n=== $Message ===" -ForegroundColor Yellow
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

function Try-GetCommandOutput {
    param([scriptblock]$ScriptBlock)

    $output = & $ScriptBlock 2>$null
    if ($LASTEXITCODE -ne 0) {
        return $null
    }

    $text = ($output | Out-String).Trim()
    if ([string]::IsNullOrWhiteSpace($text)) {
        return $null
    }

    return $text
}

function New-RandomPassword {
    param([int]$Length = 24)

    $chars = ('abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789!@$%^*-_+=').ToCharArray()
    $bytes = New-Object byte[] $Length
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)

    $builder = [System.Text.StringBuilder]::new($Length)
    foreach ($byte in $bytes) {
        [void]$builder.Append($chars[$byte % $chars.Length])
    }

    return $builder.ToString()
}

function Resolve-AzureResourceId {
    param(
        [string]$ResourceName,
        [string]$ResourceGroupName,
        [string]$ResourceType,
        [string]$Description
    )

    if ([string]::IsNullOrWhiteSpace($ResourceName)) {
        return $null
    }

    return Get-CommandOutput -FailureMessage "Unable to resolve ${Description} '$ResourceName' in resource group '$ResourceGroupName'." -ScriptBlock {
        az resource show --resource-group $ResourceGroupName --resource-type $ResourceType --name $ResourceName --query id -o tsv --only-show-errors
    }
}

function Get-AksArcClusterStatus {
    param(
        [string]$ClusterName,
        [string]$ResourceGroupName
    )

    $json = Try-GetCommandOutput -ScriptBlock {
        az aksarc show --name $ClusterName --resource-group $ResourceGroupName --query '{name:name,state:properties.status.currentState,provisioning:provisioningState}' -o json --only-show-errors
    }

    if ($null -eq $json) {
        return $null
    }

    return ($json | ConvertFrom-Json)
}

function Wait-ForAksArcClusterReady {
    param(
        [string]$ClusterName,
        [string]$ResourceGroupName,
        [int]$TimeoutMinutes,
        [int]$PollIntervalSeconds
    )

    $deadline = (Get-Date).AddMinutes($TimeoutMinutes)
    do {
        $status = Get-AksArcClusterStatus -ClusterName $ClusterName -ResourceGroupName $ResourceGroupName
        if ($null -eq $status) {
            throw "Unable to find AKS Arc cluster '$ClusterName' in resource group '$ResourceGroupName'."
        }

        $state = [string]$status.state
        $provisioning = [string]$status.provisioning
        Write-Host "  AKS Arc cluster status: state='$state', provisioning='$provisioning'" -ForegroundColor DarkGray

        if ($state -eq 'Failed' -or $provisioning -eq 'Failed' -or $provisioning -eq 'Canceled') {
            $details = Try-GetCommandOutput -ScriptBlock {
                az aksarc show --name $ClusterName --resource-group $ResourceGroupName --query '{state:properties.status.currentState,provisioning:provisioningState,error:errorDetails}' -o json --only-show-errors
            }

            if ($details) {
                throw "AKS Arc cluster '$ClusterName' failed while provisioning.`n$details"
            }

            throw "AKS Arc cluster '$ClusterName' failed while provisioning."
        }

        $readyStates = @('Succeeded', 'Running', 'Connected', 'Ready')
        $pendingStates = @('Creating', 'Provisioning', 'Accepted', 'Updating', 'Reconciling', 'Pending')
        $pendingProvisioningStates = @('Creating', 'Updating', 'Accepted', 'InProgress', 'Running')

        if (($readyStates -contains $state) -and ([string]::IsNullOrWhiteSpace($provisioning) -or $provisioning -eq 'Succeeded' -or $pendingProvisioningStates -notcontains $provisioning)) {
            return
        }

        if (($pendingStates -contains $state) -or ($pendingProvisioningStates -contains $provisioning) -or [string]::IsNullOrWhiteSpace($state)) {
            if ((Get-Date) -ge $deadline) {
                throw "Timed out waiting for AKS Arc cluster '$ClusterName' to become ready after $TimeoutMinutes minutes. Last known state='$state', provisioning='$provisioning'."
            }

            Start-Sleep -Seconds $PollIntervalSeconds
            continue
        }

        if ($provisioning -eq 'Succeeded') {
            return
        }

        if ((Get-Date) -ge $deadline) {
            throw "Timed out waiting for AKS Arc cluster '$ClusterName' to become ready after $TimeoutMinutes minutes. Last known state='$state', provisioning='$provisioning'."
        }

        Start-Sleep -Seconds $PollIntervalSeconds
    } while ($true)
}

function Ensure-AksArcCluster {
    param(
        [string]$ClusterName,
        [string]$ResourceGroupName
    )

    $status = Get-AksArcClusterStatus -ClusterName $ClusterName -ResourceGroupName $ResourceGroupName
    if ($null -eq $status) {
        if (-not $CreateLocalCluster) {
            throw "AKS Arc cluster '$ClusterName' was not found in resource group '$ResourceGroupName'. Re-run with -CreateLocalCluster and the required custom location / logical network parameters, or create it ahead of time."
        }

        $customLocationResourceGroup = if ($LocalCustomLocationResourceGroup) { $LocalCustomLocationResourceGroup } else { $ResourceGroupName }
        $resolvedCustomLocationId = if ($LocalCustomLocationId) {
            $LocalCustomLocationId
        } else {
            Resolve-AzureResourceId -ResourceName $LocalCustomLocationName -ResourceGroupName $customLocationResourceGroup -ResourceType 'Microsoft.ExtendedLocation/customLocations' -Description 'custom location'
        }

        $resolvedLogicalNetworkId = if ($LocalLogicalNetworkId) {
            $LocalLogicalNetworkId
        } else {
            Resolve-AzureResourceId -ResourceName $LocalLogicalNetworkName -ResourceGroupName $ResourceGroupName -ResourceType 'Microsoft.AzureStackHCI/logicalNetworks' -Description 'logical network'
        }

        $createArgs = @(
            'aksarc', 'create',
            '--resource-group', $ResourceGroupName,
            '--name', $ClusterName,
            '--custom-location', $resolvedCustomLocationId,
            '--vnet-ids', $resolvedLogicalNetworkId,
            '--node-count', $LocalNodeCount,
            '--control-plane-count', $LocalControlPlaneCount,
            '--load-balancer-count', $LocalLoadBalancerCount,
            '--only-show-errors'
        )

        if ($LocalControlPlaneIp) {
            $createArgs += @('--control-plane-ip', $LocalControlPlaneIp)
        }
        if ($LocalLocation) {
            $createArgs += @('--location', $LocalLocation)
        }
        if ($LocalKubernetesVersion) {
            $createArgs += @('--kubernetes-version', $LocalKubernetesVersion)
        }
        if ($LocalNodeVmSize) {
            $createArgs += @('--node-vm-size', $LocalNodeVmSize)
        }
        if ($LocalControlPlaneVmSize) {
            $createArgs += @('--control-plane-vm-size', $LocalControlPlaneVmSize)
        }

        Write-Host "  Creating AKS Arc cluster '$ClusterName'..." -ForegroundColor DarkGray
        Invoke-ExternalCommand -FailureMessage "Failed to create AKS Arc cluster '$ClusterName'." -ScriptBlock {
            az @createArgs
        }
    }

    Wait-ForAksArcClusterReady -ClusterName $ClusterName -ResourceGroupName $ResourceGroupName -TimeoutMinutes $LocalClusterWaitTimeoutMinutes -PollIntervalSeconds $LocalClusterPollIntervalSeconds
}

Push-Location $repoRoot
try {
    foreach ($commandName in @('az', 'kubectl')) {
        if (-not (Get-Command $commandName -ErrorAction SilentlyContinue)) {
            throw "Required command '$commandName' is not available on PATH."
        }
    }

    if ($SubscriptionId) {
        Invoke-ExternalCommand -FailureMessage "Failed to select Azure subscription '$SubscriptionId'." -ScriptBlock {
            az account set --subscription $SubscriptionId --only-show-errors
        }
    }

    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host " Contoso Insurance — Hybrid Deployment" -ForegroundColor Cyan
    Write-Host " Environment: $EnvironmentName" -ForegroundColor Cyan
    Write-Host " Cloud Cluster: $CloudClusterName" -ForegroundColor Cyan
    Write-Host " Local Cluster: $LocalClusterName" -ForegroundColor Cyan
    Write-Host "============================================================" -ForegroundColor Cyan

    Write-Step 'Retrieving Azure resource details'

    $acrName = if ($AcrLoginServer) {
        ($AcrLoginServer -split '\.')[0]
    } else {
        Get-CommandOutput -FailureMessage "Failed to discover ACR in resource group '$CloudResourceGroup'." -ScriptBlock {
            az acr list --resource-group $CloudResourceGroup --query '[0].name' -o tsv --only-show-errors
        }
    }

    if (-not $AcrLoginServer) {
        $AcrLoginServer = Get-CommandOutput -FailureMessage "Failed to resolve login server for ACR '$acrName'." -ScriptBlock {
            az acr show --name $acrName --query loginServer -o tsv --only-show-errors
        }
    }

    $appInsightsCs = if ($env:APPLICATIONINSIGHTS_CONNECTION_STRING) {
        $env:APPLICATIONINSIGHTS_CONNECTION_STRING
    } else {
        Get-CommandOutput -FailureMessage "Failed to resolve Application Insights connection string in resource group '$CloudResourceGroup'." -ScriptBlock {
            az monitor app-insights component list --resource-group $CloudResourceGroup --query '[0].connectionString' -o tsv --only-show-errors
        }
    }

    $kvName = Try-GetCommandOutput -ScriptBlock {
        az keyvault list --resource-group $CloudResourceGroup --query '[0].name' -o tsv --only-show-errors
    }

    if (-not $RabbitMqPassword) {
        $RabbitMqPassword = $env:RABBITMQ_PASSWORD
    }
    if (-not $RabbitMqPassword -and $kvName) {
        $RabbitMqPassword = Try-GetCommandOutput -ScriptBlock {
            az keyvault secret show --vault-name $kvName --name rabbitmq-password --query value -o tsv --only-show-errors
        }
    }
    if (-not $RabbitMqPassword) {
        $RabbitMqPassword = New-RandomPassword
        if ($kvName) {
            Invoke-ExternalCommand -FailureMessage "Failed to store generated RabbitMQ password in Key Vault '$kvName'." -ScriptBlock {
                az keyvault secret set --vault-name $kvName --name rabbitmq-password --value $RabbitMqPassword --only-show-errors | Out-Null
            }
        }
        Write-Host "  Generated RabbitMQ password for this deployment" -ForegroundColor DarkGray
    }

    if (-not $SqlSaPassword) {
        $SqlSaPassword = $env:SQL_SA_PASSWORD
    }
    if (-not $SqlSaPassword -and $kvName) {
        $SqlSaPassword = Try-GetCommandOutput -ScriptBlock {
            az keyvault secret show --vault-name $kvName --name sql-sa-password --query value -o tsv --only-show-errors
        }
    }
    if (-not $SqlSaPassword) {
        $SqlSaPassword = New-RandomPassword
        if ($kvName) {
            Invoke-ExternalCommand -FailureMessage "Failed to store generated SQL SA password in Key Vault '$kvName'." -ScriptBlock {
                az keyvault secret set --vault-name $kvName --name sql-sa-password --value $SqlSaPassword --only-show-errors | Out-Null
            }
            Write-Host "  Generated new SQL SA password and stored in Key Vault" -ForegroundColor DarkGray
        }
    }

    if (-not $AzureAdTenantId) {
        $AzureAdTenantId = Get-CommandOutput -FailureMessage 'Failed to determine Azure tenant ID.' -ScriptBlock {
            az account show --query tenantId -o tsv --only-show-errors
        }
    }

    $agcId = Try-GetCommandOutput -ScriptBlock {
        az resource list --resource-group $CloudResourceGroup --resource-type Microsoft.ServiceNetworking/trafficControllers --query '[0].id' -o tsv --only-show-errors
    }

    if (-not $CloudSqlConnectionString) {
        if (-not $CloudSqlPrivateEndpoint) {
            throw 'Provide -CloudSqlConnectionString or -CloudSqlPrivateEndpoint for the cloud Public API to reach SQL.'
        }

        $CloudSqlConnectionString = "Server=$CloudSqlPrivateEndpoint,1433;Database=insurancedb;User Id=sa;Password=$SqlSaPassword;TrustServerCertificate=true"
    }

    if (-not $CloudRabbitMqHostName) {
        throw 'Provide -CloudRabbitMqHostName or set RABBITMQ_PRIVATE_ENDPOINT for the cloud Public API to reach RabbitMQ.'
    }

    Write-Host "  ACR: $AcrLoginServer" -ForegroundColor DarkGray
    Write-Host "  AGC: $(if ($agcId) { 'Found' } else { 'Not found' })" -ForegroundColor DarkGray

    Write-Step 'Connecting to cloud AKS cluster'
    Invoke-ExternalCommand -FailureMessage "Failed to get credentials for cloud AKS cluster '$CloudClusterName'." -ScriptBlock {
        az aks get-credentials --resource-group $CloudResourceGroup --name $CloudClusterName --overwrite-existing --only-show-errors
    }

    Write-Step 'Deploying cloud workloads'
    Invoke-ExternalCommand -FailureMessage 'Failed to install Gateway API CRDs on cloud cluster.' -ScriptBlock {
        kubectl apply -f $GatewayApiCrdUrl | Out-Null
    }

    $cloudNsPath = Join-Path $repoRoot 'k8s\cloud\namespace.yaml'
    $cloudWebPath = Join-Path $repoRoot 'k8s\cloud\web-deployment.yaml'
    $cloudApiPath = Join-Path $repoRoot 'k8s\cloud\api-deployment.yaml'
    $cloudNetworkPolicyPath = Join-Path $repoRoot 'k8s\cloud\network-policies.yaml'
    $localNsPath = Join-Path $repoRoot 'k8s\local\namespace.yaml'
    $localSqlPath = Join-Path $repoRoot 'k8s\local\sqlserver-deployment.yaml'
    $localRabbitPath = Join-Path $repoRoot 'k8s\local\rabbitmq-deployment.yaml'
    $localBackendApiPath = Join-Path $repoRoot 'k8s\local\backend-api-deployment.yaml'
    $localBackendPortalPath = Join-Path $repoRoot 'k8s\local\backend-portal-deployment.yaml'
    $localWorkersPath = Join-Path $repoRoot 'k8s\local\workers-deployment.yaml'
    $localNetworkPolicyPath = Join-Path $repoRoot 'k8s\local\network-policies.yaml'

    $cloudNs = Get-Content $cloudNsPath -Raw
    $cloudNs = $cloudNs -replace '__APPINSIGHTS_CONNECTION_STRING__', $appInsightsCs
    $cloudNs = $cloudNs -replace '__RABBITMQ_PASSWORD__', $RabbitMqPassword
    $cloudNs = $cloudNs -replace '__RABBITMQ_PRIVATE_ENDPOINT__', $CloudRabbitMqHostName
    $cloudNs = $cloudNs -replace '__SQL_CONNECTION_STRING__', $CloudSqlConnectionString
    $cloudNs | kubectl apply -f - | Out-Null

    $webManifest = Get-Content $cloudWebPath -Raw
    $webManifest = $webManifest -replace '__ACR_LOGIN_SERVER__', $AcrLoginServer
    $webManifest = $webManifest -replace '__TAG__', $Tag
    $webManifest = $webManifest -replace '__AGC_RESOURCE_ID__', $agcId
    $webManifest | kubectl apply -f - | Out-Null

    $apiManifest = Get-Content $cloudApiPath -Raw
    $apiManifest = $apiManifest -replace '__ACR_LOGIN_SERVER__', $AcrLoginServer
    $apiManifest = $apiManifest -replace '__TAG__', $Tag
    $apiManifest | kubectl apply -f - | Out-Null

    Invoke-ExternalCommand -FailureMessage 'Failed to apply cloud network policies.' -ScriptBlock {
        kubectl apply -f $cloudNetworkPolicyPath | Out-Null
    }

    Write-Host '  Cloud workloads deployed ✓' -ForegroundColor Green

    Write-Step 'Ensuring Azure Local AKS Arc cluster is ready'
    Ensure-AksArcCluster -ClusterName $LocalClusterName -ResourceGroupName $LocalResourceGroup

    if ($UseArcProxy) {
        Write-Host "  Using Arc proxy to connect to $LocalClusterName" -ForegroundColor DarkGray
        $script:proxyJob = Start-Job -ScriptBlock {
            param($ClusterName, $ResourceGroupName)
            az connectedk8s proxy -n $ClusterName -g $ResourceGroupName --only-show-errors
        } -ArgumentList $LocalClusterName, $LocalResourceGroup
        Start-Sleep -Seconds 10
    } else {
        Invoke-ExternalCommand -FailureMessage "Failed to get credentials for local AKS Arc cluster '$LocalClusterName'." -ScriptBlock {
            az aksarc get-credentials --resource-group $LocalResourceGroup --name $LocalClusterName --overwrite-existing --only-show-errors
        }
    }

    Write-Step 'Deploying local workloads'

    $localSqlConnectionString = "Server=sqlserver;Database=insurancedb;User Id=sa;Password=$SqlSaPassword;TrustServerCertificate=true"
    $localNs = Get-Content $localNsPath -Raw
    $localNs = $localNs -replace '__APPINSIGHTS_CONNECTION_STRING__', $appInsightsCs
    $localNs = $localNs -replace '__RABBITMQ_PASSWORD__', $RabbitMqPassword
    $localNs = $localNs -replace '__SQL_CONNECTION_STRING__', $localSqlConnectionString
    $localNs = $localNs -replace '__AZURE_AD_INSTANCE__', $AzureAdInstance
    $localNs = $localNs -replace '__AZURE_AD_TENANT_ID__', $AzureAdTenantId
    $localNs = $localNs -replace '__AZURE_AD_DOMAIN__', $AzureAdDomain
    $localNs = $localNs -replace '__BACKEND_PORTAL_AZURE_AD_CLIENT_ID__', $BackendPortalAzureAdClientId
    $localNs = $localNs -replace '__BACKEND_PORTAL_AZURE_AD_CALLBACK_PATH__', $BackendPortalAzureAdCallbackPath
    $localNs = $localNs -replace '__BACKEND_API_AZURE_AD_CLIENT_ID__', $BackendApiAzureAdClientId
    $localNs = $localNs -replace '__BACKEND_API_AZURE_AD_AUDIENCE__', $BackendApiAzureAdAudience
    $localNs = $localNs -replace '__BACKEND_PORTAL_AZURE_AD_CLIENT_SECRET__', $BackendPortalAzureAdClientSecret
    $localNs | kubectl apply -f - | Out-Null

    $acrUsername = $env:ACR_USERNAME
    $acrPassword = $env:ACR_PASSWORD
    if ((-not $acrUsername -or -not $acrPassword) -and $acrName) {
        $acrCredentialsJson = Try-GetCommandOutput -ScriptBlock {
            az acr credential show --name $acrName --output json --only-show-errors
        }
        if ($acrCredentialsJson) {
            $acrCredentials = $acrCredentialsJson | ConvertFrom-Json
            $acrUsername = $acrCredentials.username
            $acrPassword = $acrCredentials.passwords[0].value
        }
    }
    if ($acrUsername -and $acrPassword) {
        kubectl create secret docker-registry $ImagePullSecretName --namespace $Namespace --docker-server=$AcrLoginServer --docker-username=$acrUsername --docker-password=$acrPassword --dry-run=client -o yaml | kubectl apply -f - | Out-Null
    }

    $sqlManifest = Get-Content $localSqlPath -Raw
    $sqlManifest = $sqlManifest -replace '__SQL_SA_PASSWORD__', $SqlSaPassword
    $sqlManifest | kubectl apply -f - | Out-Null

    Invoke-ExternalCommand -FailureMessage 'Failed to deploy RabbitMQ to local cluster.' -ScriptBlock {
        kubectl apply -f $localRabbitPath | Out-Null
    }

    foreach ($manifestPath in @($localBackendApiPath, $localBackendPortalPath, $localWorkersPath)) {
        $content = Get-Content $manifestPath -Raw
        $content = $content -replace '__ACR_LOGIN_SERVER__', $AcrLoginServer
        $content = $content -replace '__TAG__', $Tag
        $content | kubectl apply -f - | Out-Null
    }

    Invoke-ExternalCommand -FailureMessage 'Failed to apply local network policies.' -ScriptBlock {
        kubectl apply -f $localNetworkPolicyPath | Out-Null
    }

    Write-Host '  Local workloads deployed ✓' -ForegroundColor Green

    Write-Step 'Waiting for SQL Server and initialization'
    Invoke-ExternalCommand -FailureMessage 'SQL Server did not become ready on the local cluster.' -ScriptBlock {
        kubectl wait --for=condition=ready pod -l app=sqlserver -n $Namespace --timeout=180s | Out-Null
    }
    Invoke-ExternalCommand -FailureMessage 'SQL Server initialization job did not complete.' -ScriptBlock {
        kubectl wait --for=condition=complete job/sqlserver-init -n $Namespace --timeout=180s | Out-Null
    }

    Write-Step 'Verifying rollouts'
    foreach ($rolloutTarget in @(
        'statefulset/rabbitmq',
        'statefulset/sqlserver',
        'deployment/backendapi',
        'deployment/backendportal',
        'deployment/worker-claims',
        'deployment/worker-quotes',
        'deployment/worker-projections'
    )) {
        Invoke-ExternalCommand -FailureMessage "Rollout check failed for local workload '$rolloutTarget'." -ScriptBlock {
            kubectl rollout status $rolloutTarget -n $Namespace --timeout=180s | Out-Null
        }
    }
    Write-Host '  All local deployments ready ✓' -ForegroundColor Green

    Invoke-ExternalCommand -FailureMessage "Failed to switch back to cloud AKS cluster '$CloudClusterName'." -ScriptBlock {
        az aks get-credentials --resource-group $CloudResourceGroup --name $CloudClusterName --overwrite-existing --only-show-errors
    }
    foreach ($rolloutTarget in @('deployment/webfrontend', 'deployment/publicapi')) {
        Invoke-ExternalCommand -FailureMessage "Rollout check failed for cloud workload '$rolloutTarget'." -ScriptBlock {
            kubectl rollout status $rolloutTarget -n $Namespace --timeout=180s | Out-Null
        }
    }
    Write-Host '  All cloud deployments ready ✓' -ForegroundColor Green

    Write-Host "`n============================================================" -ForegroundColor Cyan
    Write-Host ' Hybrid Deployment Complete!' -ForegroundColor Green
    Write-Host '============================================================' -ForegroundColor Cyan
    Write-Host " Cloud cluster ($CloudClusterName):" -ForegroundColor White
    Write-Host '   • Web Frontend (internet-facing via AGC)' -ForegroundColor DarkGray
    Write-Host '   • Public API (intake layer for local services)' -ForegroundColor DarkGray
    Write-Host " Local cluster ($LocalClusterName):" -ForegroundColor White
    Write-Host '   • Backend Portal and Backend API' -ForegroundColor DarkGray
    Write-Host '   • Workers: claims, quotes, projections' -ForegroundColor DarkGray
    Write-Host '   • RabbitMQ and SQL Server' -ForegroundColor DarkGray
    Write-Host ' Cross-cluster connectivity: private networking required' -ForegroundColor DarkGray
    Write-Host ' Fleet Manager: unified management across both clusters' -ForegroundColor DarkGray
    Write-Host '============================================================' -ForegroundColor Cyan
}
finally {
    if ($null -ne $script:proxyJob) {
        Stop-Job -Job $script:proxyJob -ErrorAction SilentlyContinue | Out-Null
        Remove-Job -Job $script:proxyJob -Force -ErrorAction SilentlyContinue | Out-Null
    }

    Pop-Location
}
