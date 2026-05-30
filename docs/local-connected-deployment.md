# Azure Local connected deployment

## Overview

This branch targets **Azure Local** in **connected mode** for Contoso Insurance. In this model, the application runs on-premises on an Azure Local cluster, while Azure Arc projects the environment into Azure for governance, policy, observability, identity integration, and day-2 operations.

Use this guide with the **Azure Local Jumpstart** sandbox to demonstrate hybrid deployment patterns without changing the application code. The platform differences are handled by the new `infra/local-connected/` scripts and `k8s/local-connected/` manifests.

## Prerequisites

- Azure subscription with permissions to register resource providers, assign RBAC, create Azure Arc resources, and use Azure Container Registry.
- Azure Local Jumpstart sandbox prepared from: <https://azurearcjumpstart.com/azure_jumpstart_local>
- Azure CLI with these extensions available on the operator workstation:
  - `connectedk8s`
  - `k8s-extension`
  - `customlocation`
  - `aksarc`
  - `arcappliance`
- `kubectl`, `helm`, `git`, and `.NET 10 SDK`
- Network access from Azure Local to Azure Resource Manager, Microsoft Entra ID, Azure Container Registry, Azure Monitor/Application Insights, and any optional Arc data services endpoints.
- DNS entries (or temporary hosts-file entries) for the web and backend portal host names that resolve to the MetalLB IP assigned to `ingress-nginx`.

## Architecture on Azure Local

```mermaid
flowchart LR
    subgraph Azure[Azure control plane]
        ARM[Azure Resource Manager]
        ARC[Azure Arc]
        ACR[Azure Container Registry]
        MON[Azure Monitor / App Insights]
        ENTRA[Microsoft Entra ID]
    end

    subgraph Local[Azure Local Jumpstart sandbox]
        subgraph AKS[AKS Arc cluster]
            NGINX[nginx ingress controller]
            WEB[Web frontend]
            BPORTAL[Backend portal]
            BAPI[Backend API]
            API[Public API]
            WORKERS[Claims, Quotes, Projection workers]
            SQL[SQL Server workload\n(or Arc SQL MI)]
            RMQ[RabbitMQ]
        end
        METAL[MetalLB]
        LOGNET[Logical network / VLAN]
    end

    WEB --> API
    BPORTAL --> BAPI
    API --> SQL
    BAPI --> SQL
    WORKERS --> SQL
    API --> RMQ
    BAPI --> RMQ
    WORKERS --> RMQ
    NGINX --> WEB
    NGINX --> BPORTAL
    METAL --> NGINX
    LOGNET --> METAL
    AKS -. Arc connection .-> ARC
    ARC --> ARM
    AKS --> ACR
    AKS --> MON
    BPORTAL --> ENTRA
    BAPI --> ENTRA
```

### What runs locally vs. what stays in Azure

| Component | Location |
| --- | --- |
| AKS Arc cluster, ingress, app services, RabbitMQ, SQL demo deployment | Azure Local |
| Azure Arc inventory, policy, extensions, custom location | Azure |
| Container registry | Azure Container Registry |
| Identity for backend portal and backend API | Microsoft Entra ID |
| Monitoring / telemetry | Azure Monitor / Application Insights |

## Parameters and secrets

Edit `infra/local-connected/azure-local-params.json` before deployment.

Required secrets are intentionally **not** stored in source control. Set them in the shell before running `deploy-app.ps1`:

```powershell
$env:CONTOSO_SQL_SA_PASSWORD = '<strong-password>'
$env:CONTOSO_RABBITMQ_PASSWORD = '<strong-password>'
$env:CONTOSO_BACKEND_PORTAL_CLIENT_SECRET = '<entra-app-secret>'
```

## Step-by-step deployment guide

### Step 1: Set up Azure Local Jumpstart sandbox

1. Start the Azure Local Jumpstart scenario and wait for the sandbox bootstrap to finish.
2. Confirm that the Azure Local cluster, AKS Arc cluster, and Arc-enabled Kubernetes connection are reachable from the jump box.
3. Clone this repository and switch to `local-connected`.

### Step 2: Configure AKS Arc on the Azure Local cluster

Run the setup script from the repo root:

```powershell
pwsh ./infra/local-connected/setup-azure-local.ps1 -ConfigFile ./infra/local-connected/azure-local-params.json
```

The script:
- registers the Azure Arc and Azure Local resource providers,
- ensures required Azure CLI extensions are present,
- validates the Jumpstart-provisioned Azure Local cluster and logical network inputs,
- connects the AKS Arc cluster to Azure Arc if needed,
- creates the Azure custom location,
- installs Azure Monitor and Azure Policy Arc extensions,
- grants the Arc-connected cluster managed identity `AcrPull` on the target ACR.

> Jumpstart already provisions the Azure Local cluster resource and logical networking. The script treats those artifacts as prerequisites and validates the expected names/IP ranges from the parameter file.

### Step 3: Set up container registry access

