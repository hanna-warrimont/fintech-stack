# Lernziel-Landkarte

Jede Zeile ist ein Konzept: wo es im Code steht, welcher Test es absichert und wie man es im Betrieb beobachtet (Experiment). Status: `offen` / `teilweise` / `drin`.

| # | Konzept | Runde | Ort im Code | Test | Experiment | Status |
|---|---|---|---|---|---|---|
| 1 | Value Object mit Validierung (ISIN, Währung) | 2 | `src/Instruments/Portal.Instruments.Domain/Instruments/Isin.cs` (+ `Currency.cs`, `Wkn.cs`) | `IsinTests`, `CurrencyTests`, `WknTests` | – | drin |
| 2 | Aggregat mit Zustandsautomat + Vier-Augen-Regel | 4 | | | – | offen |
| 3 | Result-Pattern, Fehlerkatalog | 2 | `src/Instruments/Portal.Instruments.Domain/Abstractions/Result.cs` + `Instruments/InstrumentErrors.cs` | `InstrumentTests` | – | drin |
| 4 | Domain Event → Integration Event | 2 | `src/Instruments/Portal.Instruments.Infrastructure/Outbox/IntegrationEventMapper.cs` (Aufruf in `InstrumentDbContext.SaveChangesAsync`) | `Create_persists_and_writes_snapshot_to_outbox_in_same_transaction` | – | drin |
| 5 | Optimistic Concurrency | 4 | | | Zwei Freigaben gleichzeitig | offen |
| 6 | Transactional Outbox + Dispatcher | 2 | `src/Instruments/Portal.Instruments.Infrastructure/Outbox/OutboxDispatcher.cs` (+ Schreibseite `InstrumentDbContext.cs`) | `OutboxDispatcherTests`, `Failed_save_writes_no_outbox_row` | Kafka stoppen, freigeben, Kafka starten | drin |
| 7 | Idempotenter Producer | 2 | `OutboxDispatcher.ProducerConfig` | – (Konfiguration) | – | drin |
| 8 | Key-Wahl & Partitionierung (InstrumentId statt ISIN) | 1 | `src/Simulator/TickPublisher.cs` | `TickPublisherTests` | Beobachtet: `kafka-get-offsets` zeigt 4 Instrumente auf 3 von 6 Partitionen, jeder Key bleibt in seiner Partition. Runde 4: ISIN-Wechsel mitten im Tick-Strom, am Tick-Strom passiert nichts | drin |
| 9 | Consumer Groups: Arbeitsteilung vs. Broadcast | 1+3 | `src/Quote/TickConsumer.cs` (Broadcast; Arbeitsteilung folgt mit History in Runde 3) | `TickConsumerTests.Two_instances_with_own_groups_both_receive_every_tick` | Beobachtet: 2 Quote-Replikate → 2 Groups `quote-…`, jede mit allen 6 Partitionen; beide streamen dieselben Ticks. Runde 3: zweite History-Instanz (Arbeitsteilung) | teilweise |
| 10 | Manueller Offset-Commit, at-least-once | 2 | `src/Search/Portal.Search/SnapshotConsumer.cs` | `Consumer_redelivers_after_crash_before_commit_and_inbox_dedupes`, `Failed_apply_is_retried_not_skipped` | Consumer nach Verarbeitung, vor Commit abschießen | drin |
| 11 | Inbox / idempotenter Consumer | 2 | `src/Search/Portal.Search/InstrumentProjection.cs` | `Apply_is_idempotent_per_message`, `Older_revision_does_not_overwrite_newer` | Duplikate einschalten | drin |
| 12 | Sequenz-Dedupe | 3 | | | Duplikate einschalten | offen |
| 13 | Event-Time, verspätete Ticks | 3 | | | Verspätete Ticks einschalten | offen |
| 14 | Retry, DLT, Poison Message | 3 | | | Poison Message senden | offen |
| 15 | Event-carried State Transfer, kompaktiertes Topic | 2 | `src/Messaging/Contracts/InstrumentSnapshot.cs` + `src/Search/Portal.Search/InstrumentProjection.cs` | `TopicsTests.EnsureTopic_applies_config` | – | drin |
| 16 | Projektion neu aufbauen (Replay) | 2+3 | `src/Search/Portal.Search/SnapshotConsumer.cs` (Search; History folgt in Runde 3) | `Empty_projection_rebuilds_from_beginning` | Search-DB löschen, neu aufbauen | teilweise |
| 17 | Zeitgesteuerte Wirksamkeit, sicher bei mehreren Instanzen | 4 | | | Job doppelt laufen lassen | offen |
| 18 | Verteilte Folge einer Domänenentscheidung (Split) | 4 | | | History aus, Split wirksam, History an | offen |
| 19 | Eventual Consistency in der UI | 4 | | | Freigabe beobachten | offen |
| 20 | SSE-Push, Fan-out | 1 | `src/Quote/QuoteHub.cs`, `src/Frontend/app/composables/useQuoteStream.ts` | `QuoteHubTests` | Beobachtet: 3 Tabs zeigen denselben Kurs. Replikat sauber stoppen → `CompleteAll` beendet die Streams, EventSource verbindet sofort neu. Replikat hart abschießen (`kill -9`) → reicht ein Proxy den Abbruch nicht weiter, verbindet der Watchdog (10 s ohne Tick) neu. Tab schließen → Abo endet bis in den Quote-Service (Nuxt-Route `server/api/[...path].ts` bricht den Upstream ab) | drin |
| 21 | BFF: Cookie-Session, Tokens serverseitig, Rollen, Aggregation | 4 | | | – | offen |
| 22 | Distributed Tracing über Kafka | 1 | `src/Messaging/KafkaTracing.cs` | `KafkaTracingTests` | Beobachtet: Dashboard → Traces: `simulator: ticks.v1 publish` mit Spans von simulator und quote in einem Trace | drin |
| 23 | Last & Consumer-Lag | 3 | | | Lastspitze, Lag beobachten | offen |
| 24 | Bicep: Module, Parameter, Container Apps, Event Hubs, Postgres | 5 | | | what-if, Deploy, Abriss | offen |
| 25 | Kafka vs. Event Hubs: Unterschiede | 5 | | | Gleicher Code gegen Event Hubs | offen |
