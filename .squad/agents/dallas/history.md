# Dallas — History

## Project Context
- **Project:** ContosoInsurances-NativeToLocal
- **User:** Marco Antonio Silva
- **Stack:** .NET 10 Aspire, Blazor, ASP.NET Core APIs, Azure SQL (Managed Instance), RabbitMQ, AKS, Bicep/AZD
- **Description:** Enterprise Contoso Insurance app. Cloud-native on Azure, with migration paths to Azure Local (connected + disconnected).

## Learnings & Updates

### Architecture Phase (2026-04-25)
- Full Aspire solution scaffolded: 6 source + 5 test projects
- API: `ContosoInsurance.Api` (ASP.NET Core Minimal API, REST endpoints)
- Data layer: `ContosoInsurance.Data` (EF Core DbContext, domain models)
- Domain entities: Customer, Policy (with Types), Claim (with state machine), Quote
- Minimal API pattern: endpoint classes per entity (CustomerEndpoints, PolicyEndpoints, ClaimEndpoints, QuoteEndpoints)
- AppHost orchestration: SQL Server (persistent), RabbitMQ (persistent), all services with WaitFor
- Aspire components: AddSqlServerDbContext, AddRabbitMQClient for automatic wiring
- Build verified clean: 0 errors
- **Fanout task:** Complete backend services layer, DTOs, seed data, endpoint implementations

