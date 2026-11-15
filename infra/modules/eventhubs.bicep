// Event Hubs Standard als Kafka-Ersatz: gleicher Client-Code, Endpunkt <namespace>.servicebus.windows.net:9093.
// Topics entstehen HIER – Event Hubs erlaubt keine Topic-Verwaltung per Kafka-AdminClient (Kafka:ManageTopics=false).
// Unterschiede zu Kafka: docs/kafka-vs-event-hubs.md
param location string
param name string

resource namespace 'Microsoft.EventHub/namespaces@2024-01-01' = {
  name: name
  location: location
  sku: { name: 'Standard', tier: 'Standard', capacity: 1 } // 1 TU = 1000 Events/s eingehend
  properties: {
    isAutoInflateEnabled: false // Kostenbremse: lieber Throttling als automatisch mehr TUs
    minimumTlsVersion: '1.2'
  }
}

// Standard: höchstens 7 Tage (168 h) Aufbewahrung, Kompaktierung bis 1 GB pro Partition.
var hubs = [
  { name: 'ticks.v1', policy: 'Delete', hours: 24 }
  { name: 'instruments.v1', policy: 'Compact', hours: 0 } // State Transfer: letzter Snapshot pro Key
  { name: 'corporate-actions.v1', policy: 'Delete', hours: 168 } // lokal unbegrenzt – hier das Maximum
  { name: 'ticks.v1.history.dlt', policy: 'Delete', hours: 168 }
  { name: 'corporate-actions.v1.history-corporate-actions.dlt', policy: 'Delete', hours: 168 }
  { name: 'instruments.v1.search.dlt', policy: 'Delete', hours: 168 }
]

resource eventHubs 'Microsoft.EventHub/namespaces/eventhubs@2024-01-01' = [for hub in hubs: {
  parent: namespace
  name: hub.name
  properties: {
    partitionCount: 6 // wie lokal; bei Standard nachträglich nicht änderbar
    retentionDescription: hub.policy == 'Compact'
      ? { cleanupPolicy: 'Compact', tombstoneRetentionTimeInHours: 24 }
      : { cleanupPolicy: 'Delete', retentionTimeInHours: hub.hours }
  }
}]

// Nur Send und Listen – die Dienste dürfen nichts verwalten.
resource apps 'Microsoft.EventHub/namespaces/authorizationRules@2024-01-01' = {
  parent: namespace
  name: 'portal-apps'
  properties: { rights: [ 'Send', 'Listen' ] }
}

output namespaceName string = namespace.name
output bootstrapServers string = '${namespace.name}.servicebus.windows.net:9093'
output authorizationRuleName string = apps.name
