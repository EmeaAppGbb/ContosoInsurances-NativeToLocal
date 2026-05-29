# Decision: AZD Hook-Based Deploy for AKS

**Author:** Parker (Infra/DevOps)  
**Date:** 2026-07-15  
**Status:** Implemented

## Context

`azd up` failed at the deploy phase because `azure.yaml` used the Aspire AppHost pattern with `host: aks`. AZD's Aspire integration only supports Container Apps ("Aspire services must be configured to target the container app host at this time"). Our infra deploys AKS, not Container Apps.

## Decision

Replace the Aspire service definition in `azure.yaml` with AZD lifecycle hooks:

1. **`postprovision` hook** — Gets AKS credentials, installs Gateway API CRDs, creates the namespace.
2. **`deploy` hook** — Builds Docker images, pushes to ACR, substitutes K8s manifest placeholders, applies manifests in order.

All hooks use `shell: pwsh` (Windows-compatible). Deploy script is idempotent.

## Files Changed

- `azure.yaml` — Removed `services:` block, added `hooks:` block
- `scripts/postprovision.ps1` — New: post-provision setup
- `scripts/deploy.ps1` — New: full build + deploy pipeline
- `src/ContosoInsurance.Api/Dockerfile` — New: multi-stage .NET 10 build
- `src/ContosoInsurance.Web/Dockerfile` — New: multi-stage .NET 10 build
- `src/ContosoInsurance.Worker/Dockerfile` — New: multi-stage .NET 10 build

## Consequences

- `azd provision` works unchanged (Bicep infra)
- `azd deploy` now triggers the hook script instead of Aspire-based deploy
- `azd up` works end-to-end (provision + deploy)
- Docker Desktop required on dev machine for image builds
- Secrets never written to source files — substitution in temp dir, cleaned up after apply

## Team Impact

- **Lambert/Dallas:** No application code changes needed. Aspire AppHost still works for local dev (`dotnet run` from AppHost).
- **Brett:** Tests unchanged. CI can run `azd up` for E2E testing.
- **All:** Use `azd deploy` to redeploy after code changes (skips infra provision).
