# Contoso Insurance — Azure Local (Connected Mode)

> 📍 **Branch: `local-connected`** — This branch deploys the Contoso Insurance application to Azure Local in connected mode. See [`main`](../../tree/main) for the Azure cloud version.

> A comprehensive guide and lab for migrating cloud-native applications from Azure to Azure Local, demonstrating connected deployment with Azure Arc management, on-premises compute, and hybrid cloud services.

---

## 🎯 Purpose

This repository is a **learning lab and reference architecture** for organizations planning to:

1. **Build enterprise cloud-native applications** using .NET Aspire, demonstrating modern microservices patterns with service discovery, distributed tracing, and event-driven messaging.
2. **Deploy to Azure** using AKS, Azure SQL Managed Instance, and managed infrastructure services (see [`main` branch](../../tree/main)).
3. **Migrate to Azure Local in connected mode** ← **You are here** — shifting compute to on-premises while maintaining Azure Arc management and cloud-based monitoring.
4. **Run fully disconnected on Azure Local** — operating in air-gapped environments with no internet dependency (see `local-disconnected` branch).

Each deployment model lives on its own branch, making it easy to compare what changes at each stage and understand the trade-offs.

| Branch | Target Environment | Internet Required | Status |
|---|---|---|---|
| [`main`](../../tree/main) | Azure (AKS + Azure SQL MI) | ✅ Yes | Baseline |
| **`local-connected`** | **Azure Local — Connected** | **⚡ Partial** | **← Current** |
| `local-disconnected` | Azure Local — Disconnected | ❌ No | Planned |

---

## 🏗️ Architecture — Azure Local Connected Mode

In connected mode, the Contoso Insurance application runs **entirely on Azure Local hardware** while Azure Arc provides a **control plane bridge** back to Azure for management, monitoring, and identity. The application code is **unchanged** from the `main` branch — only the infrastructure layer changes.

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

### What's On-Premises vs. What's in Azure Cloud

Understanding the **split** is key to connected mode. The application workloads run locally, but management and observability stay in the cloud.

| Layer | Location | Resource | Why Here? |
|---|---|---|---|
| **Compute** | 🏢 On-Prem | Arc-enabled Kubernetes | Low-latency, data sovereignty, local access |
| **Database** | 🏢 On-Prem | Arc-enabled SQL MI | Data residency, performance, compliance |
| **Messaging** | 🏢 On-Prem | RabbitMQ (container) | Co-located with producers/consumers |
| **Ingress** | ☁️ Azure | Application Gateway for Containers (AGC) | Managed data plane in Azure; ALB Controller as Arc extension on-prem |
| **Container Images** | ☁️ Azure | Azure Container Registry | Central image store; K8s pulls images via Arc or direct |
| **Secrets** | ☁️ Azure | Azure Key Vault | Centralized secret management via Arc CSI driver |
| **Monitoring** | ☁️ Azure | Azure Monitor / App Insights | Unified observability across all sites |
| **Identity** | ☁️ Azure | Microsoft Entra ID | Single identity plane for RBAC and authentication |
| **Management** | ☁️ Azure | Azure Arc Control Plane | Azure Portal visibility, policy, GitOps configuration |

### Service Communication

All inter-service communication uses **.NET Aspire service discovery** — identical to the cloud version:

| From | To | Protocol | Discovery Address |
|---|---|---|---|
| Web Frontend | API Service | HTTP | `https+http://api` |
| API Service | SQL Database | TCP | `insurancedb` connection string |
| API Service | RabbitMQ | AMQP | `messaging` connection string |
| Worker | SQL Database | TCP | `insurancedb` connection string |
| Worker | RabbitMQ | AMQP | `messaging` connection string |

> **Key insight:** The application code doesn't change between `main` and `local-connected`. Aspire service discovery and Kubernetes DNS resolution work identically. Only the infrastructure provisioning and network topology differ.

### Security Architecture — Connected Mode

- **Application Gateway for Containers (AGC)** is the sole public entry point — the same as cloud AKS. AGC data plane runs in Azure; the ALB Controller runs as an Arc extension on-prem.
- **Gateway API** resources (`Gateway` + `HTTPRoute`) define routing rules — identical to the `main` branch.
- **AGC provides built-in WAF**, TLS termination, and automatic scaling — no on-prem load balancer needed.
- **All backend services use ClusterIP** — no services are directly internet-accessible.
- **Arc-enabled SQL MI** is accessible only from within the Kubernetes cluster network.
- **Network Policies** (Calico) enforce zero-trust pod-to-pod communication within the namespace.
- **Azure Key Vault** secrets are injected via the **Arc Key Vault CSI driver** — secrets never stored in K8s manifests.
- **Microsoft Entra ID** remains the identity provider via Azure Arc RBAC integration.