1. Make sure the ACR named in `azure-local-params.json` exists and contains the right RBAC for the Arc-connected cluster.
2. The deployment script performs token-based `az acr login --expose-token`, publishes application images with `.NET` container publishing, and creates the `acr-pull-secret` Kubernetes secret.
3. If your disconnected edge rules require it, replace the ACR endpoint with a registry mirror while keeping the same manifest structure.

### Step 4: Deploy the application

```powershell
pwsh ./infra/local-connected/deploy-app.ps1 -ConfigFile ./infra/local-connected/azure-local-params.json
```

The script:
- gets AKS Arc kubeconfig via `az aksarc get-credentials`,
- publishes all application containers to ACR,
- installs or upgrades `ingress-nginx`,
- installs or upgrades `MetalLB`,
- renders `k8s/local-connected/` with environment-specific values,
- deploys SQL Server, RabbitMQ, APIs, workers, portal, and web frontend,
- waits for rollouts and reports the ingress external IP.

### Step 5: Configure ingress and networking

- `ingress-nginx` is exposed through a `LoadBalancer` service.
- `MetalLB` advertises an IP from the configured on-premises pool.
- Create DNS records (or hosts-file entries in the sandbox) for:
  - `application.webHostname`
  - `application.backendPortalHostname`
- Ensure the IP range in `network.loadBalancerStartIp` / `network.loadBalancerEndIp` is free on the Azure Local logical network.

### Step 6: Verify the deployment

Run:

```powershell
kubectl get pods -n contoso-insurance
kubectl get ingress -n contoso-insurance
kubectl get svc -n ingress-nginx
```

Expected verification points:
- all app pods reach `Running` and `Ready`,
- `ingress-nginx-controller` has a MetalLB-assigned external IP,
- the web app loads over the local hostname,
- backend portal authentication redirects to Microsoft Entra ID,
- Azure Portal shows the connected cluster and custom location.

## Differences from cloud deployment

| Area | Cloud (`main`) | Azure Local connected (`local-connected`) |
| --- | --- | --- |
| Kubernetes platform | AKS | AKS Arc on Azure Local |
| Azure footprint | Native Azure resources | Azure Arc projections + custom location |
| Ingress | Application Gateway for Containers / AKS-native patterns | `ingress-nginx` + MetalLB |
| Networking | Azure VNet/subnets | Azure Local logical network / on-prem VLAN |
| SQL option | Azure SQL / cloud services | SQL Server container by default, Arc SQL MI optional |
| Image pull path | Direct AKS to ACR | Arc-connected cluster identity or token-based pull secret |
| AZD workflow | `azd up` native | Scripted alternative via `azure.local-connected.yaml` |

## Connected mode specifics

Connected mode keeps these Azure-backed capabilities active even though workloads run locally:

- Azure Portal inventory through Azure Arc
- Azure Policy / extension management on the Kubernetes cluster
- Microsoft Entra ID authentication for operator-facing services
- Azure Monitor / Application Insights telemetry
- Azure Container Registry as the primary image source
- Optional Arc data services and Arc-enabled SQL features

## Troubleshooting

| Issue | Likely cause | Resolution |
| --- | --- | --- |
| `az connectedk8s connect` fails | Wrong kube context or Arc providers not registered | Re-run `setup-azure-local.ps1` and validate `azureLocal.kubeContext` |
| `az aksarc get-credentials` fails | AKS Arc extension missing or cluster name mismatch | Check Jumpstart cluster name and `azureLocal.connectedClusterName` |
| `ingress-nginx` never gets an external IP | MetalLB IP pool invalid or L2 adjacency issue | Verify the address pool is routable on the logical network and unused |
| Pods cannot pull images | Missing `AcrPull` role or expired token-created secret | Re-run setup, then re-run deploy to recreate `acr-pull-secret` |
| Backend portal sign-in loops | Wrong Entra redirect URI or missing client secret | Update the Entra app registration and `CONTOSO_BACKEND_PORTAL_CLIENT_SECRET` |
| SQL workload stays unready | Storage class mismatch or weak SA password | Update `storage.storageClassName` and use a stronger password |
| App cannot reach Azure services | Proxy/firewall blocks outbound 443 | Allow egress to Azure endpoints required by Arc, Entra, ACR, and monitoring |

## Alternative AZD workflow

`azd` does not currently provision Azure Local resources directly. This repo includes `azure.local-connected.yaml` as an operator profile to document the equivalent workflow:

1. Use `azd env` only for application-level environment variables if desired.
2. Run `setup-azure-local.ps1` to prepare the Azure Arc and Azure Local side.
3. Run `deploy-app.ps1` to publish images and deploy Kubernetes resources.
4. Treat `azure.local-connected.yaml` as a profile/override reference rather than a fully native `azd up` path.

## References

- Azure Local Jumpstart: <https://azurearcjumpstart.com/azure_jumpstart_local>
- AKS on Azure Local: <https://learn.microsoft.com/en-us/azure/aks/hybrid/>
- Arc-enabled Kubernetes: <https://learn.microsoft.com/en-us/azure/azure-arc/kubernetes/>
- Arc-enabled SQL: <https://learn.microsoft.com/en-us/azure/azure-arc/data/>
