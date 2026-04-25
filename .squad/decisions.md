# Squad Decisions

## Active Decisions

### Architecture Decision — Solution Structure & Aspire Orchestration

**Author:** Ripley (Lead / Architect)  
**Date:** 2025-07-25  
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
