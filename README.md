# Contoso Insurance — Cloud-Native to Azure Local

> A comprehensive guide and lab for migrating cloud-native applications from Azure to Azure Local, demonstrating both connected and disconnected deployment models.

---

## 🎯 Purpose

This repository is a **learning lab and reference architecture** for organizations planning to:

1. **Build enterprise cloud-native applications** using .NET Aspire, demonstrating modern microservices patterns with service discovery, distributed tracing, and event-driven messaging.
2. **Deploy to Azure** using AKS, Azure SQL Managed Instance, and managed infrastructure services.
3. **Migrate to Azure Local in connected mode** — shifting compute to on-premises while maintaining Azure Arc management and cloud-based monitoring.
4. **Run fully disconnected on Azure Local** — operating in air-gapped environments with no internet dependency.

Each deployment model lives on its own branch, making it easy to compare what changes at each stage and understand the trade-offs.

| Branch | Target Environment | Internet Required |
|---|---|---|
| `main` | Azure (AKS + Azure SQL MI) | ✅ Yes |
| `local-connected` | Azure Local — Connected | ⚡ Partial |
| `local-disconnected` | Azure Local — Disconnected | ❌ No |

---

## 🏗️ Architecture

The Contoso Insurance application follows a **microservices architecture** orchestrated by .NET Aspire, with clear network boundaries separating public and private services.

```
                            ┌─────────────────────────────────────────────────────────┐
                            │                    Virtual Network                      │
                            │                                                         │
  ┌──────────┐              │  ┌──────────────────┐         ┌──────────────────────┐  │
  │          │   HTTPS      │  │                  │  HTTP   │                      │  │
  │ Internet ├─────────────►│  │  🌐 Web Frontend ├────────►│  🔌 API Service      │  │
  │          │              │  │  (Blazor Server)  │         │  (Minimal APIs)      │  │
  └──────────┘              │  │  Port: 443        │         │  Internal Only       │  │
         │                  │  └──────────────────┘         └──────┬───────────────┘  │
         │                  │                                      │                   │
         ▼                  │                                      │ Publishes Events  │
  ┌──────────────┐          │                                      ▼                   │
  │ Application  │          │                               ┌──────────────────────┐  │
  │ Gateway /    │          │                               │                      │  │
  │ Ingress      │          │                               │  🐇 RabbitMQ         │  │
  │ Controller   │──────────┤                               │  (Message Broker)    │  │
  └──────────────┘          │                               └──────┬───────────────┘  │
                            │                                      │                   │
                            │                                      │ Consumes Events   │
                            │                                      ▼                   │
                            │  ┌──────────────────────────────────────────────────┐    │
                            │  │                                                  │    │
                            │  │  ⚙️  Background Worker                           │    │
                            │  │  (Claim Processing)                              │    │
                            │  │                                                  │    │
                            │  └──────────────────────┬───────────────────────────┘    │
                            │                         │                                │
                            │                         │ Read / Write                   │
                            │                         ▼                                │
                            │  ┌──────────────────────────────────────────────────┐    │
                            │  │  🗄️  Azure SQL Managed Instance                  │    │
                            │  │  (Private Endpoint — No Public Access)           │    │
                            │  └──────────────────────────────────────────────────┘    │
                            │                                                         │
                            └─────────────────────────────────────────────────────────┘
```

### Service Communication

All inter-service communication uses **.NET Aspire service discovery**:

| From | To | Protocol | Discovery Address |
|---|---|---|---|
| Web Frontend | API Service | HTTP | `https+http://api` |
| API Service | SQL Database | TCP | `insurancedb` connection string |
| API Service | RabbitMQ | AMQP | `messaging` connection string |
| Worker | SQL Database | TCP | `insurancedb` connection string |
| Worker | RabbitMQ | AMQP | `messaging` connection string |

Aspire automatically injects connection strings and service URLs at runtime — no hardcoded addresses, no manual configuration.

### Security Architecture

- **Only the Web Frontend is internet-facing**, exposed through an Application Gateway / Ingress Controller.
- The **API, Worker, RabbitMQ, and SQL database are private** — accessible only within the virtual network.
- **Network Security Groups (NSGs)** restrict lateral movement between services.
- **Private endpoints** ensure database traffic never leaves the VNet.
- **Key Vault** manages secrets, connection strings, and certificates.

