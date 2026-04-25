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

## 🏗️ Architecture — Azure Local Connected Mode

In connected mode, the Contoso Insurance application runs **entirely on Azure Local hardware** while Azure Arc provides a **control plane bridge** back to Azure for management, monitoring, and identity. The application code is **unchanged** from the `main` branch — only the infrastructure layer changes.

**New in April 2026:** Application Gateway for Containers (AGC) now works with Arc-enabled K8s clusters, providing a **unified ingress layer** across cloud and edge. This is a game-changer — the ingress configuration is now **identical** between `main` (cloud AKS) and `local-connected` (on-prem Arc K8s).

### Topology Diagram

```
 ┌─────────────────────────────────────────────────────────────────────────────────────┐
 │                              AZURE CLOUD                                           │
 │                                                                                     │
 │   ┌──────────────────┐  ┌──────────────────┐  ┌──────────────────────────────────┐  │
 │   │  📦 Azure        │  │  🔑 Azure        │  │  📊 Azure Monitor               │  │
 │   │  Container       │  │  Key Vault       │  │  ┌─────────────────────────────┐ │  │
 │   │  Registry (ACR)  │  │                  │  │  │ Log Analytics Workspace     │ │  │
 │   │                  │  │  Secrets &       │  │  │ Application Insights        │ │  │
 │   │  Image source    │  │  Certificates    │  │  │ Metrics & Alerts            │ │  │
 │   │  of truth        │  │  (via Arc CSI)   │  │  └─────────────────────────────┘ │  │
 │   └──────────────────┘  └──────────────────┘  └──────────────────────────────────┘  │
 │                                                                                     │
 │   ┌──────────────────────────────────────────────────────────────────────────────┐  │
 │   │                     🌐 Azure Arc Control Plane                              │  │
 │   │                                                                              │  │
 │   │   Azure Resource Manager  ←→  Arc Resource Bridge  ←→  Custom Locations     │  │
 │   │                                                                              │  │
 │   │   • Cluster visibility in Azure Portal       • Azure Policy enforcement     │  │
 │   │   • GitOps (Flux) configuration management   • RBAC via Entra ID            │  │
 │   └──────────────────────────────────────────────────────────────────────────────┘  │
 │                                                                                     │
 │   ┌──────────────────────────────────────────────────────────────────────────────┐  │
 │   │            🚀 Application Gateway for Containers (AGC)                      │  │
 │   │            Microsoft.ServiceNetworking/trafficControllers                   │  │
 │   │                                                                              │  │
 │   │   Internet ──► AGC Frontend (public IP, TLS termination, WAF)               │  │
 │   │                    │                                                         │  │
 │   │                    │  Gateway API: Gateway + HTTPRoute resources             │  │
 │   │                    │  Routes traffic to on-prem pods via Arc tunnel          │  │
 │   └────────────────────┼─────────────────────────────────────────────────────────┘  │
 │                        │                                                             │
 └────────────────────────┼─────────────────────────────────────────────────────────────┘
                          │
                   Arc Tunnel / Secure Channel
                   (outbound HTTPS 443 only)
                          │
 ┌────────────────────────┴─────────────────────────────────────────────────────────────┐
 │                         ON-PREMISES — AZURE LOCAL CLUSTER                            │
 │                                                                                      │
 │   ┌───────────────────────────────────────────────────────────────────────────────┐  │
 │   │          Arc-enabled Kubernetes 1.35 (AKS on Azure Local)                    │  │
 │   │          Custom Location: contoso-local-cl                                   │  │
 │   │                                                                               │  │
 │   │          ┌─────────────────────────────────────────┐                         │  │
 │   │          │  ALB Controller (Arc Extension)         │                         │  │
 │   │          │  Manages Gateway + HTTPRoute resources  │                         │  │
 │   │          │  Syncs with AGC in Azure cloud          │                         │  │
 │   │          └────────────────┬────────────────────────┘                         │  │
 │   │                           │ Routes to backend pods                            │  │
 │   │                           │                                                   │  │
 │   │          ┌────────────────┼──────────────────────────────┐                   │  │
 │   │          │   contoso-insurance namespace                 │                   │  │
 │   │          │                │                               │                   │  │
 │   │          │  ┌─────────────▼────────────────────────┐    │                   │  │
 │   │          │  │  🌐 Web Frontend (Blazor Server)      │    │                   │  │
 │   │          │  │  ClusterIP — port 8080                │    │                   │  │
 │   │          │  └─────────────┬────────────────────────┘    │                   │  │
 │   │          │                │ HTTP                         │                   │  │
 │   │          │  ┌─────────────▼────────────────────────┐    │                   │  │
 │   │          │  │  🔌 API Service (Minimal APIs)        │    │                   │  │
 │   │          │  │  ClusterIP — port 8080                │    │                   │  │
 │   │          │  └───────┬─────────────────┬─────────────┘    │                   │  │
 │   │          │          │ AMQP            │ TDS               │                   │  │
 │   │          │  ┌───────▼──────────┐  ┌───▼────────────────┐ │                   │  │
 │   │          │  │ 🐇 RabbitMQ      │  │                    │ │                   │  │
 │   │          │  │ ClusterIP        │  │                    │ │                   │  │
 │   │          │  │ 5672 / 15672     │  │                    │ │                   │  │
 │   │          │  └───────┬──────────┘  │                    │ │                   │  │
 │   │          │          │ AMQP        │                    │ │                   │  │
 │   │          │  ┌───────▼──────────┐  │                    │ │                   │  │
 │   │          │  │ ⚙️ Worker         │  │                    │ │                   │  │
 │   │          │  │ (Claim Processor)├──►                    │ │                   │  │
 │   │          │  │ ClusterIP        │  │                    │ │                   │  │
 │   │          │  └──────────────────┘  │                    │ │                   │  │
 │   │          │                        │                    │ │                   │  │
 │   │          └────────────────────────┼────────────────────┘ │                   │  │
 │   │                                   │                      │                   │  │
 │   └───────────────────────────────────┼──────────────────────┘                   │  │
 │                                       │                                           │  │
 │   ┌───────────────────────────────────▼───────────────────────────────────────┐   │  │
 │   │  🗄️  Arc-enabled SQL Managed Instance                                     │   │  │
 │   │  Data Controller: contoso-dc  |  Custom Location: contoso-local-cl        │   │  │
 │   │  Private — accessible only from the K8s cluster network                   │   │  │
 │   └───────────────────────────────────────────────────────────────────────────┘   │  │
 │                                                                                   │  │
 │   Azure Local Cluster: 2–4 node HCI cluster  │  Logical Network: 10.0.0.0/16     │  │
 └───────────────────────────────────────────────────────────────────────────────────────┘
```

