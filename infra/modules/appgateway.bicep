// ============================================================================
// Ingress Module — NGINX Ingress + MetalLB Guidance (Azure Local Connected)
// ============================================================================
//
// REPLACES: appgateway.bicep (cloud deployment on main branch)
//
// WHY THIS CHANGED:
// Azure Application Gateway is an Azure-native PaaS L7 load balancer. It
// does NOT exist on Azure Local. On-premises Kubernetes clusters need a
// different ingress strategy:
//
//   1. NGINX Ingress Controller — L7 reverse proxy running as K8s pods
//      Provides: TLS termination, path-based routing, rate limiting
//      Deployed via: K8s manifests (see k8s/ingress-nginx.yaml)
//
//   2. MetalLB — Bare-metal LoadBalancer implementation for K8s
//      Provides: External IP allocation for LoadBalancer services
//      Deployed via: K8s manifests (see k8s/metallb-config.yaml)
//      Needed because: On-prem K8s has no cloud provider to assign external IPs
//
//   3. Alternative: Azure Local SDN Load Balancer
//      If your Azure Local deployment uses SDN, you may use the built-in
//      software load balancer instead of MetalLB.
//
// SECURITY COMPARISON:
//   Cloud (App Gateway WAF v2)     → Azure Local (NGINX + ModSecurity)
//   - OWASP 3.2 managed rules     → OWASP CRS via ModSecurity plugin
//   - Bot protection               → Rate limiting + custom rules
//   - DDoS protection (Azure)      → Physical firewall / IPS
//   - TLS termination              → TLS termination (cert-manager)
//   - Azure-managed certificates   → cert-manager with Let's Encrypt or internal CA
//
// This Bicep module is a DOCUMENTATION MODULE — it does not deploy Azure
// resources. The actual ingress infrastructure is deployed as K8s workloads.
// ============================================================================
//
// DEPLOYMENT GUIDE:
//
// Step 1: Install MetalLB (bare-metal load balancer)
//   kubectl apply -f k8s/metallb-config.yaml
//
// Step 2: Install NGINX Ingress Controller
//   kubectl apply -f k8s/ingress-nginx.yaml
//   # Or via Helm:
//   # helm install ingress-nginx ingress-nginx/ingress-nginx \
//   #   --namespace ingress-nginx --create-namespace \
//   #   --set controller.service.type=LoadBalancer
//
// Step 3: Apply Ingress resources
//   kubectl apply -f k8s/web-deployment.yaml  # includes Ingress resource
//
// Step 4: Configure DNS
//   Point your domain to the MetalLB-assigned external IP
//
// ============================================================================

// This module intentionally has no deployable resources.
// It exists to maintain the same module structure as the cloud deployment
// and to serve as documentation for the ingress replacement strategy.
