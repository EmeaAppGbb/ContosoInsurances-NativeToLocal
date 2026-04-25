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
