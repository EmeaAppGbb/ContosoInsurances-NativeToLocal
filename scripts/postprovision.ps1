#Requires -Version 7.0
<#
.SYNOPSIS
    Post-provision hook: sets up AKS access, installs Gateway API CRDs, creates namespace.
.DESCRIPTION
    Called by AZD after infrastructure provisioning completes.
    Environment variables from Bicep outputs are automatically available.
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Write-Host "=== Post-Provision: Setting up AKS cluster access ===" -ForegroundColor Cyan

# Get AKS credentials
Write-Host "Getting AKS credentials..."
az aks get-credentials `
    --resource-group $env:AZURE_RESOURCE_GROUP `
    --name $env:AZURE_AKS_CLUSTER_NAME `
    --overwrite-existing

# Install Gateway API CRDs
Write-Host "Installing Gateway API CRDs..."
kubectl apply -f https://github.com/kubernetes-sigs/gateway-api/releases/download/v1.2.1/standard-install.yaml

# Create namespace (idempotent)
Write-Host "Creating namespace..."
$nsFile = Join-Path $PSScriptRoot ".." "k8s" "namespace.yaml"
kubectl apply -f $nsFile

Write-Host "=== Post-Provision complete ===" -ForegroundColor Green
