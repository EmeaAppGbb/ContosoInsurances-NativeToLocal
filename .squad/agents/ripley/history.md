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