> **What changed from `main`?** Nothing for ingress! AGC works identically for cloud AKS and Arc-enabled K8s. The on-prem cluster runs the ALB Controller as an Arc extension, which syncs with the AGC resource in Azure. This is the key benefit of AGC in connected mode.

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
├── infra/                                # 🔧  Bicep templates (Arc resources, Custom Locations)
│   ├── main.bicep                        #     Orchestrator — deploys all Arc-enabled resources
│   └── modules/                          #     Individual resource modules
│
├── k8s/                                  # ☸️   Kubernetes manifests for Azure Local deployment
│   ├── namespace.yaml                    #     contoso-insurance namespace
│   ├── configmap.yaml                    #     Application configuration
│   ├── secrets.yaml                      #     Secret references (Key Vault CSI)
│   ├── api-deployment.yaml              #     API service + ClusterIP
│   ├── web-deployment.yaml              #     Web frontend + ClusterIP + Ingress
│   ├── worker-deployment.yaml           #     Background worker
│   ├── rabbitmq-deployment.yaml         #     RabbitMQ broker + ClusterIP
│   └── network-policies.yaml            #     Calico zero-trust policies
│
└── tests/
    ├── ContosoInsurance.Api.Tests/       # API endpoint tests
    ├── ContosoInsurance.AppHost.Tests/   # Aspire integration tests
    ├── ContosoInsurance.Data.Tests/      # Data layer tests
    ├── ContosoInsurance.Web.Tests/       # Frontend tests
    └── ContosoInsurance.Worker.Tests/    # Worker tests
```

> **What changed from `main`?** The `src/` and `tests/` directories are **identical** to the `main` branch. Only `infra/` and `k8s/` were rewritten to target Azure Local with Arc-enabled resources instead of Azure-native PaaS services.

---

## 🛠️ Tech Stack

| Category | Technology | Purpose |
|---|---|---|
| **Runtime** | .NET 10 | Application framework |
| **Orchestration** | .NET Aspire 9.2 | Service orchestration, discovery, and developer tooling |
| **Frontend** | Blazor Server | Interactive server-rendered UI |
| **API** | ASP.NET Core Minimal APIs | Lightweight REST endpoints |
| **ORM** | Entity Framework Core 10 | Database access and migrations |
| **Database** | Arc-enabled SQL MI on Azure Local | Relational data storage (on-prem) |
| **Messaging** | RabbitMQ | Asynchronous event-driven processing |
| **Observability** | OpenTelemetry → Azure Monitor | Distributed tracing, metrics, and logging |
| **Container Orchestration** | Arc-enabled K8s 1.35 on Azure Local | Production workload hosting (on-prem) |
| **Ingress** | Application Gateway for Containers (AGC) | Gateway API-based ingress with built-in WAF (via Arc extension) |
| **Infrastructure** | Bicep (Arc resources) | Infrastructure as Code |
| **Management** | Azure Arc | Unified cloud management for on-prem resources |
| **CI/CD** | GitHub Actions | Build, test, and deployment pipelines |

---

## 🚀 Getting Started

### Prerequisites

#### For Local Development (Aspire — unchanged from `main`)

| Tool | Version | Install |
|---|---|---|
| .NET SDK | 10.0+ | [Download](https://dotnet.microsoft.com/download/dotnet/10.0) |
| Docker Desktop | Latest | [Download](https://www.docker.com/products/docker-desktop) |
| IDE | VS 2022+ or VS Code | [Visual Studio](https://visualstudio.microsoft.com/) / [VS Code + C# Dev Kit](https://code.visualstudio.com/) |

#### For Azure Local Deployment

| Requirement | Description |
|---|---|
| **Azure Local Cluster** | 2–4 node Azure Local (HCI) cluster, registered with Azure |
| **Azure Arc** | Arc Resource Bridge deployed on the Azure Local cluster |
| **Arc-enabled Kubernetes** | AKS on Azure Local provisioned via Arc (with a Custom Location) |
| **Azure CLI** | Latest, with `connectedk8s`, `k8s-extension`, and `customlocation` extensions |
| **kubectl** | 1.35+, configured with credentials to the Arc-enabled K8s cluster |
| **Azure Subscription** | For Arc control plane, AGC, ACR, Key Vault, and Monitor resources |

### Run Locally with Aspire

Local development is **identical** to the `main` branch — Aspire handles everything:

1. **Clone the repository**

   ```bash
   git clone https://github.com/EmeaAppGbb/ContosoInsurances-NativeToLocal.git
   cd ContosoInsurances-NativeToLocal
   git checkout local-connected
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

### Deploy to Azure Local (Connected Mode)

Deployment to Azure Local follows a two-phase approach: **Bicep** provisions the Arc-enabled infrastructure, then **kubectl** deploys the application manifests.

#### Phase 1 — Provision Arc Resources with Bicep

```bash
# Authenticate
az login
az account set --subscription <your-subscription-id>

# Deploy Arc-enabled infrastructure (SQL MI, data controller, extensions)
az deployment group create \
  --resource-group rg-contoso-local \
  --template-file infra/main.bicep \
  --parameters infra/main.parameters.json
```