> **🔑 Traffic flow:** Internet → AGC (Azure cloud, managed data plane) → Arc tunnel (outbound 443) → ALB Controller → K8s ClusterIP pods on-prem. No inbound firewall rules required on the on-prem network.

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

- **Only the Web Frontend is internet-facing**, exposed through Application Gateway for Containers (AGC) using Gateway API (`Gateway` + `HTTPRoute` resources).
- **AGC provides built-in WAF**, TLS termination, and automatic scaling — no separate WAF SKU required.
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
| **Container Orchestration** | AKS / Kubernetes 1.35 | Production workload hosting |
| **Ingress** | Application Gateway for Containers (AGC) | Gateway API-based ingress with built-in WAF |
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
| kubectl | 1.35+ | `az aks install-cli` |
| IDE | VS 2022+ or VS Code | [Visual Studio](https://visualstudio.microsoft.com/) / [VS Code + C# Dev Kit](https://code.visualstudio.com/) |

> **For Azure deployment:** Gateway API CRDs are installed automatically by the ALB Controller add-on when AGC is provisioned. No manual CRD installation is required for AKS.

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
- Azure Kubernetes Service (AKS) cluster (Kubernetes 1.35) in a private VNet
- **Application Gateway for Containers (AGC)** — managed ingress with Gateway API
- ALB Controller add-on (manages AGC lifecycle from within AKS)
- Azure SQL Managed Instance (private endpoint)
- Azure Container Registry (private)
- Azure Key Vault for secrets management
- Log Analytics workspace + Application Insights

> **Note:** AGC replaces the legacy Application Gateway WAF v2 + AGIC pattern. The ALB Controller add-on automatically provisions the `Microsoft.ServiceNetworking/trafficControllers` resource and installs Gateway API CRDs. Traffic is routed using `Gateway` and `HTTPRoute` resources instead of `Ingress`.

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
- **Networking**: Application Gateway for Containers (AGC) as the sole public entry point using Gateway API; all other services are private
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
| **Compute** | AKS (K8s 1.35, managed) | Arc-enabled K8s 1.35 on Azure Local | Standalone K8s 1.35 |
| **Ingress** | AGC (Gateway API) | AGC via Arc (Gateway API) | NGINX / HAProxy (local) |
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

## 🔄 Why AGC?

**Application Gateway for Containers (AGC)** is the successor to the Application Gateway Ingress Controller (AGIC) and the recommended ingress solution for AKS as of April 2026.

### Deprecation Timeline

| Component | Status | Date | Replacement |
|---|---|---|---|
| **AGIC** (Application Gateway Ingress Controller) | ⛔ Retired | March 2026 | Application Gateway for Containers (AGC) |
| **NGINX Ingress Controller** (community) | ⛔ Retired | March 2026 | AGC or vendor-supported alternatives |
| **Kubernetes Ingress API** | ⚠️ Maintenance mode | Ongoing | Gateway API (`Gateway` + `HTTPRoute`) |
| **Kubernetes 1.30** | ⛔ End of life | March 2026 | Kubernetes 1.35 (current GA) |

### AGC Advantages Over Legacy AGIC

| Feature | AGIC (retired) | AGC |
|---|---|---|
| **API** | Kubernetes Ingress | Gateway API (Gateway + HTTPRoute) |
| **Data plane** | Self-managed App Gateway | Fully managed by Azure |
| **WAF** | Separate WAF v2 SKU required | Built-in WAF integration |
| **Scaling** | Manual / autoscale rules | Automatic, near-instant scaling |
| **Multi-site** | Limited | Native multi-site with Gateway listeners |
| **Arc support** | ❌ None | ✅ Works with Arc-enabled K8s (connected mode) |
| **Resource type** | `Microsoft.Network/applicationGateways` | `Microsoft.ServiceNetworking/trafficControllers` |

### Key Insight: Unified Ingress Across Cloud and Edge

With AGC, **the ingress layer is now identical** between cloud AKS (`main` branch) and on-prem Arc-enabled K8s (`local-connected` branch). AGC works as an Azure resource in both cases — the ALB Controller runs as an AKS add-on in the cloud and as an Arc extension on-prem. This dramatically simplifies hybrid architectures.

> **AGC has been GA since November 2025.** It is the Azure-recommended path for all new AKS deployments and the required migration target for existing AGIC users.

---

## ⚠️ Deprecated Components

The following components have been removed from this architecture as of the April 2026 refresh:

| Component | Removed | Reason | Replacement |
|---|---|---|---|
| Application Gateway WAF v2 + AGIC | ✅ | AGIC retired March 2026 | AGC with built-in WAF |
| NGINX Ingress Controller (community) | ✅ | Retired March 2026 | AGC (Gateway API) |
| MetalLB | ✅ | Not needed with AGC | AGC manages load balancing |
| Kubernetes `Ingress` resources | ✅ | Superseded by Gateway API | `Gateway` + `HTTPRoute` resources |
| Kubernetes 1.30 | ✅ | End of life March 2026 | Kubernetes 1.35 |

> **If you are following older tutorials** that reference AGIC, NGINX Ingress, or `Ingress` resources, those patterns are no longer supported. This repository uses the current Azure-recommended stack.

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
