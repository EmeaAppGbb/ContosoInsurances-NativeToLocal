#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$ConfigFile = (Join-Path $PSScriptRoot 'azure-local-params.json'),
    [switch]$SkipCliExtensionInstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

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

function Ensure-AzExtension {
    param([string]$Name)

    $installed = (& az extension show --name $Name 2>$null) | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Invoke-ExternalCommand -FailureMessage "Failed to install Azure CLI extension '$Name'." -ScriptBlock {
            az extension add --name $Name --upgrade --only-show-errors
        }
    }
    elseif (-not $SkipCliExtensionInstall) {
        Invoke-ExternalCommand -FailureMessage "Failed to update Azure CLI extension '$Name'." -ScriptBlock {
            az extension update --name $Name --only-show-errors
        }
    }
}

function Ensure-ProviderRegistration {
    param([string]$Namespace)

    $state = Get-CommandOutput -FailureMessage "Failed to query provider '$Namespace'." -ScriptBlock {
        az provider show --namespace $Namespace --query registrationState -o tsv
    }

    if ($state -ne 'Registered') {
        Invoke-ExternalCommand -FailureMessage "Failed to register provider '$Namespace'." -ScriptBlock {
            az provider register --namespace $Namespace --wait --only-show-errors
        }
    }
}

function Ensure-ResourceGroup {
    param(
        [string]$Name,
        [string]$Location
    )

    $exists = Get-CommandOutput -FailureMessage "Failed to query resource group '$Name'." -ScriptBlock {
        az group exists --name $Name
    }

    if ($exists -eq 'true') {
        Write-Host "Resource group '$Name' already exists."
        return
    }

    Invoke-ExternalCommand -FailureMessage "Failed to create resource group '$Name'." -ScriptBlock {
        az group create --name $Name --location $Location --output table --only-show-errors
    }
}

function Ensure-ConnectedCluster {
    param(
        [pscustomobject]$Config,
        [string]$ResourceGroup,
        [string]$Location
    )

    $clusterName = $Config.azureLocal.connectedClusterName
    $show = & az connectedk8s show --resource-group $ResourceGroup --name $clusterName --query id -o tsv 2>$null
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($show)) {
        Write-Host "Azure Arc-enabled Kubernetes resource '$clusterName' already exists."
        return $show.Trim()
    }

    if ([string]::IsNullOrWhiteSpace($Config.azureLocal.kubeContext)) {
        throw "azureLocal.kubeContext must be set before the cluster can be connected to Azure Arc."
    }

    Write-Host "Connecting AKS Arc cluster '$clusterName' to Azure Arc using kube context '$($Config.azureLocal.kubeContext)'."
    Invoke-ExternalCommand -FailureMessage "Failed to connect '$clusterName' to Azure Arc." -ScriptBlock {
        az connectedk8s connect --resource-group $ResourceGroup --name $clusterName --location $Location --kube-config-context $Config.azureLocal.kubeContext --only-show-errors
    }

    return Get-CommandOutput -FailureMessage "Failed to read connected cluster resource ID for '$clusterName'." -ScriptBlock {
        az connectedk8s show --resource-group $ResourceGroup --name $clusterName --query id -o tsv
    }
}

function Ensure-CustomLocation {
    param(
        [pscustomobject]$Config,
        [string]$ResourceGroup,
        [string]$HostResourceId,
        [string]$Location
    )

    $name = $Config.azureLocal.customLocationName
    $existing = & az customlocation show --resource-group $ResourceGroup --name $name --query id -o tsv 2>$null
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($existing)) {
        Write-Host "Custom location '$name' already exists."
        return $existing.Trim()
    }

    Invoke-ExternalCommand -FailureMessage "Failed to create custom location '$name'." -ScriptBlock {
        az customlocation create --resource-group $ResourceGroup --name $name --location $Location --host-resource-id $HostResourceId --namespace $Config.azureLocal.arcNamespace --only-show-errors
    }

    return Get-CommandOutput -FailureMessage "Failed to read custom location '$name'." -ScriptBlock {
        az customlocation show --resource-group $ResourceGroup --name $name --query id -o tsv
    }
}

function Ensure-ArcExtensions {
    param(
        [pscustomobject]$Config,
        [string]$ResourceGroup,
        [string]$ClusterName
    )

    $extensions = @(
        @{ Name = 'azuremonitor-containers'; Type = 'microsoft.azuremonitor.containers'; Namespace = 'azure-monitor' },
        @{ Name = 'azurepolicy'; Type = 'microsoft.policyinsights'; Namespace = 'azure-policy' }
    )

    foreach ($extension in $extensions) {
        $existing = & az k8s-extension show --resource-group $ResourceGroup --cluster-name $ClusterName --cluster-type connectedClusters --name $extension.Name --query id -o tsv 2>$null
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($existing)) {
            Write-Host "Arc extension '$($extension.Name)' already exists."
            continue
        }

        Invoke-ExternalCommand -FailureMessage "Failed to install Arc extension '$($extension.Name)'." -ScriptBlock {
            az k8s-extension create --resource-group $ResourceGroup --cluster-name $ClusterName --cluster-type connectedClusters --name $extension.Name --extension-type $extension.Type --scope cluster --release-namespace $extension.Namespace --auto-upgrade true --only-show-errors
        }
    }
}

