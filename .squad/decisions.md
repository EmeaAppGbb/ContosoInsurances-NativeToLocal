# Squad Decisions

## Active Decisions

### Test Infrastructure & Conventions

**Author:** Brett (Tester)  
**Date:** 2026-04-25  
**Status:** Implemented

#### 1. SQLite In-Memory for Test Database
Used `Microsoft.EntityFrameworkCore.Sqlite` with `:memory:` connections instead of EF InMemory provider. SQLite enforces unique indexes and constraints, giving more realistic testing.

#### 2. NSubstitute for Mocking
Chose NSubstitute over Moq — cleaner syntax, active maintenance, no Castle.DynamicProxy issues on .NET 10.

#### 3. WebApplicationFactory + Service Replacement Pattern
API integration tests use `WebApplicationFactory<Program>` with service descriptor removal/replacement in `ConfigureWebHost`. This replaces Aspire-managed SQL Server and RabbitMQ with test doubles.

#### 4. FluentAssertions for Readability
All assertions use FluentAssertions — `.Should().Be()`, `.Should().StartWith()`, etc.

#### 5. AppHost Tests Kept Minimal
Aspire AppHost integration tests require real container infrastructure (Docker). Left with a single compilation-verification test. Full E2E tests should run in CI with Docker available.

**Team Impact:** Anyone adding new endpoints should follow established pattern (DTOs in request, assert on response DTO type). Claims tests must create Active policy first (Draft→Activate→submit claim). The `public partial class Program { }` line in API Program.cs is required for WebApplicationFactory.

---

### Backend Services Architecture

**Author:** Dallas (Backend Dev)  
**Date:** 2026-04-25  
**Status:** Implemented

#### Design Decisions
1. **Services layer between endpoints and DbContext** — all business logic (validation, domain rules, RabbitMQ publishing) lives in scoped services. Endpoints are thin delegates.
2. **DTOs for all request/response** — no EF models cross the API boundary. Prevents circular serialization and decouples API contract from data model.
3. **Claim status machine** — transitions are validated (Submitted→UnderReview→Approved/Denied→Paid→Closed). Invalid transitions return 409.
4. **Premium calculation** — centralized in PolicyService as `internal static`, reused by QuoteService. Simple rate × coverage / 12.
5. **Global exception handler middleware** — maps domain exceptions to proper HTTP status codes (404, 409, 400, 500).

**Team Impact:** All team members building UI should use the DTO shapes, not the EF models. The Blazor frontend can rely on consistent error response format (problem+json). RabbitMQ event publishing is now inside ClaimService, not the endpoint.

---

### Frontend CSS Design System & Architecture

**Author:** Lambert (Frontend Dev)  
**Date:** 2026-04-25  
**Status:** Implemented

#### Decision
Built a custom CSS design system (`ci-` prefix) layered on top of Bootstrap, rather than replacing Bootstrap or using a component library.

#### Rationale
- Bootstrap provides responsive grid and base utilities; custom CSS adds enterprise Contoso branding
- `ci-` prefix avoids class name collisions with Bootstrap
- No external dependencies added — keeps bundle size minimal
- Design tokens in CSS custom properties (`--ci-navy`, `--ci-teal`, etc.) for consistent theming

**Team Impact:** All team members should use `ci-` prefixed classes for custom styling. Brand colors defined as CSS variables in `wwwroot/app.css`. Data models referenced via ProjectReference to ContosoInsurance.Data (no duplicate DTOs in Web). API integration uses `HttpClientFactory.CreateClient("api")` pattern consistently.

---

### Azure SQL Database vs SQL Managed Instance

**Date:** 2026-04-25  
**Author:** Parker (Infra/DevOps)  
**Status:** Implemented (with path to MI)

#### Context
The architecture calls for Azure SQL Managed Instance for full SQL Server compatibility and VNet-native deployment. However, SQL MI has high minimum cost (~$350/mo) and long provisioning times (4-6 hours), making it impractical for dev/test environments.

#### Decision
Use **Azure SQL Database (Serverless General Purpose)** for the initial deployment. The Bicep module is structured so that switching to SQL Managed Instance requires replacing only `infra/modules/sql.bicep` — all consumers reference the module outputs (connection string, server FQDN) which remain the same shape.

#### Consequences
- Dev/test deployments are fast and cheap (serverless auto-pause at 60 min idle)
- Production Azure Local deployments should switch to SQL MI for full compatibility
- No impact on application code — connection strings are identical

**Team Impact:** No code changes needed; EF Core works identically against both.

---

### Private AKS Cluster with Application Gateway Ingress

