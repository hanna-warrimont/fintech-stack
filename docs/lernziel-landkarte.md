# Lernziel-Landkarte

Jede Zeile ist ein Konzept: wo es im Code steht, welcher Test es absichert und wie man es im Betrieb beobachtet (Experiment). Status: `offen` / `teilweise` / `drin`.

| # | Konzept | Runde | Ort im Code | Test | Experiment | Status |
|---|---|---|---|---|---|---|
| 1 | Value Object mit Validierung (ISIN, Währung) | 2 | | | – | offen |
| 2 | Aggregat mit Zustandsautomat + Vier-Augen-Regel | 4 | | | – | offen |
| 3 | Result-Pattern, Fehlerkatalog | 2 | | | – | offen |
| 4 | Domain Event → Integration Event | 2 | | | – | offen |
| 5 | Optimistic Concurrency | 4 | | | Zwei Freigaben gleichzeitig | offen |
| 6 | Transactional Outbox + Dispatcher | 2 | | | Kafka stoppen, freigeben, Kafka starten | offen |
| 7 | Idempotenter Producer | 2 | | | – | offen |
| 8 | Key-Wahl & Partitionierung (InstrumentId statt ISIN) | 1 | `src/Simulator/TickPublisher.cs` | `TickPublisherTests` | ISIN-Wechsel mitten im Tick-Strom (Runde 4): Am Tick-Strom passiert nichts | drin |
| 9 | Consumer Groups: Arbeitsteilung vs. Broadcast | 1+3 | `src/Quote/TickConsumer.cs` (Broadcast; Arbeitsteilung folgt mit History in Runde 3) | `TickConsumerTests.Two_instances_with_own_groups_both_receive_every_tick` | Zweite Instanz History bzw. Quote | teilweise |
| 10 | Manueller Offset-Commit, at-least-once | 2 | | | Consumer nach Verarbeitung, vor Commit abschießen | offen |
| 11 | Inbox / idempotenter Consumer | 2 | | | Duplikate einschalten | offen |
| 12 | Sequenz-Dedupe | 3 | | | Duplikate einschalten | offen |
| 13 | Event-Time, verspätete Ticks | 3 | | | Verspätete Ticks einschalten | offen |
| 14 | Retry, DLT, Poison Message | 3 | | | Poison Message senden | offen |
| 15 | Event-carried State Transfer, kompaktiertes Topic | 2 | | | – | offen |
| 16 | Projektion neu aufbauen (Replay) | 2+3 | | | Search-DB löschen, neu aufbauen | offen |
| 17 | Zeitgesteuerte Wirksamkeit, sicher bei mehreren Instanzen | 4 | | | Job doppelt laufen lassen | offen |
| 18 | Verteilte Folge einer Domänenentscheidung (Split) | 4 | | | History aus, Split wirksam, History an | offen |
| 19 | Eventual Consistency in der UI | 4 | | | Freigabe beobachten | offen |
| 20 | SSE-Push, Fan-out | 1 | `src/Quote/QuoteHub.cs` | `QuoteHubTests` | 3 Browser-Tabs, Quote neu starten | drin |
| 21 | BFF: Cookie-Session, Tokens serverseitig, Rollen, Aggregation | 4 | | | – | offen |
| 22 | Distributed Tracing über Kafka | 1 | `src/Messaging/KafkaTracing.cs` | `KafkaTracingTests` | Trace `ticks.v1 publish` → `ticks.v1 process` im Dashboard (Task 5) | drin |
| 23 | Last & Consumer-Lag | 3 | | | Lastspitze, Lag beobachten | offen |
| 24 | Bicep: Module, Parameter, Container Apps, Event Hubs, Postgres | 5 | | | what-if, Deploy, Abriss | offen |
| 25 | Kafka vs. Event Hubs: Unterschiede | 5 | | | Gleicher Code gegen Event Hubs | offen |
