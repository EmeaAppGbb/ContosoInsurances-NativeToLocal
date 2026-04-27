# Decision: Replace Deprecated Ingress Components with AGC

**Author:** Parker (Infra/DevOps)
**Date:** 2026-07-15
**Status:** Implemented

## Context

Three critical infrastructure components reached end-of-life in March 2026:

1. **AGIC (Application Gateway Ingress Controller)** — RETIRED March 2026
2. **NGINX Ingress Controller (community)** — RETIRED March 2026
3. **Kubernetes 1.30** — DEPRECATED March 2026

The previous architecture used:
- `main` branch: App Gateway WAF v2 + AGIC for cloud AKS ingress
- `local-connected` branch: NGINX Ingress + MetalLB for on-prem Arc K8s ingress

## Decision

Replace all deprecated components with **Application Gateway for Containers (AGC)** and **Gateway API** across both branches:

### Main Branch (Cloud AKS)
- App Gateway WAF v2 → AGC Traffic Controller (`Microsoft.ServiceNetworking/trafficControllers`)
- AGIC addon → ALB Controller managed addon (`ingressProfile.webAppRouting`)
- Ingress API → Gateway API (Gateway + HTTPRoute CRDs)
- K8s 1.30 → 1.35, AKS API 2024-06-02-preview → 2024-09-01
- omsagent addon → Azure Monitor managed monitoring

### Local-Connected Branch (Arc-enabled K8s)
- NGINX Ingress + MetalLB → AGC + ALB Controller Arc extension
- Ingress API → Gateway API (Gateway + HTTPRoute CRDs)
- Traffic flow: Internet → AGC (Azure) → Arc tunnel → on-prem pods

## Rationale

1. **AGC works with both AKS and Arc-enabled K8s** — unified ingress pattern eliminates divergence between cloud and connected branches
2. **Connected mode AGC eliminates inbound firewall rules** — traffic flows through the outbound-only Arc tunnel, significantly simplifying on-prem network security
3. **No more on-prem MetalLB/NGINX maintenance** — Azure-managed L7 routing and WAF for on-prem workloads
4. **Gateway API is the future** — expressive, role-oriented, portable, vendor-neutral
5. **All replaced components are officially retired** — continued use would be unsupported

## Consequences

- Gateway API CRDs must be installed on clusters before deployment (v1.2.1)
- AGC subnet requires delegation to `Microsoft.ServiceNetworking/trafficControllers`
- CI/CD pipeline must substitute `__AGC_RESOURCE_ID__` into Gateway annotations
- Connected mode requires healthy Arc connectivity agent (outbound HTTPS to Azure)
- Cost: AGC replaces both App Gateway WAF v2 and NGINX/MetalLB cost lines

## Files Changed

### Main Branch
- `infra/modules/appgateway.bicep` — New AGC Traffic Controller + frontend + association
- `infra/modules/networking.bicep` — snet-appgw → snet-agc with ServiceNetworking delegation
- `infra/modules/aks.bicep` — K8s 1.35, API 2024-09-01, ALB Controller addon, Azure Monitor
- `infra/main.bicep` — Updated module params and outputs for AGC
- `k8s/web-deployment.yaml` — ClusterIP + Gateway + HTTPRoute (removed internal LB)
- `k8s/network-policies.yaml` — Updated ingress comments for AGC
- `.github/workflows/ci-cd.yml` — Gateway API CRDs + AGC ID substitution

### Local-Connected Branch
- `infra/modules/appgateway.bicep` — Real AGC deployment (was docs-only for NGINX/MetalLB)
- `infra/modules/arc-kubernetes.bicep` — Added ALB Controller Arc extension
- `infra/modules/networking.bicep` — Updated docs, removed MetalLB IP range
- `infra/main.bicep` — Added AGC module, updated comments
- `k8s/web-deployment.yaml` — Gateway API resources (replaced NGINX Ingress)
- `k8s/network-policies.yaml` — azure-alb-system namespace (was ingress-nginx)
- `k8s/arc-extensions.yaml` — ALB Controller docs, removed NGINX/MetalLB refs
- `k8s/ingress-nginx.yaml` — DELETED (NGINX retired)
- `k8s/metallb-config.yaml` — DELETED (not needed with AGC)
- `azure.yaml` — Updated deployment workflow
- `.github/workflows/ci-cd.yml` — Gateway API CRDs + AGC substitution

**Team Impact:** No application code changes. K8s manifests now use Gateway API instead of Ingress API. The infrastructure is simpler and more maintainable with a single ingress pattern across both deployment modes.