---

## 🧩 Solution Structure

```
ContosoInsurance.slnx                     # .NET 10 XML solution file
│
├── src/
│   ├── ContosoInsurance.AppHost/         # 🎛️  Aspire orchestrator — defines all resources
│   ├── ContosoInsurance.Api/             # 🔌  REST API — Customers, Policies, Claims, Quotes
│   ├── ContosoInsurance.Web/             # 🌐  Blazor Server frontend — user-facing UI
│   ├── ContosoInsurance.Worker/          # ⚙️   Background worker — processes claim events
│   ├── ContosoInsurance.Data/            # 🗄️  EF Core — DbContext, models, enums, migrations
│   └── ContosoInsurance.ServiceDefaults/ # 📡  Shared config — OpenTelemetry, health, resilience
│
└── tests/
    ├── ContosoInsurance.Api.Tests/       # API endpoint tests
    ├── ContosoInsurance.AppHost.Tests/   # Aspire integration tests
    ├── ContosoInsurance.Data.Tests/      # Data layer tests
    ├── ContosoInsurance.Web.Tests/       # Frontend tests
    └── ContosoInsurance.Worker.Tests/    # Worker tests
```

---

## 🛠️ Tech Stack

| Category | Technology | Purpose |
|---|---|---|
| **Runtime** | .NET 10 | Application framework |
| **Orchestration** | .NET Aspire 9.2 | Service orchestration, discovery, and developer tooling |
| **Frontend** | Blazor Server | Interactive server-rendered UI |
| **API** | ASP.NET Core Minimal APIs | Lightweight REST endpoints |
| **ORM** | Entity Framework Core 10 | Database access and migrations |
| **Database** | SQL Server / Azure SQL MI | Relational data storage |
| **Messaging** | RabbitMQ | Asynchronous event-driven processing |
| **Observability** | OpenTelemetry | Distributed tracing, metrics, and logging |
| **Container Orchestration** | AKS / Kubernetes | Production workload hosting |
| **Infrastructure** | Bicep + Azure Developer CLI | Infrastructure as Code |
| **CI/CD** | GitHub Actions | Build, test, and deployment pipelines |

---

## 🚀 Getting Started

### Prerequisites

