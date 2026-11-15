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
@description('true für die .NET-Dienste: Probes auf /alive und /health aus ServiceDefaults. Sonst die Standard-Probes von Container Apps.')
param healthChecks bool = false

// Startup schützt die Liveness, solange der Dienst vor app.Run() migriert und Topics anlegt (bis ~100 s).
// Liveness = Prozess lebt (nur der "self"-Check); Readiness = Datenbank erreichbar, sonst kein Traffic, aber kein Neustart.
var probes = healthChecks ? [
  { type: 'Startup', httpGet: { path: '/alive', port: targetPort }, initialDelaySeconds: 5, periodSeconds: 10, failureThreshold: 10 }
  { type: 'Liveness', httpGet: { path: '/alive', port: targetPort }, periodSeconds: 10, failureThreshold: 3 }
  { type: 'Readiness', httpGet: { path: '/health', port: targetPort }, periodSeconds: 10, failureThreshold: 3 }
] : []

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
        // Intern rufen sich die Apps per http://<name> auf (wie lokal). Mit allowInsecure:false antwortete der
        // Ingress darauf mit 301 → https://<name>, und YARP, HttpClient und der Nuxt-Proxy scheiterten daran.
        // Von außen bleibt es HTTPS-only.
        allowInsecure: !external
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
          // Telemetrie an das Aspire-Dashboard der Umgebung (Wert wie in Aspires eigenem Container-Apps-Generator;
          // beim ersten Deploy prüfen). ServiceDefaults exportiert nur, wenn OTEL_EXPORTER_OTLP_ENDPOINT gesetzt ist.
          env: concat(env, [
            { name: 'OTEL_EXPORTER_OTLP_ENDPOINT', value: 'http://aspire-dashboard:18889' }
            { name: 'OTEL_EXPORTER_OTLP_PROTOCOL', value: 'grpc' }
            { name: 'OTEL_SERVICE_NAME', value: name }
          ])
          probes: probes
        }
      ]
      // Kafka-Consumer laufen dauerhaft; Skalierung auf 0 ist hier nicht sinnvoll – Kosten begrenzt der Abriss.
      scale: { minReplicas: 1, maxReplicas: 1 }
    }
  }
}

output fqdn string = external ? app.properties.configuration.ingress.fqdn : ''
