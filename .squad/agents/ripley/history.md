# Ripley — History

## Project Context
- **Project:** ContosoInsurances-NativeToLocal
- **User:** Marco Antonio Silva
- **Stack:** .NET 10 Aspire, Blazor, ASP.NET Core APIs, Azure SQL (Managed Instance), RabbitMQ, AKS, Bicep/AZD
- **Description:** Enterprise Contoso Insurance app. Cloud-native on Azure, with migration paths to Azure Local (connected + disconnected).
- **Branches:** main (Azure cloud), local-connected, local-disconnected

## Learnings

- Solution uses .slnx format (new XML-based solution format in .NET 10), not classic .sln
- Aspire 13.2.4 templates installed; AppHost uses `Aspire.AppHost.Sdk/13.2.4`
- .NET 10.0.203 SDK installed on this machine
- EF Core 10.0.7 is the current version for .NET 10
- Build command: `dotnet build ContosoInsurance.slnx` from repo root
- ServiceDefaults template comes fully configured with OpenTelemetry, health checks, resilience, and service discovery
- AppHost orchestrates: SQL Server (container), RabbitMQ (with management plugin), API, Worker, Web frontend
- Domain models: Customer, Policy, Claim, Quote with proper EF Core relationships
- API uses Minimal API pattern with endpoint classes per entity
- Worker uses raw RabbitMQ.Client for claim event processing
- Web frontend connects to API via Aspire service discovery (`https+http://api`)

### Architecture Phase Complete (2026-04-25)
- Full solution structure designed and scaffolded (6 source + 5 test projects)
- Aspire orchestration verified: AppHost with SQL Server & RabbitMQ (persistent), all services with WaitFor chains
- Domain model finalized and documented
- Architectural decisions published to squad decisions.md
- Build verified clean: 0 errors, 0 warnings
- Team fanout initiated: Lambert (Frontend), Dallas (Backend), Parker (Infra), Brett (Testing)
- **Next:** Document architecture in README with migration roadmap

### README Authored (2025-07-24)
- Wrote comprehensive README.md at repo root — serves as primary project guide
- Includes: ASCII architecture diagram, service communication table, security architecture
- Full migration roadmap: main (Azure) → local-connected → local-disconnected with comparison table
- Detailed project structure, tech stack, getting started (local + Azure), application features
- All content derived from actual codebase inspection (AppHost, endpoints, models, worker, service defaults)
- README is conference/customer-presentation ready

### Squad Documentation & Orchestration (2026-04-25)
- Reviewed and consolidated all agent decisions from .squad/decisions/inbox/ → decisions.md
- Wrote orchestration logs for all 5 agents (timestamp 2026-04-25T03:20Z)
- Documented full build milestone in agent history files
- Merged all team decisions with deduplication
- Wrote session log for full build phase
- Updated Scribe history with squad coordination notes

## FULL BUILD MILESTONE (2026-04-25T03:20Z)

✅ **ALL DELIVERABLES COMPLETE:**
- Frontend: 8 interactive pages, 700+ line custom CSS, full API integration
- Backend: 4 services, all CRUD+domain endpoints, seed data, RabbitMQ integration  
- Infrastructure: 21 Bicep modules, K8s manifests, CI/CD pipeline, zero-trust policies
- Testing: 72 tests (all passing), comprehensive coverage
- Documentation: Comprehensive README, ASCII diagram, migration roadmap, orchestration logs

✅ **Build Status:** 0 errors, 0 warnings  
✅ **Test Status:** 72 tests, 100% passing  
✅ **Decisions:** Consolidated, deduped, team-aligned  
✅ **Orchestration:** Logged and recorded  
✅ **Ready for:** Azure deployment, monitoring, scaling, customer delivery

## Learnings

- Solution uses .slnx format (new XML-based solution format in .NET 10), not classic .sln
- Aspire 13.2.4 templates installed; AppHost uses `Aspire.AppHost.Sdk/13.2.4`
- .NET 10.0.203 SDK installed on this machine
- EF Core 10.0.7 is the current version for .NET 10
- Build command: `dotnet build ContosoInsurance.slnx` from repo root
- ServiceDefaults template comes fully configured with OpenTelemetry, health checks, resilience, and service discovery
- AppHost orchestrates: SQL Server (container), RabbitMQ (with management plugin), API, Worker, Web frontend
- Domain models: Customer, Policy, Claim, Quote with proper EF Core relationships
- API uses Minimal API pattern with endpoint classes per entity
- Worker uses raw RabbitMQ.Client for claim event processing
- Web frontend connects to API via Aspire service discovery (`https+http://api`)
- Squad workflow: charter + agents + parallel fanout + orchestration log + decision consolidation = high-velocity full-stack delivery
- Backend portal should be a separate Blazor application from the public web app so the trust boundary, deployment boundary, and auth model stay clean for hybrid cloud/local deployment
- The target hybrid split is public web + public-safe API + public projection data in Azure, with backend portal + private workflow API + private data + workers on Azure Local connected through versioned RabbitMQ contracts
- RabbitMQ should evolve from a single queue implementation into versioned topic exchanges with outbox, idempotent consumers, retry queues, and sanitized projection events back to the public side

### README Updated for local-connected Branch (2025-07-24)
- Rewrote README.md (608 insertions, 98 deletions) to serve as the primary guide for Azure Local connected mode
- Added prominent branch banner with cross-reference to `main` for cloud version
- Created detailed ASCII topology diagram showing on-prem (Arc K8s, Arc SQL MI, RabbitMQ, NGINX/MetalLB) vs cloud (ACR, KV, Monitor, Arc control plane)
- Expanded local-connected migration roadmap: change rationale table, resource comparison, cost implications, operational differences
- Added 8-step Migration Guide: HCI setup → Arc → K8s → SQL MI → extensions → deploy → ingress → validate
- Updated solution structure to document `infra/` and `k8s/` directories
- Key learning: The split between on-prem and cloud is the core concept — compute/data local, management/observability in Azure
- Application code is identical across branches; only infra/ and k8s/ change

### AGC Migration — README Updates (2025-07-25)
- Updated README.md on BOTH `main` and `local-connected` branches for April 2026 AGC migration
- **main branch:** Replaced App Gateway WAFv2 + AGIC with AGC in architecture diagram, tech stack, prerequisites, deploy steps. Added "Why AGC?" and "Deprecated Components" sections with full deprecation timeline.
- **local-connected branch:** Replaced NGINX Ingress + MetalLB with AGC + ALB Controller Arc extension throughout. Rewrote topology diagram to show AGC in Azure cloud with Arc tunnel to on-prem pods. Rewrote Step 7 (Configure AGC with ALB Controller). Updated all comparison tables.
- Key architectural insight: With AGC, the ingress layer is now IDENTICAL between cloud AKS and on-prem Arc K8s — this is a massive simplification for hybrid architectures
- AGC uses `Microsoft.ServiceNetworking/trafficControllers` resource type, Gateway API (`Gateway` + `HTTPRoute`), ALB Controller (AKS add-on or Arc extension)
- Deprecation timeline documented: AGIC retired March 2026, NGINX Ingress (community) retired March 2026, K8s 1.30 EOL March 2026, AGC GA since Nov 2025
- Merge strategy: merged main→local-connected with conflict resolution, then applied local-connected specific changes on top

