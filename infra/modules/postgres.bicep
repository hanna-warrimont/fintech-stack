// Ein Postgres-Server, je Dienst eine eigene Datenbank (kein Dienst greift auf die Datenbank eines anderen zu).
param location string
param prefix string
param suffix string
@secure()
param adminPassword string

resource server 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: '${prefix}-pg-${suffix}'
  location: location
  sku: { name: 'Standard_B1ms', tier: 'Burstable' } // kleinste sinnvolle Größe
  properties: {
    version: '17'
    administratorLogin: 'portaladmin'
    administratorLoginPassword: adminPassword
    storage: { storageSizeGB: 32 }
    backup: { backupRetentionDays: 7, geoRedundantBackup: 'Disabled' }
    highAvailability: { mode: 'Disabled' }
  }
}

// ponytail: öffentlicher Endpunkt, nur für Azure-Dienste freigegeben; Ausbau: VNet-Integration und Private Endpoint
resource allowAzure 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2024-08-01' = {
  parent: server
  name: 'AllowAzureServices'
  properties: { startIpAddress: '0.0.0.0', endIpAddress: '0.0.0.0' }
}

resource databases 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = [for db in [ 'instrumentdb', 'searchdb', 'historydb', 'keycloak' ]: {
  parent: server
  name: db
}]

output fqdn string = server.properties.fullyQualifiedDomainName
output adminLogin string = server.properties.administratorLogin
