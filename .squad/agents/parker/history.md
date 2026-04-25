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

