@description('Name of the web app (must be globally unique).')
param appName string = 'cardgames-mauricio'

@description('Azure region for all resources.')
param location string = 'brazilsouth'

@description('App Service Plan SKU.')
@allowed(['B1', 'B2', 'B3', 'S1', 'S2', 'S3'])
param sku string = 'B1'

// ── App Service Plan ────────────────────────────────────────────────────────

resource appServicePlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: '${appName}-plan'
  location: location
  kind: 'linux'
  sku: {
    name: sku
  }
  properties: {
    reserved: true  // required for Linux
  }
}

// ── Web App ─────────────────────────────────────────────────────────────────

resource webApp 'Microsoft.Web/sites@2023-12-01' = {
  name: appName
  location: location
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      webSocketsEnabled: true        // required for Blazor Server / SignalR
      minTlsVersion: '1.2'
      remoteDebuggingEnabled: false
      http20Enabled: true
    }
  }
}

// ── Outputs ──────────────────────────────────────────────────────────────────

output appUrl string = 'https://${webApp.properties.defaultHostName}'
