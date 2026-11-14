// Log Analytics + Container Apps Environment (Consumption) + Aspire-Dashboard für Traces und Logs.
param location string
param prefix string
param suffix string

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${prefix}-logs-${suffix}'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
    workspaceCapping: { dailyQuotaGb: json('0.5') } // Kostenbremse: höchstens 0,5 GB Ingest pro Tag
  }
}

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${prefix}-env-${suffix}'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

// Aspire-Dashboard als Komponente der Umgebung – dasselbe Dashboard wie lokal, für Traces über Event Hubs.
resource dashboard 'Microsoft.App/managedEnvironments/dotNetComponents@2024-10-02-preview' = {
  parent: environment
  name: 'aspire-dashboard'
  properties: {
    componentType: 'AspireDashboard'
  }
}

output environmentId string = environment.id
output defaultDomain string = environment.properties.defaultDomain
