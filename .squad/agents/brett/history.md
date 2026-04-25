# Brett — History

## Project Context
- **Project:** ContosoInsurances-NativeToLocal
- **User:** Marco Antonio Silva
- **Stack:** .NET 10 Aspire, Blazor, ASP.NET Core APIs, Azure SQL (Managed Instance), RabbitMQ, AKS, Bicep/AZD
- **Description:** Enterprise Contoso Insurance app. Cloud-native on Azure, with migration paths to Azure Local (connected + disconnected).

## Learnings & Updates

### Architecture Phase (2026-04-25)
- Full Aspire solution scaffolded: 6 source + 5 test projects
- AppHost orchestrates SQL Server (persistent), RabbitMQ (persistent), all services with WaitFor chains
- Minimal API pattern (endpoint classes per entity)
- Domain model: Customer → Policies/Quotes; Policy → Claims; Claims have state machine (Submitted → UnderReview → Approved/Denied → Paid → Closed)
- Build verified: 0 errors
- **Fanout task:** Write comprehensive xUnit test suites for data layer, API integration, worker, AppHost

