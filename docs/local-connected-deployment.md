# Local-connected deployment guide

The `local-connected` branch shows how the same Contoso Insurance application moves from Azure public cloud into a connected hybrid deployment on **Azure Local**.

## What this branch demonstrates

- deployment into the **Azure Local Jumpstart sandbox**
- Kubernetes workloads running with **AKS Arc**
- **Arc-enabled services** and extensions providing Azure-based hybrid management
- the transition point between fully public/sovereign cloud hosting and fully disconnected edge hosting

## Target platform

The connected hybrid stage keeps the application architecture intact while changing the hosting model:

- the application workloads run on Azure Local infrastructure
- Kubernetes is provided through **AKS Arc**
- Azure Arc keeps management, governance, and connected operations available from Azure
- this stage is ideal for learning how the same app can move on-premises before an air-gapped deployment

## Recommended workflow

1. Check out the `local-connected` branch.
2. Prepare the **Azure Local Jumpstart sandbox** and confirm the required Arc-connected resources are available.
3. Review the branch-specific `infra/` and `k8s/` assets that configure the connected Azure Local deployment.
4. Follow the deployment steps documented in the `local-connected` branch to publish and deploy the application into the Arc-managed environment.

## Why it matters

This branch is the bridge between public or sovereign Azure deployments and a fully disconnected Azure Local environment. It shows that the **same application** can keep its architecture and service boundaries while the hosting model shifts to hybrid, Arc-managed infrastructure.
