// Eine Container App – für alle 8 Dienste wiederverwendet.
param name string
param location string
param environmentId string
param registryServer string
param identityId string
param image string
param external bool
param targetPort int
param cpu string = '0.25'
param memory string = '0.5Gi'
param env array = []
@secure()
param secrets object = {} // Name → Wert; in env per { name, secretRef: <Name> } referenziert

resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: name // = Aspire-Ressourcenname: Im Environment löst http://<name> auf (Service Discovery wie lokal)
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identityId}': {} }
  }
  properties: {
    environmentId: environmentId
    configuration: {
      ingress: {
        external: external // nur frontend und keycloak sind von außen erreichbar
        targetPort: targetPort
        transport: 'auto'
        allowInsecure: false
      }
      registries: [ { server: registryServer, identity: identityId } ]
      secrets: [for s in items(secrets): { name: s.key, value: s.value }]
    }
    template: {
      containers: [
        {
          name: name
          image: image
          resources: { cpu: json(cpu), memory: memory }
          env: env
        }
      ]
      // Kafka-Consumer laufen dauerhaft; Skalierung auf 0 ist hier nicht sinnvoll – Kosten begrenzt der Abriss.
      scale: { minReplicas: 1, maxReplicas: 1 }
    }
  }
}

output fqdn string = external ? app.properties.configuration.ingress.fqdn : ''