**Date:** 2026-04-25  
**Author:** Parker (Infra/DevOps)  
**Status:** Implemented

#### Context
The Web frontend must be internet-accessible while all other services remain private.

#### Decision
- AKS API server is private (no public endpoint)
- All services use ClusterIP (internal) — no public IPs on any pod/service
- Web frontend uses an internal LoadBalancer service
- Application Gateway WAF v2 is the sole public-facing resource, routing to the web frontend's internal LB
- Calico network policies enforce zero-trust pod-to-pod communication

#### Consequences
- Requires VPN/bastion or authorized IP ranges to manage the cluster
- App Gateway adds ~$250/mo cost but provides WAF (OWASP 3.2) and bot protection
- CI/CD pipeline needs a self-hosted runner or VPN connectivity for kubectl access to private cluster

---

### Architecture Decision — Solution Structure & Aspire Orchestration

**Author:** Ripley (Lead / Architect)  
**Date:** 2026-04-25  
**Status:** Implemented

#### Decision

Created the full .NET 10 Aspire solution structure for Contoso Insurance with the following architecture:

##### Solution Layout
```
ContosoInsurance.slnx
├── src/
│   ├── ContosoInsurance.AppHost          — Aspire orchestrator (SQL Server, RabbitMQ, all services)
│   ├── ContosoInsurance.ServiceDefaults  — OpenTelemetry, health checks, resilience, service discovery
│   ├── ContosoInsurance.Api              — ASP.NET Core Minimal API (REST endpoints)
│   ├── ContosoInsurance.Web              — Blazor Server (interactive) frontend
│   ├── ContosoInsurance.Worker           — Background worker (RabbitMQ consumer)
│   └── ContosoInsurance.Data             — EF Core DbContext, domain models, enums
└── tests/
    ├── ContosoInsurance.Api.Tests
    ├── ContosoInsurance.Worker.Tests
    ├── ContosoInsurance.Web.Tests
    ├── ContosoInsurance.Data.Tests
    └── ContosoInsurance.AppHost.Tests    — Aspire integration tests (xUnit)
```

##### Key Architectural Decisions

1. **Aspire AppHost orchestrates everything**: SQL Server (container, persistent lifetime), RabbitMQ (with management plugin, persistent lifetime), API, Worker, and Web frontend. Services use `WaitFor` to ensure dependencies are ready.

2. **Minimal API pattern** for ContosoInsurance.Api: Endpoint classes organized by entity (CustomerEndpoints, PolicyEndpoints, ClaimEndpoints, QuoteEndpoints). No controllers — keeps the API lean and fast.

3. **RabbitMQ for async claim processing**: The API publishes `ClaimSubmittedEvent` messages when claims are filed. The Worker consumes them and transitions claim status to `UnderReview`. Uses raw RabbitMQ.Client (not MassTransit) for simplicity and fewer dependencies.

4. **Shared Data project**: `InsuranceDbContext` with domain models (`Customer`, `Policy`, `Claim`, `Quote`) and enums (`PolicyType`, `PolicyStatus`, `ClaimStatus`). Referenced by both API and Worker.

5. **Aspire-managed components**: Using `AddSqlServerDbContext` and `AddRabbitMQClient` Aspire component methods in API and Worker for automatic connection string wiring, health checks, and telemetry.

6. **Web→API communication**: Blazor frontend uses a named HttpClient with Aspire service discovery (`https+http://api`) to call the backend API.

7. **ServiceDefaults**: Template-generated with OpenTelemetry (traces, metrics, logs), health checks (`/health`, `/alive`), HTTP resilience (Polly), and service discovery — all services opt in via `AddServiceDefaults()`.

8. **Database auto-migration**: In development mode, the API calls `EnsureCreatedAsync()` to bootstrap the database schema automatically.

##### Domain Model

- **Customer** → has many Policies, has many Quotes
- **Policy** → belongs to Customer, has many Claims. Types: Auto, Home, Life, Health, Travel, Business
- **Claim** → belongs to Policy. Statuses: Submitted → UnderReview → Approved/Denied → Paid → Closed
- **Quote** → belongs to Customer. Has expiration date and acceptance flag.

##### Tech Stack Versions
- .NET 10.0.203
- Aspire 13.2.4
- EF Core 10.0.7
- RabbitMQ.Client (via Aspire.RabbitMQ.Client)

##### Build Verification
✅ `dotnet build ContosoInsurance.slnx` — 0 errors, 0 warnings

## Governance

- All meaningful changes require team consensus
- Document architectural decisions here
- Keep history focused on work, decisions focused on direction
