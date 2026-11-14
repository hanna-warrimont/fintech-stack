# Kafka (lokal) vs. Azure Event Hubs Standard

Der Client-Code ist in beiden Welten **derselbe** (`Confluent.Kafka`). Alle Unterschiede für die Dienste stecken an einer Stelle: [`src/Messaging/KafkaSettings.cs`](../src/Messaging/KafkaSettings.cs). Der Rest steht im Bicep ([`infra/modules/eventhubs.bicep`](../infra/modules/eventhubs.bicep)) oder ist eine Grenze, mit der das Portal lebt. Landkarte Zeile 25.

Stand: 2026-11-14. Quellen am Ende.

| Thema | Kafka lokal (Aspire) | Event Hubs Standard | Folge für das Portal | Stelle |
|---|---|---|---|---|
| Verbindung | `PLAINTEXT`, Port aus Aspire | `SASL_SSL` + `PLAIN`, Benutzer `$ConnectionString`, Passwort = Connection-String, Port 9093 | SASL nur, wenn `Kafka:SaslPassword` gesetzt ist | `KafkaSettings.Apply` |
| Leerlauf | Verbindungen bleiben offen | Azure schließt nach 240 s Leerlauf | `socket.keepalive.enable=true`, `metadata.max.age.ms=180000` | `KafkaSettings.Apply` |
| Timeouts | librdkafka-Standard | Producer `request.timeout.ms` > 20 s (empfohlen 60 s), Consumer `session.timeout.ms` 30 s | in Azure gesetzt | `KafkaSettings.Producer/Consumer` |
| Topics anlegen | per AdminClient beim Start (`EnsureTopicAsync`) | AdminClient für Topic-Verwaltung **nicht unterstützt** | `Kafka:ManageTopics=false`; Topics als Event Hubs im Bicep | `KafkaSettings.EnsureTopicAsync`, `eventhubs.bicep` |
| Partitionen | 6, beliebig | höchstens 32 pro Event Hub, bei Standard **nachträglich nicht änderbar** | 6 wie lokal | `eventhubs.bicep` |
| Anzahl Topics | beliebig | höchstens 10 Event Hubs pro Namespace | wir brauchen 5 (3 Topics, 2 DLTs) | `eventhubs.bicep` |
| Aufbewahrung | `ticks.v1` 7 Tage, `corporate-actions.v1` **unbegrenzt** (`retention.ms=-1`) | höchstens **7 Tage** | Ein History-Neuaufbau nach mehr als 7 Tagen verliert ältere Split-Faktoren, ältere Charts wären dann unangepasst. Ausbaupfad: Premium (90 Tage) oder Faktoren beim Neuaufbau aus dem Instrument-Service nachladen. | `eventhubs.bicep`, `Topics.LongRetention` |
| Kompaktierung | `cleanup.policy=compact` | unterstützt, bis **1 GB pro Partition** | `instruments.v1` bleibt Event-carried State Transfer, Search baut sich wie lokal neu auf | `eventhubs.bicep` |
| Durchsatz | so viel die Maschine schafft | 1 TU = 1 MB/s bzw. **1000 Events/s eingehend** | Die Lastspitze aus Runde 3 (`rate=300` × 4 Instrumente = 1200/s) **wird gedrosselt**. Auto-Inflate ist bewusst aus (Kosten). | `eventhubs.bicep` |
| Idempotenter Producer | ja | ja | gleich | `KafkaSettings.Producer` |
| Transaktionen / Exactly-once | ja | **nein** (Standard) | Das Portal nutzt keine: Outbox (Producer-Seite) und Inbox bzw. seq-Dedupe (Consumer-Seite) statt Transaktionen | `OutboxDispatcher`, `AdjustmentStore`, `CandleStore` |
| Kompression | alle Codecs | nur `gzip` | Das Portal komprimiert nicht | – |
| Consumer Groups | beliebig | Kafka-Groups entstehen automatisch, höchstens 1000 pro Namespace | Quote und Simulator erzeugen bei **jedem Start neue** Groups (Broadcast). Bei vielen Neustarts sammeln sie sich an, alte verfallen erst mit dem Offset-Ablauf. | `Quote/Program.cs`, `Simulator/Program.cs` |
| Broker-Konfiguration | voll im Zugriff | keine (verwalteter Dienst) | – | – |
| Kosten | 0 € | TU 0,0269 €/h **plus eigener Zähler „Kafka-Endpunkt“ 0,0808 €/h** | Den Kafka-Endpunkt bezahlt man extra, auch im Standard-Tarif | `docs/azure.md` |

## Was gleich bleibt (und das ist die Lektion)

- **Key = `InstrumentId`:** Die Reihenfolge pro Instrument hängt an der Partition, nicht am Broker-Produkt.
- **Consumer Groups:** Arbeitsteilung (`history`, `search`) gegenüber Broadcast (`quote-…`, `simulator-…`).
- **Commit erst nach der Verarbeitung, Retry und DLT:** alles Client-Code, alles unverändert.
- **Outbox und Inbox:** gerade weil Event Hubs Standard keine Transaktionen hat, ist das der portable Weg.

## Quellen

- [Azure Event Hubs Quotas and Limits](https://learn.microsoft.com/en-us/azure/event-hubs/event-hubs-quotas), abgerufen 2026-11-14
- [Apache Kafka client configurations for Azure Event Hubs](https://learn.microsoft.com/en-us/azure/event-hubs/apache-kafka-configurations), abgerufen 2026-11-14
- [Apache Kafka Protocol Support in Azure Event Hubs](https://learn.microsoft.com/en-us/azure/event-hubs/azure-event-hubs-apache-kafka-overview), abgerufen 2026-11-14
- [Azure Retail Prices API](https://prices.azure.com/api/retail/prices) (Event Hubs, `germanywestcentral`, EUR), abgerufen 2026-11-14
