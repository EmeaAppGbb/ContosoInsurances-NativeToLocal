// ============================================================================
// Application Gateway Module — Public-Facing Ingress
// This is the ONLY resource with a public IP. Routes internet traffic to the
// Web frontend service running in AKS. Includes WAF v2 with OWASP rules.
// ============================================================================

@description('Azure region')
param location string

@description('Unique resource token for naming')
param resourceToken string

@description('Resource tags')
param tags object

@description('Application Gateway subnet resource ID')
param appGwSubnetId string

// ---------------------------------------------------------------------------
// Variables
// ---------------------------------------------------------------------------

var abbrs = loadJsonContent('../abbreviations.json')
var appGwName = '${abbrs.applicationGateway}${resourceToken}'
var publicIpName = '${abbrs.publicIPAddress}appgw-${resourceToken}'

// ---------------------------------------------------------------------------
// Public IP Address
// ---------------------------------------------------------------------------

resource publicIp 'Microsoft.Network/publicIPAddresses@2024-01-01' = {
  name: publicIpName
  location: location
  tags: tags
  sku: {
    name: 'Standard'
  }
  properties: {
    publicIPAllocationMethod: 'Static'
    publicIPAddressVersion: 'IPv4'
  }
}

// ---------------------------------------------------------------------------
// WAF Policy — OWASP 3.2 rule set
// ---------------------------------------------------------------------------

resource wafPolicy 'Microsoft.Network/ApplicationGatewayWebApplicationFirewallPolicies@2024-01-01' = {
  name: 'waf-${resourceToken}'
  location: location
  tags: tags
  properties: {
    policySettings: {
      requestBodyCheck: true
      maxRequestBodySizeInKb: 128
      fileUploadLimitInMb: 100
      state: 'Enabled'
      mode: 'Prevention'
    }
    managedRules: {
      managedRuleSets: [
        {
          ruleSetType: 'OWASP'
          ruleSetVersion: '3.2'
        }
        {
          ruleSetType: 'Microsoft_BotManagerRuleSet'
          ruleSetVersion: '1.0'
        }
      ]
    }
  }
}

// ---------------------------------------------------------------------------
// Application Gateway v2 with WAF
// ---------------------------------------------------------------------------

resource appGw 'Microsoft.Network/applicationGateways@2024-01-01' = {
  name: appGwName
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'WAF_v2'
      tier: 'WAF_v2'
      capacity: 2
    }
    firewallPolicy: {
      id: wafPolicy.id
    }
    gatewayIPConfigurations: [
      {
        name: 'appGatewayIpConfig'
        properties: {
          subnet: {
            id: appGwSubnetId
          }
        }
      }
    ]
    frontendIPConfigurations: [
      {
        name: 'appGatewayFrontendIP'
        properties: {
          publicIPAddress: {
            id: publicIp.id
          }
        }
      }
    ]
    frontendPorts: [
      {
        name: 'port-80'
        properties: {
          port: 80
        }
      }
      {
        name: 'port-443'
        properties: {
          port: 443
        }
      }
    ]
    // Backend pool — will be updated post-deploy to point at AKS internal LB
    backendAddressPools: [
      {
        name: 'web-frontend-pool'
        properties: {
          backendAddresses: []
        }
      }
    ]
    backendHttpSettingsCollection: [
      {
        name: 'web-frontend-settings'
        properties: {
          port: 8080
          protocol: 'Http'
          cookieBasedAffinity: 'Disabled'
          requestTimeout: 30
          pickHostNameFromBackendAddress: false
        }
      }
    ]
    httpListeners: [
      {
        name: 'http-listener'
        properties: {
          frontendIPConfiguration: {
            id: resourceId('Microsoft.Network/applicationGateways/frontendIPConfigurations', appGwName, 'appGatewayFrontendIP')
          }
          frontendPort: {
            id: resourceId('Microsoft.Network/applicationGateways/frontendPorts', appGwName, 'port-80')
          }
          protocol: 'Http'
        }
      }
    ]
    // Route all HTTP traffic to the web frontend backend pool
    requestRoutingRules: [
      {
        name: 'web-routing-rule'
        properties: {
          priority: 100
          ruleType: 'Basic'
          httpListener: {
            id: resourceId('Microsoft.Network/applicationGateways/httpListeners', appGwName, 'http-listener')
          }
          backendAddressPool: {
            id: resourceId('Microsoft.Network/applicationGateways/backendAddressPools', appGwName, 'web-frontend-pool')
          }
          backendHttpSettings: {
            id: resourceId('Microsoft.Network/applicationGateways/backendHttpSettingsCollection', appGwName, 'web-frontend-settings')
          }
        }
      }
    ]
  }
}

// ---------------------------------------------------------------------------
// Resource Lock (production protection)
// ---------------------------------------------------------------------------

resource appGwLock 'Microsoft.Authorization/locks@2020-05-01' = {
  name: 'appgw-do-not-delete'
  scope: appGw
  properties: {
    level: 'CanNotDelete'
    notes: 'Application Gateway is the sole ingress point. Do not delete.'
  }
}

// ---------------------------------------------------------------------------
// Outputs
// ---------------------------------------------------------------------------

output appGatewayId string = appGw.id
output appGatewayName string = appGw.name
output publicIpAddress string = publicIp.properties.ipAddress
output publicIpId string = publicIp.id
