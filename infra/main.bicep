// Instrumenten-Portal in Azure (Landkarte 24). Alles in EINER Ressourcengruppe – Abriss = az group delete (destroy.sh).
// Zwei Durchläufe (deploy.sh): erst Infrastruktur (deployApps=false), dann Images pushen, dann Apps (deployApps=true).
targetScope = 'resourceGroup'

param location string = resourceGroup().location
param prefix string = 'portal'
param budgetEmail string
param budgetStartDate string = '${utcNow('yyyy-MM')}-01'
param deployApps bool = false
param imageTag string = 'latest'

@secure()
param postgresAdminPassword string
@secure()
param bffClientSecret string
@secure()
param keycloakAdminPassword string
@secure()
param piaPassword string
@secure()
param fredPassword string
@secure()
param friedaPassword string

var suffix = take(uniqueString(resourceGroup().id), 8)
// Namen hier berechnet (nicht aus Modul-Outputs): existing-Referenzen und Rollenzuweisungen brauchen Werte,
// die zu Beginn des Deployments feststehen.
var registryName = '${prefix}acr${suffix}'
var eventHubsName = '${prefix}-eh-${suffix}'

module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  params: { location: location, prefix: prefix, suffix: suffix }
}

module registry 'modules/registry.bicep' = {
  name: 'registry'
  params: { location: location, name: registryName }
}

module eventHubs 'modules/eventhubs.bicep' = {
  name: 'eventhubs'
  params: { location: location, name: eventHubsName }
}

module postgres 'modules/postgres.bicep' = {
  name: 'postgres'
  params: { location: location, prefix: prefix, suffix: suffix, adminPassword: postgresAdminPassword }
}

module budget 'modules/budget.bicep' = {
  name: 'budget'
  params: { budgetEmail: budgetEmail, startDate: budgetStartDate }
}

// Eine Identität für alle Apps, VOR den Apps berechtigt (AcrPull) – sonst scheitert der erste Image-Pull.
resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${prefix}-apps-${suffix}'
  location: location
}

resource acr 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: registryName
}

var acrPullRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')

resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(acr.id, identity.id, acrPullRole)
  scope: acr
  dependsOn: [ registry ]
  properties: {
    roleDefinitionId: acrPullRole
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// ---------- Apps (zweiter Durchlauf) ----------

var domain = monitoring.outputs.defaultDomain
var keycloakUrl = 'https://keycloak.${domain}'
var frontendUrl = 'https://frontend.${domain}'
var registryServer = registry.outputs.loginServer

resource ehNamespace 'Microsoft.EventHub/namespaces@2024-01-01' existing = {
  name: eventHubsName
}
resource ehRule 'Microsoft.EventHub/namespaces/authorizationRules@2024-01-01' existing = {
  parent: ehNamespace
  name: 'portal-apps'
}

// Kafka gegen Event Hubs: SASL-Passwort = Connection-String (direkt per listKeys, nie als Output).
var kafkaEnv = [
  { name: 'ConnectionStrings__kafka', value: eventHubs.outputs.bootstrapServers }
  { name: 'Kafka__SaslPassword', secretRef: 'kafka-connection' }
  { name: 'Kafka__ManageTopics', value: 'false' }
]
var kafkaSecret = { 'kafka-connection': ehRule.listKeys().primaryConnectionString }

func pg(fqdn string, db string, password string) string =>
  'Host=${fqdn};Database=${db};Username=portaladmin;Password=${password};Ssl Mode=Require'

module instrument 'modules/app.bicep' = if (deployApps) {
  name: 'app-instrument'
  dependsOn: [ acrPull, eventHubs, postgres ]
  params: {
    location: location
    environmentId: monitoring.outputs.environmentId
    registryServer: registryServer
    identityId: identity.id
    name: 'instrument'
    image: '${registryServer}/portal/instrument:${imageTag}'
    external: false
    targetPort: 8080
    healthChecks: true
    env: concat(kafkaEnv, [
      { name: 'ConnectionStrings__instrumentdb', secretRef: 'db' }
      { name: 'services__keycloak__http__0', value: keycloakUrl }
    ])
    secrets: union(kafkaSecret, { db: pg(postgres.outputs.fqdn, 'instrumentdb', postgresAdminPassword) })
  }
}

module search 'modules/app.bicep' = if (deployApps) {
  name: 'app-search'
  dependsOn: [ acrPull, eventHubs, postgres ]
  params: {
    location: location
    environmentId: monitoring.outputs.environmentId
    registryServer: registryServer
    identityId: identity.id
    name: 'search'
    image: '${registryServer}/portal/search:${imageTag}'
    external: false
    targetPort: 8080
    healthChecks: true
    env: concat(kafkaEnv, [ { name: 'ConnectionStrings__searchdb', secretRef: 'db' } ])
    secrets: union(kafkaSecret, { db: pg(postgres.outputs.fqdn, 'searchdb', postgresAdminPassword) })
  }
}

module history 'modules/app.bicep' = if (deployApps) {
  name: 'app-history'
  dependsOn: [ acrPull, eventHubs, postgres ]
  params: {
    location: location
    environmentId: monitoring.outputs.environmentId
    registryServer: registryServer
    identityId: identity.id
    name: 'history'
    image: '${registryServer}/portal/history:${imageTag}'
    external: false
    targetPort: 8080
    healthChecks: true
    env: concat(kafkaEnv, [ { name: 'ConnectionStrings__historydb', secretRef: 'db' } ])
    secrets: union(kafkaSecret, { db: pg(postgres.outputs.fqdn, 'historydb', postgresAdminPassword) })
  }
}

module quote 'modules/app.bicep' = if (deployApps) {
  name: 'app-quote'
  dependsOn: [ acrPull, eventHubs, postgres ]
  params: {
    location: location
    environmentId: monitoring.outputs.environmentId
    registryServer: registryServer
    identityId: identity.id
    name: 'quote'
    image: '${registryServer}/portal/quote:${imageTag}'
    external: false
    targetPort: 8080
    healthChecks: true
    env: kafkaEnv
    secrets: kafkaSecret
  }
}

module simulator 'modules/app.bicep' = if (deployApps) {
  name: 'app-simulator'
  dependsOn: [ acrPull, eventHubs, postgres ]
  params: {
    location: location
    environmentId: monitoring.outputs.environmentId
    registryServer: registryServer
    identityId: identity.id
    name: 'simulator'
    image: '${registryServer}/portal/simulator:${imageTag}'
    external: false
    targetPort: 8080
    healthChecks: true
    env: kafkaEnv
    secrets: kafkaSecret
  }
}

module bff 'modules/app.bicep' = if (deployApps) {
  name: 'app-bff'
  dependsOn: [ acrPull, eventHubs, postgres ]
  params: {
    location: location
    environmentId: monitoring.outputs.environmentId
    registryServer: registryServer
    identityId: identity.id
    name: 'bff'
    image: '${registryServer}/portal/bff:${imageTag}'
    external: false // nur über den Nuxt-Proxy erreichbar
    targetPort: 8080
    healthChecks: true
    env: [
      { name: 'services__instrument__http__0', value: 'http://instrument' }
      { name: 'services__search__http__0', value: 'http://search' }
      { name: 'services__quote__http__0', value: 'http://quote' }
      { name: 'services__history__http__0', value: 'http://history' }
      { name: 'services__keycloak__http__0', value: keycloakUrl }
      { name: 'Bff__ClientSecret', secretRef: 'bff-secret' }
      { name: 'Bff__TrustAllProxies', value: 'true' }
    ]
    secrets: { 'bff-secret': bffClientSecret }
  }
}

module frontend 'modules/app.bicep' = if (deployApps) {
  name: 'app-frontend'
  dependsOn: [ acrPull, eventHubs, postgres ]
  params: {
    location: location
    environmentId: monitoring.outputs.environmentId
    registryServer: registryServer
    identityId: identity.id
    name: 'frontend'
    image: '${registryServer}/portal/frontend:${imageTag}'
    external: true
    targetPort: 3000
    env: [ { name: 'NUXT_BFF_URL', value: 'http://bff' } ]
  }
}

module keycloak 'modules/app.bicep' = if (deployApps) {
  name: 'app-keycloak'
  dependsOn: [ acrPull, eventHubs, postgres ]
  params: {
    location: location
    environmentId: monitoring.outputs.environmentId
    registryServer: registryServer
    identityId: identity.id
    name: 'keycloak'
    image: '${registryServer}/portal/keycloak:${imageTag}'
    external: true // der Browser meldet sich hier an
    targetPort: 8080
    cpu: '0.5'
    memory: '1Gi'
    env: [
      { name: 'KC_DB_URL', value: 'jdbc:postgresql://${postgres.outputs.fqdn}:5432/keycloak?sslmode=require' }
      { name: 'KC_DB_USERNAME', value: 'portaladmin' }
      { name: 'KC_DB_PASSWORD', secretRef: 'db-password' }
      { name: 'KC_HOSTNAME', value: keycloakUrl }
      { name: 'KC_PROXY_HEADERS', value: 'xforwarded' } // TLS endet am Ingress
      { name: 'KC_HTTP_ENABLED', value: 'true' }
      { name: 'KC_BOOTSTRAP_ADMIN_USERNAME', value: 'admin' }
      { name: 'KC_BOOTSTRAP_ADMIN_PASSWORD', secretRef: 'admin-password' }
      // Platzhalter im Realm-Import (infra/keycloak/portal-realm.json)
      { name: 'PORTAL_FRONTEND_URL', value: frontendUrl }
      { name: 'PORTAL_BFF_SECRET', secretRef: 'bff-secret' }
      { name: 'PORTAL_PIA_PASSWORD', secretRef: 'pia-password' }
      { name: 'PORTAL_FRED_PASSWORD', secretRef: 'fred-password' }
      { name: 'PORTAL_FRIEDA_PASSWORD', secretRef: 'frieda-password' }
    ]
    secrets: {
      'db-password': postgresAdminPassword
      'admin-password': keycloakAdminPassword
      'bff-secret': bffClientSecret
      'pia-password': piaPassword
      'fred-password': fredPassword
      'frieda-password': friedaPassword
    }
  }
}

output registryName string = registry.outputs.name
output registryServer string = registryServer
output frontendUrl string = frontendUrl
output keycloakUrl string = keycloakUrl
