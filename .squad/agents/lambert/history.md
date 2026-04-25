# Lambert — History

## Project Context
- **Project:** ContosoInsurances-NativeToLocal
- **User:** Marco Antonio Silva
- **Stack:** .NET 10 Aspire, Blazor, ASP.NET Core APIs, Azure SQL (Managed Instance), RabbitMQ, AKS, Bicep/AZD
- **Description:** Enterprise Contoso Insurance app. Cloud-native on Azure, with migration paths to Azure Local (connected + disconnected).

## Learnings & Updates

### Architecture Phase (2026-04-25)
- Full Aspire solution scaffolded: 6 source + 5 test projects
- Web frontend: `ContosoInsurance.Web` (Blazor Server, interactive)
- API discoverable via Aspire: uses named HttpClient with `https+http://api` service discovery
- Domain model: Customer, Policy (Auto/Home/Life/Health/Travel/Business), Claim, Quote
- API pattern: Minimal API with endpoint classes (no controllers)
- **Fanout task:** Build enterprise Blazor UI pages (Home, Dashboard, Policies, Claims, Quotes, Customers) with professional CSS

