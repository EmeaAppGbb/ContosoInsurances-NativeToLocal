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

### Frontend Build (2026-04-25)
- Built complete enterprise Contoso Insurance frontend: 8 pages, full custom CSS design system, API integration
- Added ProjectReference to ContosoInsurance.Data — shared models/enums, no local DTOs
- Custom CSS design system with `ci-` prefix (700+ lines): brand colors (navy/teal/gold), cards, tables, badges, forms, hero, stats
- Pages: Home (hero + features + stats + testimonials + CTA), Dashboard (metrics + recent activity + quick actions), Policies (filterable list + detail view), Claims (list + multi-step file-a-claim form), Quotes (list + request form), Customers (directory + add form)
- All interactive pages use `@rendermode InteractiveServer` for event handlers
- Razor gotcha: `@{ }` inside conditionals causes RZ1010 — use computed properties instead
- `app.css` at `wwwroot/app.css` (not `wwwroot/css/`) — referenced via `@Assets["app.css"]`
- Deleted Counter.razor and Weather.razor (default templates)

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

- Full stack frontend (Blazor + CSS design system) integrates seamlessly with backend DTOs
- Custom CSS design system scales well for enterprise branding without bloat
- API integration patterns established for future page development