// Container-Apps-Job, der pro Dienst eine eigene Postgres-Rolle anlegt (Skript: infra/postgres/roles.sql).
// Bicep/ARM kann keine Rollen IN Postgres anlegen. Der Job läuft in Azure, also lässt ihn die Firewall
// (AllowAzureServices) durch – lokal braucht es weder psql noch eine Firewall-Ausnahme für die eigene IP.
// Trigger: manuell, deploy.sh startet ihn zwischen Infrastruktur und Apps und wartet auf das Ergebnis.
param location string
param environmentId string
param postgresFqdn string
param adminLogin string
@secure()
param adminPassword string
@secure()
param instrumentDbPassword string
@secure()
param searchDbPassword string
@secure()
param historyDbPassword string
@secure()
param keycloakDbPassword string

resource job 'Microsoft.App/jobs@2024-03-01' = {
  name: 'db-roles'
  location: location
  properties: {
    environmentId: environmentId
    configuration: {
      triggerType: 'Manual'
      manualTriggerConfig: { parallelism: 1, replicaCompletionCount: 1 }
      replicaTimeout: 300
      replicaRetryLimit: 1
      secrets: [
        { name: 'admin-password', value: adminPassword }
        { name: 'instrument-password', value: instrumentDbPassword }
        { name: 'search-password', value: searchDbPassword }
        { name: 'history-password', value: historyDbPassword }
        { name: 'keycloak-password', value: keycloakDbPassword }
      ]
    }
    template: {
      containers: [
        {
          name: 'psql'
          image: 'docker.io/library/postgres:16-alpine' // gleiche Hauptversion wie der Server, bringt psql mit
          command: [ 'sh', '-c', 'printf "%s" "$ROLES_SQL" | psql -f -' ]
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: [
            { name: 'ROLES_SQL', value: loadTextContent('../postgres/roles.sql') }
            // Standard-Variablen von libpq: Verbindung zur Datenbank "postgres" als Server-Admin
            { name: 'PGHOST', value: postgresFqdn }
            { name: 'PGDATABASE', value: 'postgres' }
            { name: 'PGUSER', value: adminLogin }
            { name: 'PGPASSWORD', secretRef: 'admin-password' }
            { name: 'PGSSLMODE', value: 'require' }
            { name: 'INSTRUMENT_DB_PASSWORD', secretRef: 'instrument-password' }
            { name: 'SEARCH_DB_PASSWORD', secretRef: 'search-password' }
            { name: 'HISTORY_DB_PASSWORD', secretRef: 'history-password' }
            { name: 'KEYCLOAK_DB_PASSWORD', secretRef: 'keycloak-password' }
          ]
        }
      ]
    }
  }
}
