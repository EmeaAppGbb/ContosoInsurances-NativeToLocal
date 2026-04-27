# Parker — History

## Project Context
- **Project:** ContosoInsurances-NativeToLocal
- **User:** Marco Antonio Silva
- **Stack:** .NET 10 Aspire, Blazor, ASP.NET Core APIs, Azure SQL (Managed Instance), RabbitMQ, AKS, Bicep/AZD
- **Description:** Enterprise Contoso Insurance app. Cloud-native on Azure, with migration paths to Azure Local (connected + disconnected).

## Learnings & Updates

### Architecture Phase (2026-04-25)
- Full .NET 10 Aspire solution scaffolded (6 source + 5 test projects)
- Cloud-native, enterprise-grade architecture ready for hybrid Azure deployments
- Service mesh: AppHost orchestrates API, Worker, Web with Aspire service discovery
- Data dependencies: SQL Server (managed instance on Azure, Aspire container in dev)
- Async messaging: RabbitMQ (managed or Aspire container in dev)
- Tech versions: .NET 10.0.203, Aspire 13.2.4, EF Core 10.0.7
- Build verified clean: 0 errors
- **Fanout task:** Write Bicep/AZD infrastructure code (AKS, Azure SQL MI, VNet, private endpoints, ACR, Key Vault)

### Infrastructure Phase (2026-07-15)
- Created full IaC scaffold: `azure.yaml`, `infra/` (7 Bicep modules), `k8s/` (8 manifests), CI/CD pipeline
- Aspire connection names: `insurancedb` (SQL), `messaging` (RabbitMQ) — K8s env vars must match
- Azure CNI needs /20 subnet for AKS pod IPs; App Gateway WAF v2 needs dedicated subnet + GatewayManager NSG
- Chose Azure SQL Database serverless over MI for cost; MI is production target for Azure Local
- All PaaS (ACR, SQL, KV) use private endpoints + DNS zone links; only App Gateway has a public IP
- Network policies enforce zero-trust: default-deny + explicit allow per service pair
- Key Vault uses RBAC (not access policies); deployer gets admin, AKS kubelet gets secrets-user

## FULL BUILD MILESTONE (2026-04-25T03:20Z)

✅ **ALL DELIVERABLES COMPLETE:**
- Frontend: 8 interactive pages, 700+ line custom CSS, full API integration
- Backend: 4 services, all CRUD+domain endpoints, seed data, RabbitMQ integration  
- Infrastructure: 21 Bicep modules, K8s manifests, CI/CD pipeline, zero-trust policies
- Testing: 72 tests (all passing), comprehensive coverage
- Documentation: Comprehensive README, ASCII diagram, migration roadmap

✅ **Build Status:** 0 errors, 0 warnings  
✅ **Test Status:** 72 tests, 100% passing  
✅ **Decisions:** Consolidated and team-aligned  
✅ **Ready for:** Azure deployment, monitoring, scaling

## Learnings

- Modular Bicep design (module outputs referenced by consumers) allows safe infrastructure swaps (e.g., SQL DB→MI)
- Private cluster + App Gateway pattern provides strong security posture while maintaining accessibility
- Zero-trust network policies (Calico) require explicit allow rules but prevent lateral movement

### Azure Local Connected Mode Migration (2026-07-15)
- Migrated all infra from cloud AKS to Azure Local connected mode on `local-connected` branch
- targetScope changed from `subscription` to `resourceGroup` — Azure Local resources deploy into existing RGs
- AKS → Arc-enabled K8s: cluster exists on-prem, Bicep configures Arc extensions (monitoring, policy, KV secrets, Flux GitOps)
- Azure SQL DB → Arc SQL MI: deployed via Custom Location to Azure Local hardware; same connection string shape
- App Gateway → NGINX Ingress + MetalLB: on-prem has no PaaS L7 LB; ModSecurity replaces WAF OWASP rules
- Private endpoints REMOVED: on-prem cluster reaches Azure PaaS (ACR, KV, Monitor) over internet/ExpressRoute
- ACR auth: AKS AcrPull managed identity → imagePullSecrets with service principal/token
- storageClassName: `managed-csi` (Azure Disk) → `default` (Azure Local Storage Spaces Direct)
- Network policies: SQL egress changed from ipBlock CIDR (private endpoint) to namespaceSelector (arc-data namespace)
- Bicep `reference()` function can't be used for `location` property — must pass location as parameter
- Connected mode keeps ACR, Key Vault, and Monitor in Azure cloud — only compute + data move on-prem
- Application code is 100% unchanged — same containers, same env vars, same connection string format

### AGC Migration — Deprecation Refactor (2026-07-15)
- Replaced App Gateway WAF v2 with AGC (Application Gateway for Containers) on `main` branch
- Replaced NGINX Ingress + MetalLB with AGC on `local-connected` branch
- AGIC and NGINX Ingress Controller both RETIRED March 2026; AGC is the GA successor (Nov 2025)
- Kubernetes version bumped from 1.30 → 1.35 (1.30 deprecated March 2026)
- AKS API version: 2024-06-02-preview → 2024-09-01 (stable)
- omsagent AKS addon → Azure Monitor managed monitoring addon (azureMonitorProfile)
- ALB Controller: AKS gets managed addon, Arc-enabled K8s gets Arc extension (Microsoft.NetworkFunction.ALBController)
- Gateway API (Gateway + HTTPRoute CRDs) replaces legacy Ingress API for both branches
- AGC subnet requires delegation to Microsoft.ServiceNetworking/trafficControllers
- Connected mode AGC: Internet → AGC (Azure cloud) → Arc tunnel → on-prem pods — no inbound firewall rules needed!
- AGC provides unified ingress pattern for BOTH cloud AKS and Arc-enabled K8s — major simplification
- Deleted k8s/ingress-nginx.yaml and k8s/metallb-config.yaml (retired components)
- Network policies updated: ingress-nginx namespace → azure-alb-system namespace
- Gateway API CRDs must be installed separately (v1.2.1 standard-install.yaml)
