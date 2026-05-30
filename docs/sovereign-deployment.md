# Sovereign deployment

## What this branch targets

This branch packages the Contoso Insurance AKS deployment for a **German sovereign-style regional deployment** in **Germany West Central** by default.

In this repository, "sovereign" means the workload stays in German Azure regions to satisfy data residency and regional control requirements while preserving the same application behavior as the `main` branch.

> Germany West Central is the default because it supports the current ingress design with **Application Gateway for Containers (AGC)**. Germany North can be considered later, but AGC availability must be revalidated first.

## Why use a sovereign region

Use a sovereign regional deployment when you need to:

- keep primary data and platform resources in Germany
- align with internal or regulatory residency requirements
- preserve the same AKS-based operating model used in the public cloud branch
- minimize application changes while moving to a more constrained regional footprint

## Current sovereign-specific differences

Compared with the standard `main` branch deployment:

- **Default location:** `germanywestcentral`
- **Suggested AZD environment name:** `contoso-sovereign`
- **Ingress:** still **AGC**, because Germany West Central supports it
- **Database:** Azure SQL Database uses **provisioned General Purpose Gen5** instead of serverless because Azure SQL serverless is not currently available in Germany West Central / Germany North
- **Microsoft Entra ID endpoint:** unchanged by default (`https://login.microsoftonline.com/`) because these Germany regions are still in Azure public cloud

## Prerequisites

Before running `azd up`, make sure you have:

1. Access to an Azure subscription that can deploy into **Germany West Central**
2. Azure Developer CLI (`azd`)
3. Azure CLI (`az`)
4. `kubectl`
5. `helm`
6. Permission to create:
   - resource groups
   - AKS
   - Azure Container Registry
   - Azure SQL Database
   - Key Vault
   - private networking resources
7. A principal object ID available for `AZURE_PRINCIPAL_ID`
8. Any Entra app registration secrets required by the backend portal, if you are enabling production auth

## Deploy with AZD

From the repository root on the `sovereign` branch:

```powershell
git checkout sovereign
azd auth login
azd env new contoso-sovereign
azd env set AZURE_PRINCIPAL_ID (az ad signed-in-user show --query id -o tsv)
azd up
```

### Notes

- `infra/main.bicep` defaults to `germanywestcentral`
- `infra/main.parameters.json` is already pinned to `germanywestcentral` on this branch so `azd up` works without setting `AZURE_LOCATION`
- `.azure/config.json` suggests `contoso-sovereign` as the default environment name
- `scripts/deploy-all.ps1` still uses the public cloud Entra endpoint unless `AZURE_AD_INSTANCE` is explicitly overridden

## If you need Germany North

Germany North is not the primary target for this branch.

Before switching the location, confirm at least the following service availability in that region:

- Application Gateway for Containers (AGC)
- AKS
- Azure SQL Database private endpoint support with the selected SKU
- Azure Container Registry

If AGC is not available, you must introduce an alternative ingress path before using Germany North.

## Compliance considerations

Regional deployment helps with residency, but you should still validate:

- where logs and monitoring data are stored
- backup and disaster recovery settings
- paired-region behavior for platform services
- tenant and identity requirements for Microsoft Entra ID
- organizational policy assignments applied at subscription or management-group scope

## Operational guidance

- Keep using the same `azd up` workflow as the standard deployment
- Keep application manifests and code identical unless a regional platform limitation requires a change
- Prefer Germany West Central for production-style sovereign deployments until AGC support in Germany North is confirmed