This provisions:
- Arc Data Controller and SQL Managed Instance (on the Azure Local cluster)
- Azure Container Registry (in Azure cloud)
- Azure Key Vault (in Azure cloud)
- Log Analytics workspace and Application Insights (in Azure cloud)
- Arc K8s extensions: monitoring agent, Key Vault CSI driver, Azure Policy

#### Phase 2 — Deploy Application to Arc-enabled Kubernetes

```bash
# Get credentials for the Arc-enabled K8s cluster
az connectedk8s proxy --name contoso-arc-k8s --resource-group rg-contoso-local

# Apply Kubernetes manifests
kubectl apply -f k8s/namespace.yaml
kubectl apply -f k8s/configmap.yaml
kubectl apply -f k8s/secrets.yaml
kubectl apply -f k8s/rabbitmq-deployment.yaml
kubectl apply -f k8s/api-deployment.yaml
kubectl apply -f k8s/worker-deployment.yaml
kubectl apply -f k8s/web-deployment.yaml
kubectl apply -f k8s/network-policies.yaml

# Verify all pods are running
kubectl get pods -n contoso-insurance
```

> **💡 Tip:** For production, use **GitOps with Flux** — configure an Arc GitOps source pointing to this repository's `k8s/` folder. Arc will automatically reconcile the cluster state with the manifests in Git.

---

## 📊 Application Features

Contoso Insurance is a full-featured insurance management application. These features are **identical across all branches** — only the deployment target changes.

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

The starting point: a fully cloud-native deployment on Azure. See the [`main` branch README](../../tree/main) for full details.

- **Compute**: Azure Kubernetes Service (AKS) in a private VNet
- **Database**: Azure SQL Managed Instance with private endpoint
- **Messaging**: RabbitMQ running as a container in AKS
- **Registry**: Azure Container Registry (private, VNet-integrated)
- **Monitoring**: Azure Monitor, Log Analytics, Application Insights
- **Identity**: Microsoft Entra ID (Azure AD)
- **Networking**: Application Gateway for Containers (AGC) as the sole public entry point using Gateway API; all other services are private
- **Infrastructure as Code**: Bicep templates deployed via Azure Developer CLI (`azd`)

### Branch: `local-connected` — Azure Local (Connected) 🔗 ← You Are Here

The deployment target shifts to Azure Local hardware while **maintaining connectivity to Azure** for management and monitoring. The application code is **unchanged** — only infrastructure and deployment manifests differ.

#### What Changed from `main` and Why

| Change | Reason |
|---|---|
| AKS → Arc-enabled K8s 1.35 on Azure Local | Compute moves on-prem for data sovereignty, latency, and compliance |
| Azure SQL MI (PaaS) → Arc-enabled SQL MI | Database stays close to compute; data never leaves the premises |
| AGC add-on → AGC Arc extension | **Same AGC!** ALB Controller runs as Arc extension instead of AKS add-on |
| VNet + NSGs → Kubernetes Network Policies (Calico) | On-prem networking uses the cluster's CNI, not Azure virtual networking |
| `azd up` → Bicep + `kubectl apply` | No `azd` integration for Azure Local yet; deploy in two phases |
| Azure-native monitoring → Arc monitoring extension | Same Azure Monitor backend, but telemetry flows through the Arc agent |

> **🎯 Notice what DIDN'T change:** The ingress layer (AGC + Gateway API) is now **identical** between cloud and on-prem. This is a huge simplification over the previous NGINX + MetalLB pattern.

#### Detailed Resource Comparison

| Azure Cloud Resource (`main`) | Azure Local Equivalent (`local-connected`) | Notes |
|---|---|---|
| Azure Kubernetes Service (AKS) | AKS on Azure Local (Arc-enabled, K8s 1.35) | Managed via Azure Arc; visible in Azure Portal |
| Azure SQL Managed Instance | Arc-enabled SQL MI + Data Controller | Runs on Azure Local; managed via Arc Data Controller |
| AGC (AKS ALB Controller add-on) | **AGC (ALB Controller Arc extension)** | **Same AGC resource!** ALB Controller deployed as Arc extension |
| Azure VNet + Subnets | Azure Local Logical Network | SDN-managed networking on the HCI cluster |
| Azure Private Endpoints | Kubernetes ClusterIP services | All services are cluster-internal by default |
| Azure Container Registry | Azure Container Registry (unchanged) | Images still pulled from ACR; cluster has outbound access |
| Azure Key Vault | Azure Key Vault (unchanged) | Accessed via Arc Key Vault CSI driver on the cluster |
| Azure Monitor + App Insights | Azure Monitor + App Insights (unchanged) | Telemetry forwarded via Arc monitoring extension |
| Microsoft Entra ID | Microsoft Entra ID (unchanged) | Arc RBAC integration; same identity plane |
| Azure Policy (for AKS) | Azure Policy (via Arc) | Same policies, enforced on the Arc-connected cluster |

#### What Stays in Azure Cloud and Why

Some services **remain in Azure** because they provide centralized management value that doesn't benefit from being on-premises:

- **Application Gateway for Containers (AGC)** — Managed ingress. The AGC data plane runs in Azure cloud; only the ALB Controller runs on-prem as an Arc extension. This means no on-prem load balancer to manage — a major operational win over the old NGINX + MetalLB pattern.
- **Azure Container Registry** — Central image repository. The Arc-enabled cluster pulls images from ACR over the network. Running a local registry adds operational burden without significant benefit in connected mode.
- **Azure Key Vault** — Centralized secret management. The Arc Key Vault CSI driver mounts secrets directly into pods. Secrets are never stored in Kubernetes manifests.
- **Azure Monitor / Application Insights** — Unified observability. Telemetry from all sites (cloud, connected, future disconnected) aggregates into a single pane of glass. The Arc monitoring extension handles forwarding.
- **Microsoft Entra ID** — Single identity plane. Users authenticate against Entra ID regardless of where the workload runs.

#### Network Topology — Now Unified!

```
CLOUD (main)                          ON-PREM (local-connected)
─────────────                         ─────────────────────────
Internet                              Internet
    │                                     │
    ▼                                     ▼
AGC (Azure cloud)                     AGC (Azure cloud) ← SAME!
    │  Gateway + HTTPRoute                │  Gateway + HTTPRoute
    │  (AKS ALB Controller add-on)        │  (ALB Controller Arc extension)
    ▼                                     │
AKS cluster (Private VNet)                ▼ (via Arc tunnel)
    │                                 Arc K8s cluster (Azure Local)
Web Pod → API Pod → SQL MI               │
                                      Web Pod → API Pod → SQL MI
```

**🎯 Key insight:** With AGC, the ingress pattern is **identical** for cloud and on-prem. Both use the same `Gateway` and `HTTPRoute` resources. The only difference is how the ALB Controller is deployed (AKS add-on vs Arc extension) and how traffic reaches the cluster (VNet link vs Arc tunnel).

**Key network characteristics:**
- No on-prem load balancer needed — AGC handles everything from Azure cloud
- No NGINX, no MetalLB — eliminated entirely
- Outbound internet required for: AGC tunnel, Arc agent heartbeat, image pulls, telemetry, and Entra auth
- All inbound traffic flows through AGC → Arc tunnel → ClusterIP services

#### Cost Implications

| Cost Category | Azure Cloud (`main`) | Azure Local (`local-connected`) |
|---|---|---|
| **Compute** | AKS node pool VMs (~$300–$800/mo) | Azure Local hardware (CAPEX, already owned) |
| **Database** | SQL MI (~$350+/mo) | Arc SQL MI license (included with Azure Local) |
| **Ingress** | AGC (~$150/mo) | AGC (~$150/mo, same — managed in Azure) |
| **Monitoring** | Pay per GB ingested | Pay per GB ingested (same) |
| **Arc Management** | N/A | Free (Arc control plane is no-cost) |
| **ACR** | ~$5–50/mo | ~$5–50/mo (same, still in Azure) |
| **Key Vault** | ~$3/mo | ~$3/mo (same, still in Azure) |
| **Total estimate** | ~$860+/mo (OPEX) | ~$210/mo cloud + hardware CAPEX |