| Tool | Version | Install |
|---|---|---|
| .NET SDK | 10.0+ | [Download](https://dotnet.microsoft.com/download/dotnet/10.0) |
| Docker Desktop | Latest | [Download](https://www.docker.com/products/docker-desktop) |
| Azure Developer CLI | Latest | `winget install Microsoft.Azd` |
| Azure CLI | Latest | `winget install Microsoft.AzureCLI` |
| IDE | VS 2022+ or VS Code | [Visual Studio](https://visualstudio.microsoft.com/) / [VS Code + C# Dev Kit](https://code.visualstudio.com/) |

### Run Locally with Aspire

1. **Clone the repository**

   ```bash
   git clone https://github.com/EmeaAppGbb/ContosoInsurances-NativeToLocal.git
   cd ContosoInsurances-NativeToLocal
   ```

2. **Ensure Docker Desktop is running** — Aspire uses containers for SQL Server and RabbitMQ.

3. **Start the Aspire AppHost**

   ```bash
   dotnet run --project src/ContosoInsurance.AppHost
   ```

4. **Open the Aspire Dashboard** — The console output will display the dashboard URL (typically `https://localhost:17225`). The dashboard provides:
   - Live service status and health
   - Distributed traces across all services
   - Structured logs from every component
   - Real-time metrics

5. **Access the application**
   - **Web Frontend**: URL shown in the Aspire dashboard (external endpoint)
   - **API Swagger**: Available in development mode at the API's `/swagger` endpoint
   - **RabbitMQ Management**: `http://localhost:15672` (guest/guest)

> **What happens under the hood:** Aspire starts a SQL Server container, a RabbitMQ container (with the management plugin), the API service, the background worker, and the Blazor frontend — all wired together with automatic service discovery and health-check-based startup ordering.

### Deploy to Azure

> ⚠️ **Coming soon** — Azure deployment with `azd` is being prepared on the `main` branch.

```bash
# Authenticate
azd auth login
az login

# Initialize and deploy
azd init
azd up
```

**Resources created:**
- Azure Kubernetes Service (AKS) cluster in a private VNet
- Azure SQL Managed Instance (private endpoint)
- Azure Container Registry (private)
- Application Gateway with public IP (ingress)
- Azure Key Vault for secrets management
- Log Analytics workspace + Application Insights

---

## 📊 Application Features

Contoso Insurance is a full-featured insurance management application:

### 👤 Customer Management
- Register new customers with contact details
- View customer profiles and their associated policies

### 📋 Policy Management
- Create insurance policies (Auto, Home, Life, Health, Travel, Business)
- Track policy lifecycle: Draft → Active → Expired / Cancelled / Suspended
- Auto-generated policy numbers (`POL-20250101-a1b2c3d4`)

### 📝 Claims Processing
- File claims against active policies
- **Event-driven workflow**: Submitting a claim publishes a `ClaimSubmittedEvent` to RabbitMQ
- Background worker automatically transitions claims from `Submitted` → `Under Review`
- Full claim lifecycle: Submitted → Under Review → Approved / Denied → Paid → Closed
- Auto-generated claim numbers (`CLM-20250101-a1b2c3d4`)

### 💰 Quote Generation
- Request insurance quotes for any policy type
- Quotes auto-expire after 30 days
- Auto-generated quote numbers (`QTE-20250101-a1b2c3d4`)

---

## 🗺️ Migration Roadmap

This repository demonstrates a **progressive migration path** from Azure cloud to Azure Local, organized across three branches.

### Branch: `main` — Azure Cloud ☁️

The starting point: a fully cloud-native deployment on Azure.

- **Compute**: Azure Kubernetes Service (AKS) in a private VNet
- **Database**: Azure SQL Managed Instance with private endpoint
- **Messaging**: RabbitMQ running as a container in AKS
- **Registry**: Azure Container Registry (private, VNet-integrated)
- **Monitoring**: Azure Monitor, Log Analytics, Application Insights
- **Identity**: Microsoft Entra ID (Azure AD)
- **Networking**: Application Gateway as the sole public entry point; all other services are private
- **Infrastructure as Code**: Bicep templates deployed via Azure Developer CLI (`azd`)

### Branch: `local-connected` — Azure Local (Connected) 🔗

The deployment target shifts to Azure Local hardware while **maintaining connectivity to Azure** for management and monitoring.

**What changes:**
- **Compute** → Arc-enabled Kubernetes on Azure Local (managed via Azure Arc)
- **Database** → Azure SQL Managed Instance on Azure Local (Arc-enabled)
- **Registry** → Local container registry, synced from Azure Container Registry
- **Networking** → On-premises network with Azure Arc connectivity

**What stays the same:**
- Azure Monitor and Application Insights (telemetry sent to Azure)
- Microsoft Entra ID for authentication
- Azure Arc for cluster and resource management
- GitHub Actions for CI/CD (artifacts pushed via Arc)

**Why connected mode?** Ideal for branch offices, retail locations, or edge sites that have intermittent or low-bandwidth connectivity but still benefit from centralized Azure management.

### Branch: `local-disconnected` — Azure Local (Disconnected) 🔒

A **fully air-gapped deployment** with zero internet dependency.

**What changes:**
- **Compute** → Standalone Kubernetes cluster (no Arc connection)
- **Database** → Local SQL Server instance
- **Messaging** → Local RabbitMQ instance
- **Registry** → Fully local container registry (images imported offline)
- **Monitoring** → Local monitoring stack (Prometheus + Grafana or equivalent)
- **Identity** → Local identity provider (Active Directory / local accounts)
- **CI/CD** → Offline deployment via portable media or local pipeline
- **Networking** → Isolated network, no external connectivity

**Why disconnected mode?** Required for highly regulated industries (government, defense, healthcare), classified environments, or locations with no network connectivity.

### Migration Comparison Table

| Aspect | ☁️ `main` (Azure) | 🔗 `local-connected` | 🔒 `local-disconnected` |
|---|---|---|---|
| **Compute** | AKS (managed) | Arc-enabled K8s on Azure Local | Standalone K8s |
| **Database** | Azure SQL MI (PaaS) | SQL MI on Azure Local (Arc) | Local SQL Server |
| **Messaging** | RabbitMQ in AKS | RabbitMQ on Azure Local | RabbitMQ (local) |
| **Container Registry** | Azure Container Registry | ACR + local sync | Local registry only |
| **Monitoring** | App Insights + Log Analytics | App Insights (via Arc) | Prometheus + Grafana |
| **Identity** | Microsoft Entra ID | Microsoft Entra ID (via Arc) | Local AD / accounts |
| **Secrets** | Azure Key Vault | Azure Key Vault (via Arc) | Local secret store |
| **CI/CD** | GitHub Actions → AKS | GitHub Actions → Arc | Offline / manual deploy |
| **Internet** | ✅ Required | ⚡ Partial (management) | ❌ Not required |
| **Azure Arc** | N/A | ✅ Enabled | ❌ Not used |

---

## 🧪 Testing

Run all tests from the repository root:

```bash
# Run all tests
dotnet test ContosoInsurance.slnx

# Run tests for a specific project
dotnet test tests/ContosoInsurance.Api.Tests
dotnet test tests/ContosoInsurance.Data.Tests
dotnet test tests/ContosoInsurance.Worker.Tests
```

Build the solution:

```bash
dotnet build ContosoInsurance.slnx
```

---

## 📁 Project Details

### ContosoInsurance.AppHost

The Aspire orchestrator that defines and wires all services:

```csharp
// Infrastructure
SQL Server (sql) → Database (insurancedb)    [persistent container]
RabbitMQ (messaging)                          [with management plugin]

// Services
API     → references: insurancedb, messaging  [waits for both]
Worker  → references: insurancedb, messaging  [waits for both]
Web     → references: api                     [external endpoint, waits for API]
```

### ContosoInsurance.Api

Minimal API service exposing RESTful endpoints:

| Resource | Endpoints | Description |
|---|---|---|
| `/api/customers` | GET, GET/:id, POST | Customer registration and lookup |
| `/api/policies` | GET, GET/:id, POST | Policy creation and management |
| `/api/claims` | GET, GET/:id, POST | Claim filing (publishes events to RabbitMQ) |
| `/api/quotes` | GET, GET/:id, POST | Quote generation with 30-day expiry |

### ContosoInsurance.Data

Entity Framework Core data layer with four domain models:

- **Customer** — Name, email (unique), phone, address
- **Policy** — Type (Auto/Home/Life/Health/Travel/Business), status, premium, coverage, dates
- **Claim** — Status lifecycle, description, amount, incident date, linked to policy
- **Quote** — Estimated premium, coverage, 30-day expiration, linked to customer

### ContosoInsurance.Worker

Background service that consumes RabbitMQ messages:

- Listens on `claim-events` queue
- Receives `ClaimSubmittedEvent` messages (published by the API when a claim is filed)
- Transitions claims from **Submitted** → **Under Review**
- Manual acknowledgment with error handling and requeue on failure

### ContosoInsurance.ServiceDefaults

Shared configuration applied to all services:

- **OpenTelemetry**: Distributed tracing, metrics, structured logging
- **Health Checks**: `/health` (readiness) and `/alive` (liveness)
- **Service Discovery**: Automatic DNS-based resolution between services
- **Resilience**: Standard HTTP retry and circuit-breaker policies

---

## 🤝 Contributing

Contributions are welcome! Please follow these guidelines:

1. **Fork** the repository
2. **Create a feature branch** from the appropriate base branch (`main`, `local-connected`, or `local-disconnected`)
3. **Make your changes** and ensure all tests pass (`dotnet test ContosoInsurance.slnx`)
4. **Submit a pull request** with a clear description of the changes

### Branch Workflow

- `main` — Azure cloud deployment (primary development branch)
- `local-connected` — Azure Local connected mode (branched from `main`)
- `local-disconnected` — Azure Local disconnected mode (branched from `local-connected`)

---

## 📄 License

This project is licensed under the [MIT License](LICENSE).
