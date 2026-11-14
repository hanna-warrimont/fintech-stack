// Container Registry Basic. Kein Admin-Benutzer: Die Apps ziehen per Managed Identity (AcrPull, siehe app.bicep).
param location string
@description('Nur Buchstaben und Ziffern (ACR-Regel).')
param name string

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: name
  location: location
  sku: { name: 'Basic' }
  properties: { adminUserEnabled: false }
}

output name string = registry.name
output loginServer string = registry.properties.loginServer