function Ensure-AcrPullRole {
    param(
        [string]$ResourceGroup,
        [string]$ClusterName,
        [string]$AcrName
    )

    $principalId = Get-CommandOutput -FailureMessage "Failed to get managed identity for connected cluster '$ClusterName'." -ScriptBlock {
        az connectedk8s show --resource-group $ResourceGroup --name $ClusterName --query identity.principalId -o tsv
    }

    $acrId = Get-CommandOutput -FailureMessage "Failed to resolve ACR '$AcrName'." -ScriptBlock {
        az acr show --name $AcrName --query id -o tsv
    }

    & az role assignment create --assignee-object-id $principalId --assignee-principal-type ServicePrincipal --role AcrPull --scope $acrId --only-show-errors 2>$null | Out-Null
    Write-Host "Ensured AcrPull role assignment for the Arc-enabled cluster identity."
}

function Ensure-PlaceholderAzureLocalResources {
    param([pscustomobject]$Config)

    Write-Host "Jumpstart normally provisions the Azure Local cluster resource and logical network during sandbox bootstrap." -ForegroundColor Yellow
    Write-Host "Expected Azure Local cluster: $($Config.azureLocal.clusterName)" -ForegroundColor Yellow
    Write-Host "Expected logical network: $($Config.network.logicalNetworkName) ($($Config.network.addressPrefix))" -ForegroundColor Yellow
    Write-Host "Expected MetalLB pool: $($Config.network.loadBalancerStartIp)-$($Config.network.loadBalancerEndIp)" -ForegroundColor Yellow
}

foreach ($commandName in @('az', 'kubectl', 'helm')) {
    if (-not (Get-Command $commandName -ErrorAction SilentlyContinue)) {
        throw "Required command '$commandName' is not available on PATH."
    }
}

$config = Read-Config -Path $ConfigFile

if (-not [string]::IsNullOrWhiteSpace($config.subscriptionId) -and $config.subscriptionId -notlike '<*') {
    Write-Step 'Selecting Azure subscription'
    Invoke-ExternalCommand -FailureMessage "Failed to set subscription '$($config.subscriptionId)'." -ScriptBlock {
        az account set --subscription $config.subscriptionId
    }
}

if (-not $SkipCliExtensionInstall) {
    Write-Step 'Ensuring Azure CLI extensions'
    foreach ($extension in @('connectedk8s', 'k8s-extension', 'customlocation', 'aksarc', 'arcappliance')) {
        Ensure-AzExtension -Name $extension
    }
}

Write-Step 'Registering Azure resource providers'
foreach ($namespace in @(
    'Microsoft.Kubernetes',
    'Microsoft.KubernetesConfiguration',
    'Microsoft.ExtendedLocation',
    'Microsoft.AzureArcData',
    'Microsoft.AzureStackHCI',
    'Microsoft.HybridContainerService',
    'Microsoft.ResourceConnector',
    'Microsoft.ContainerRegistry',
    'Microsoft.OperationalInsights',
    'Microsoft.Insights'
)) {
    Ensure-ProviderRegistration -Namespace $namespace
}

Write-Step 'Ensuring resource group'
Ensure-ResourceGroup -Name $config.resourceGroupName -Location $config.azureLocation

Write-Step 'Validating Jumpstart-provided Azure Local resources'
Ensure-PlaceholderAzureLocalResources -Config $config

Write-Step 'Connecting AKS Arc cluster to Azure Arc'
$connectedClusterId = Ensure-ConnectedCluster -Config $config -ResourceGroup $config.resourceGroupName -Location $config.arcLocation

Write-Step 'Creating custom location'
$customLocationId = Ensure-CustomLocation -Config $config -ResourceGroup $config.resourceGroupName -HostResourceId $connectedClusterId -Location $config.arcLocation

Write-Step 'Installing Arc extensions'
Ensure-ArcExtensions -Config $config -ResourceGroup $config.resourceGroupName -ClusterName $config.azureLocal.connectedClusterName

Write-Step 'Configuring ACR integration'
Ensure-AcrPullRole -ResourceGroup $config.resourceGroupName -ClusterName $config.azureLocal.connectedClusterName -AcrName $config.containerRegistry.acrName

Write-Step 'Setup summary'
Write-Host "Connected cluster resource: $connectedClusterId" -ForegroundColor Green
Write-Host "Custom location: $customLocationId" -ForegroundColor Green
Write-Host "Logical network (Jumpstart): $($config.network.logicalNetworkName)" -ForegroundColor Green
Write-Host "Load balancer pool: $($config.network.loadBalancerStartIp)-$($config.network.loadBalancerEndIp)" -ForegroundColor Green