> **Summary:** Connected mode trades cloud OPEX for on-prem CAPEX. AGC cost is the same in both modes (it's an Azure cloud resource either way). If you already own Azure Local hardware, the ongoing Azure costs drop significantly — you only pay for cloud services that remain (AGC, ACR, KV, Monitor, Arc SQL MI license).

#### Operational Differences

| Operation | Azure Cloud | Azure Local (Connected) |
|---|---|---|
| **Cluster upgrades** | AKS auto-upgrade or managed | Arc-initiated K8s upgrades via Azure Portal |
| **Monitoring** | Native App Insights integration | Arc monitoring extension → App Insights |
| **Log collection** | Azure Monitor agent (built-in) | Container Insights via Arc extension |
| **Policy enforcement** | Azure Policy for AKS | Azure Policy for Arc-enabled K8s |
| **Secret rotation** | Key Vault auto-rotation | Key Vault + Arc CSI driver sync |
| **Scaling** | AKS node autoscaler | Manual or VM-based (Azure Local capacity) |
| **Disaster recovery** | Azure-managed redundancy | Azure Local stretch cluster or backup/restore |
| **Certificate management** | AGC + Key Vault | AGC + Key Vault (same!) |

---

### Branch: `local-disconnected` — Azure Local (Disconnected) 🔒

A **fully air-gapped deployment** with zero internet dependency. See the `local-disconnected` branch (planned).

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
| **Database** | Azure SQL MI (PaaS) | SQL MI on Azure Local (Arc) | Local SQL Server |
| **Messaging** | RabbitMQ in AKS | RabbitMQ on Azure Local | RabbitMQ (local) |
| **Container Registry** | Azure Container Registry | ACR (unchanged — cloud) | Local registry only |
| **Ingress** | AGC (Gateway API) | **AGC (Gateway API) — SAME!** | NGINX / HAProxy (local) |
| **Monitoring** | App Insights + Log Analytics | App Insights via Arc | Prometheus + Grafana |
| **Identity** | Microsoft Entra ID | Microsoft Entra ID via Arc | Local AD / accounts |
| **Secrets** | Azure Key Vault | Azure Key Vault via Arc CSI | Local secret store |
| **CI/CD** | GitHub Actions → AKS | GitHub Actions → Arc / GitOps | Offline / manual deploy |
| **Internet** | ✅ Required | ⚡ Partial (management + telemetry) | ❌ Not required |
| **Azure Arc** | N/A | ✅ Enabled | ❌ Not used |
| **App Code Changes** | — | None | None |

---

## 🔄 Migration Guide — Azure Cloud to Azure Local (Connected)

This section provides a **step-by-step walkthrough** for migrating the Contoso Insurance application from Azure cloud (`main` branch) to Azure Local in connected mode. Each step explains not just *what* to do, but *why* — this is a learning lab.

### Step 1: Set Up the Azure Local Cluster

**What:** Prepare the physical infrastructure — an Azure Local (formerly Azure Stack HCI) cluster registered with Azure.

**Why:** Azure Local provides the on-premises compute and storage fabric. The cluster must be registered with Azure to enable Arc management.

**Key concepts:**
- **Azure Local** is a hyperconverged infrastructure (HCI) solution — compute, storage, and networking in a single cluster
- The cluster runs a specialized Azure Local OS and registers itself as an Azure resource
- Registration creates an Azure resource (`Microsoft.AzureStackHCI/clusters`) that represents your on-prem hardware

**Actions:**
1. Deploy 2–4 physical or nested Azure Local nodes
2. Run the Azure Local deployment wizard (Windows Admin Center or Azure Portal)
3. Register the cluster with your Azure subscription
4. Verify the cluster appears in the Azure Portal under **Azure Local**

> **💡 Learning note:** Azure Local is not "just Hyper-V" — it includes Azure-consistent storage (Storage Spaces Direct), software-defined networking, and the Arc Resource Bridge that enables Azure services to run on-premises.

---

### Step 2: Enable Azure Arc on the Cluster

**What:** Deploy the Arc Resource Bridge and establish the control plane connection.

**Why:** Azure Arc is the bridge between your on-premises hardware and the Azure management plane. Without Arc, Azure has no visibility into your local resources.

**Key concepts:**
- **Arc Resource Bridge** is a lightweight VM that runs on the Azure Local cluster and maintains a persistent outbound connection to Azure
- **Custom Locations** are an Azure resource type that represents a physical location (your data center, branch office). Arc-enabled services deploy *to* a Custom Location
- The Arc agent only requires **outbound HTTPS (443)** — no inbound firewall rules needed

**Actions:**
1. Deploy Arc Resource Bridge on the Azure Local cluster (automated during HCI setup)
2. Create a Custom Location (e.g., `contoso-local-cl`) that maps to the cluster
3. Verify Arc connectivity in the Azure Portal

```bash
# Verify Arc Resource Bridge status
az arcappliance show --resource-group rg-contoso-local --name contoso-arc-bridge

# List custom locations
az customlocation list --resource-group rg-contoso-local -o table
```

---

### Step 3: Deploy Arc-enabled Kubernetes

**What:** Provision an AKS cluster on Azure Local, managed via Azure Arc.

**Why:** This replaces the cloud AKS cluster from the `main` branch. The Kubernetes API and workloads run on-premises, but the cluster is visible and manageable from the Azure Portal.

**Key concepts:**
- **AKS on Azure Local** is a full Kubernetes 1.35 distribution that runs on HCI nodes
- The cluster is **Arc-connected** — it appears as a `Microsoft.Kubernetes/connectedClusters` resource in Azure
- You can manage it with `kubectl` (direct) or through the Azure Portal (via Arc)
- **Logical Networks** in Azure Local provide IP address management for pods and services

**Actions:**
1. Create a Logical Network for Kubernetes (e.g., `10.0.0.0/16`)
2. Provision the AKS cluster targeting your Custom Location

```bash
# Create Arc-enabled AKS cluster on Azure Local
az aksarc create \
  --resource-group rg-contoso-local \
  --name contoso-arc-k8s \
  --custom-location contoso-local-cl \
  --vnet-ids <logical-network-id> \
  --node-count 3 \
  --generate-ssh-keys
```

---

### Step 4: Deploy Arc-enabled SQL Managed Instance

**What:** Deploy a SQL Managed Instance on the Azure Local cluster, managed via Azure Arc Data Services.

**Why:** This replaces Azure SQL MI (PaaS) from the `main` branch. The database runs on-premises for data sovereignty and low latency, but remains visible in the Azure Portal.

**Key concepts:**
- **Arc Data Controller** is the management plane for Arc-enabled data services (SQL MI, PostgreSQL). It runs as pods in the K8s cluster.
- **Arc-enabled SQL MI** is a full SQL Server instance running in Kubernetes, managed through Azure Arc
- The SQL MI is accessible from within the cluster network — no public endpoint
- Billing and monitoring flow through Arc to Azure

**Actions:**
1. Deploy the Arc Data Controller
2. Deploy the SQL Managed Instance
3. Create the `InsuranceDb` database

```bash
# Deploy Arc Data Controller
az arcdata dc create \
  --name contoso-dc \
  --resource-group rg-contoso-local \
  --custom-location contoso-local-cl \
  --connectivity-mode direct \
  --storage-class default

# Deploy Arc-enabled SQL MI
az sql mi-arc create \
  --name contoso-sql \
  --resource-group rg-contoso-local \
  --custom-location contoso-local-cl \
  --data-controller contoso-dc \
  --cores-limit 4 \
  --memory-limit 8Gi

# Create the application database
kubectl exec -it contoso-sql-0 -n contoso-dc -- \
  /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P '<password>' \
  -Q "CREATE DATABASE InsuranceDb"
```

---

### Step 5: Configure Arc Extensions (Monitoring, Key Vault, Policy)

**What:** Install Arc K8s extensions that bring Azure cloud capabilities to the on-premises cluster.

**Why:** These extensions bridge the gap between self-managed K8s and Azure-managed AKS. They provide the same monitoring, secret management, and policy enforcement you had in the cloud.

**Key concepts:**
- **Arc K8s Extensions** are Helm charts deployed and managed by Azure Arc. You install them via the Azure CLI, and Arc handles upgrades.
- **ALB Controller extension** enables Application Gateway for Containers (AGC) on the Arc-connected cluster — this is the ingress controller
- **Container Insights extension** forwards logs and metrics to Azure Monitor
- **Key Vault CSI driver** mounts Key Vault secrets directly into pod volumes
- **Azure Policy extension** enforces compliance policies on the cluster

**Actions:**

```bash
# Install ALB Controller extension (AGC ingress — replaces NGINX + MetalLB)
az k8s-extension create \
  --cluster-name contoso-arc-k8s \
  --resource-group rg-contoso-local \
  --cluster-type connectedClusters \
  --extension-type Microsoft.ServiceNetworking.Alb \
  --name alb-controller

# Install monitoring extension (Container Insights → Azure Monitor)
az k8s-extension create \
  --cluster-name contoso-arc-k8s \
  --resource-group rg-contoso-local \
  --cluster-type connectedClusters \
  --extension-type Microsoft.AzureMonitor.Containers \
  --name azuremonitor \
  --configuration-settings \
    logAnalyticsWorkspaceResourceID=<workspace-id>

# Install Key Vault CSI driver
az k8s-extension create \
  --cluster-name contoso-arc-k8s \
  --resource-group rg-contoso-local \
  --cluster-type connectedClusters \
  --extension-type Microsoft.AzureKeyVaultSecretsProvider \
  --name akvsecretsprovider

# Install Azure Policy extension
az k8s-extension create \
  --cluster-name contoso-arc-k8s \
  --resource-group rg-contoso-local \
  --cluster-type connectedClusters \
  --extension-type Microsoft.PolicyInsights \
  --name azurepolicy
```

> **💡 Note:** The ALB Controller extension automatically installs Gateway API CRDs on the cluster. No manual CRD installation needed.

---

### Step 6: Deploy Application via kubectl (or GitOps)

**What:** Deploy the Contoso Insurance application to the Arc-enabled K8s cluster using the manifests in the `k8s/` directory.

**Why:** The application containers are identical to the cloud version. We're deploying the same images, but now they run on Azure Local hardware.

**Key concepts:**
- Container images are pulled from **Azure Container Registry** — the cluster has outbound internet access
- Service-to-service communication uses **Kubernetes ClusterIP** services and DNS
- Environment variables and connection strings come from **ConfigMaps** and **Secrets** (backed by Key Vault CSI)

**Option A — Direct kubectl apply:**

```bash
# Connect to the Arc-enabled cluster
az connectedk8s proxy --name contoso-arc-k8s --resource-group rg-contoso-local

# Deploy in order
kubectl apply -f k8s/namespace.yaml
kubectl apply -f k8s/configmap.yaml
kubectl apply -f k8s/secrets.yaml
kubectl apply -f k8s/rabbitmq-deployment.yaml
kubectl apply -f k8s/api-deployment.yaml
kubectl apply -f k8s/worker-deployment.yaml
kubectl apply -f k8s/web-deployment.yaml
kubectl apply -f k8s/network-policies.yaml
```

**Option B — GitOps with Flux (recommended for production):**

```bash
# Create a GitOps configuration pointing to this repo
az k8s-configuration flux create \
  --cluster-name contoso-arc-k8s \
  --resource-group rg-contoso-local \
  --cluster-type connectedClusters \
  --name contoso-gitops \
  --url https://github.com/EmeaAppGbb/ContosoInsurances-NativeToLocal \
  --branch local-connected \
  --kustomization name=app path=./k8s prune=true
```

> **💡 Learning note:** GitOps means the cluster continuously reconciles its state with the Git repository. Push a change to `k8s/`, and Flux automatically applies it. No manual `kubectl` needed.

---

### Step 7: Configure AGC with ALB Controller Arc Extension

**What:** Install the ALB Controller as an Arc extension on the on-prem cluster and configure Application Gateway for Containers (AGC) for ingress.

**Why:** AGC provides the **same managed ingress** for on-prem Arc K8s that it provides for cloud AKS. The ALB Controller Arc extension manages `Gateway` and `HTTPRoute` resources on the cluster and syncs them with the AGC data plane in Azure. Traffic flows from the internet through AGC in Azure cloud, through the Arc tunnel, to your on-prem pods.

**Key concepts:**
- **Application Gateway for Containers (AGC)** is an Azure cloud resource (`Microsoft.ServiceNetworking/trafficControllers`) that acts as the managed data plane for ingress. GA since November 2025.
- **ALB Controller** is deployed as an **Arc extension** on the on-prem cluster (on cloud AKS, it's an add-on — same controller, different deployment model).
- **Gateway API** resources (`Gateway` + `HTTPRoute`) define the routing rules — this is the Kubernetes community standard, replacing the deprecated `Ingress` resource.
- The AGC configuration is **identical** to what you'd use on cloud AKS (`main` branch). This is the key benefit of the April 2026 migration.

> **⚠️ Historical note:** Before April 2026, this step used NGINX Ingress Controller + MetalLB for on-prem ingress. Both NGINX Ingress (community) and AGIC were retired in March 2026. AGC with Arc extension is the replacement.

**Actions:**

```bash
# Install ALB Controller as an Arc extension
az k8s-extension create \
  --cluster-name contoso-arc-k8s \
  --resource-group rg-contoso-local \
  --cluster-type connectedClusters \
  --extension-type Microsoft.ServiceNetworking.Alb \
  --name alb-controller

# Create the AGC resource in Azure (if not already provisioned via Bicep)
az network alb create \
  --resource-group rg-contoso-local \
  --name contoso-agc \
  --location <azure-region>

# Create an AGC frontend
az network alb frontend create \
  --resource-group rg-contoso-local \
  --alb-name contoso-agc \
  --name contoso-frontend

# Apply Gateway API resources to the cluster
kubectl apply -f - <<EOF
apiVersion: gateway.networking.k8s.io/v1
kind: Gateway
metadata:
  name: contoso-gateway
  namespace: contoso-insurance
  annotations:
    alb.networking.azure.io/alb-id: /subscriptions/<sub>/resourceGroups/rg-contoso-local/providers/Microsoft.ServiceNetworking/trafficControllers/contoso-agc
spec:
  gatewayClassName: azure-alb-external
  listeners:
  - name: https
    port: 443
    protocol: HTTPS
    tls:
      mode: Terminate
      certificateRefs:
      - name: contoso-tls
---
apiVersion: gateway.networking.k8s.io/v1
kind: HTTPRoute
metadata:
  name: contoso-web-route
  namespace: contoso-insurance
spec:
  parentRefs:
  - name: contoso-gateway
  rules:
  - matches:
    - path:
        type: PathPrefix
        value: /
    backendRefs:
    - name: web
      port: 8080
EOF

# Verify the Gateway is programmed
kubectl get gateway contoso-gateway -n contoso-insurance
# Expected: PROGRAMMED=True, ADDRESS=<AGC public IP>
```

> **💡 Learning note:** The `Gateway` and `HTTPRoute` manifests above are **identical** to what you'd deploy on cloud AKS. The only difference is the ALB Controller deployment model (Arc extension vs AKS add-on). This is the power of AGC in connected mode — one ingress pattern for all environments.

---

### Step 8: Validate and Test

**What:** Verify the complete deployment — all pods running, services accessible, telemetry flowing.

**Why:** Connected mode has more moving parts than a cloud deployment. Validating each layer ensures nothing was misconfigured.

**Validation checklist:**

```bash
# 1. All pods running
kubectl get pods -n contoso-insurance
# Expected: web, api, worker, rabbitmq — all Running

# 2. Services have correct types
kubectl get svc -n contoso-insurance
# Expected: all ClusterIP

# 3. Gateway is programmed
kubectl get gateway -n contoso-insurance
# Expected: contoso-gateway with PROGRAMMED=True

# 4. AGC ingress is reachable (use the AGC public IP)
AGC_IP=$(kubectl get gateway contoso-gateway -n contoso-insurance -o jsonpath='{.status.addresses[0].value}')
curl -k https://$AGC_IP
# Expected: Contoso Insurance home page

# 5. API health check (via AGC)
curl -k https://$AGC_IP/api/health
# Expected: Healthy

# 6. RabbitMQ is processing events
kubectl logs -n contoso-insurance deployment/worker --tail=20
# Expected: "Connected to RabbitMQ", "Waiting for messages"

# 7. SQL MI connectivity
kubectl exec -it deployment/api -n contoso-insurance -- \
  curl -s http://localhost:8080/api/customers
# Expected: JSON response (empty array or seeded data)

# 8. Arc connectivity
az connectedk8s show --name contoso-arc-k8s --resource-group rg-contoso-local \
  --query connectivityStatus -o tsv
# Expected: Connected

# 9. ALB Controller extension status
az k8s-extension show \
  --cluster-name contoso-arc-k8s \
  --resource-group rg-contoso-local \
  --cluster-type connectedClusters \
  --name alb-controller --query provisioningState -o tsv
# Expected: Succeeded

# 10. Telemetry in Azure Monitor
# Open Azure Portal → Monitor → Application Insights → Live Metrics
# Expected: Request telemetry from the on-prem cluster
```

> **🎉 Success!** If all checks pass, you have the Contoso Insurance application running on Azure Local with full Azure Arc management, monitoring, and identity integration — with zero application code changes.

---

## 🔄 Why AGC? — Unified Ingress for Cloud and Edge

**Application Gateway for Containers (AGC)** is the successor to both AGIC and NGINX Ingress for Azure-connected Kubernetes clusters. As of April 2026, it is the only Azure-recommended ingress solution for AKS and Arc-enabled K8s.

### Deprecation Timeline

| Component | Status | Date | Replacement |
|---|---|---|---|
| **AGIC** (Application Gateway Ingress Controller) | ⛔ Retired | March 2026 | Application Gateway for Containers (AGC) |
| **NGINX Ingress Controller** (community) | ⛔ Retired | March 2026 | AGC or vendor-supported alternatives |
| **MetalLB** | ⚠️ Not needed | April 2026 | AGC manages load balancing for connected clusters |
| **Kubernetes Ingress API** | ⚠️ Maintenance mode | Ongoing | Gateway API (`Gateway` + `HTTPRoute`) |
| **Kubernetes 1.30** | ⛔ End of life | March 2026 | Kubernetes 1.35 (current GA) |

### Why This Matters for Connected Mode

Previously, migrating from cloud to on-prem meant **replacing** the ingress stack entirely:
- Cloud: Application Gateway WAF v2 + AGIC
- On-prem: NGINX Ingress Controller + MetalLB (completely different tools, configs, and operational knowledge)

**Now with AGC**, the ingress layer is **identical**:
- Cloud: AGC + ALB Controller (AKS add-on) + Gateway API
- On-prem: AGC + ALB Controller (Arc extension) + Gateway API ← **SAME!**

This means:
- ✅ **One set of Gateway/HTTPRoute manifests** works everywhere
- ✅ **One operational playbook** for ingress management
- ✅ **No on-prem load balancer** to manage (MetalLB eliminated)
- ✅ **Built-in WAF and TLS** via AGC — no cert-manager needed for ingress certs
- ✅ **Automatic scaling** handled by Azure — no capacity planning for ingress

### AGC Architecture in Connected Mode

```
                AGC Resource (Azure cloud)
                Microsoft.ServiceNetworking/trafficControllers
                ┌────────────────────────────────┐
Internet ──►    │  Public IP + WAF + TLS          │
                │  Near-instant autoscaling        │
                │  Managed data plane              │
                └────────────┬───────────────────┘
                             │
                      Arc Tunnel (443)
                             │
                ┌────────────▼───────────────────┐
                │  ALB Controller (Arc extension) │
                │  Manages Gateway + HTTPRoute    │  On-prem K8s cluster
                │  Syncs state with AGC           │
                └────────────┬───────────────────┘
                             │
                    ClusterIP services
                     (Web, API, etc.)
```

> **AGC has been GA since November 2025.** Arc extension support for connected mode was added in early 2026. See the [`main` branch](../../tree/main) for the cloud AKS equivalent.

---

## ⚠️ Deprecated Components

The following components have been removed from this architecture as of the April 2026 refresh:

| Component | Removed | Reason | Replacement |
|---|---|---|---|
| NGINX Ingress Controller (community) | ✅ | Retired March 2026 | AGC with ALB Controller Arc extension |
| MetalLB | ✅ | Not needed — AGC handles load balancing from Azure cloud | AGC |
| Application Gateway WAF v2 + AGIC | ✅ | AGIC retired March 2026 | AGC with built-in WAF |
| Kubernetes `Ingress` resources | ✅ | Superseded by Gateway API | `Gateway` + `HTTPRoute` resources |
| Kubernetes 1.30 | ✅ | End of life March 2026 | Kubernetes 1.35 |
| Helm (for ingress) | ✅ | NGINX + MetalLB Helm charts no longer needed | AGC installed via `az k8s-extension` |

> **If you are following older tutorials** that reference NGINX Ingress, MetalLB, AGIC, or `Ingress` resources, those patterns are no longer supported. This repository uses the current Azure-recommended stack — AGC with Gateway API.

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

> **Note:** Tests run against in-memory/SQLite test doubles and do not require Azure Local or Arc infrastructure. The test suite is identical across all branches.

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

- [`main`](../../tree/main) — Azure cloud deployment (primary development branch)
- **`local-connected`** — Azure Local connected mode ← You are here
- `local-disconnected` — Azure Local disconnected mode (branched from `local-connected`)

---

## 📄 License

This project is licensed under the [MIT License](LICENSE).
