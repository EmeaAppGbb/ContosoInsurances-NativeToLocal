# Decisions

## Switch Azure SQL to Entra ID-Only Authentication

**Date:** 2025-07-22  
**Author:** Parker (Infra/DevOps)  
**Status:** Implemented  
**Requested by:** Marco Antonio Silva  

### Context

MCAPS subscription policies block Azure SQL Server creation when SQL authentication is enabled. The `azd up` deployment was failing at the SQL Server resource.

### Decision

Switch from SQL username/password authentication to **Entra ID-only authentication** on the Azure SQL logical server.

### Changes Made

| File | Change |
|------|--------|
| `infra/modules/sql.bicep` | Removed `adminLogin`/`adminPassword` params; added `entraAdminObjectId`/`entraAdminDisplayName`; set `azureADOnlyAuthentication: true` in `administrators` block; updated connection string to use `Authentication=Active Directory Default`; bumped API to `2024-05-01-preview` |
| `infra/main.bicep` | Removed `sqlAdminLogin`/`sqlAdminPassword` params; added `sqlEntraAdminDisplayName`; passes `principalId` as `entraAdminObjectId` to SQL module |
| `infra/main.parameters.json` | Removed `sqlAdminLogin` and `sqlAdminPassword` entries |

### Consequences

- **Positive:** Compliant with MCAPS policies; no passwords stored or prompted; passwordless auth via managed identity
- **Positive:** `azd up` no longer prompts for `SQL_ADMIN_PASSWORD`
- **Follow-up needed:** AKS kubelet managed identity needs SQL access granted post-deployment (via SQL `CREATE USER ... FROM EXTERNAL PROVIDER`)
- **No impact:** Private endpoint/DNS config unchanged; Key Vault still stores connection string (now passwordless)
