# Runde 1 – Walking Skeleton: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ein Kurs-Simulator schreibt Ticks nach Kafka. Ein Quote-Service liest sie und streamt sie per SSE über ein BFF an eine Nuxt-PDP mit Live-Ticker. Alles startet lokal mit einem Aspire-Befehl.

**Architecture:** Der Simulator publiziert `ticks.v1` (Key = InstrumentId, 6 Partitionen). Quote konsumiert mit einer eigenen Group pro Instanz (Broadcast) in einen In-Memory-Hub und bietet SSE an. Das BFF ist in dieser Runde nur ein YARP-Proxy unter `/api`. Nuxt proxyt `/api` zum BFF. OpenTelemetry-Kontext wird über Kafka-Header weitergereicht.

**Tech Stack:** .NET 10, Aspire 13.x (lokal installiert: 13.6.x), Confluent.Kafka, YARP, xUnit, Testcontainers.Kafka, Nuxt 4.

**Spec:** `docs/superpowers/specs/2026-11-13-instrumenten-portal-design.md` (Abschnitte 3, 5, 7, 8 und 10, Runde 1)

## Global Constraints

- Ziel-Framework `net10.0`, Aspire-Pakete in der Version 13.x, alle gleich.
- Kafka-Client ist **`Confluent.Kafka` direkt**. Kein `Aspire.Confluent.Kafka`, kein MassTransit oder Wolverine. Die Kafka-Konfiguration bleibt im Code sichtbar. Die Bootstrap-Server kommen aus `ConnectionStrings:kafka`, also aus Aspires `WithReference(kafka)`.
- Topic `ticks.v1` hat 6 Partitionen, der Key ist `InstrumentId.ToString("D")`, der Wert ist JSON (`JsonSerializerDefaults.Web`).
- Tick-Vertrag: `{ instrumentId, seq, price, volume, eventTime }`. Ticks haben **keinen** Event-Umschlag, der kommt erst in Runde 2 für fachliche Events.
- Bewusste Vereinfachungen bekommen einen `// ponytail:`-Kommentar mit Grenze und Ausbaupfad.
- Ein Konzept, eine Stelle: Jede Landkarten-Zeile zeigt auf genau eine Klasse oder Datei.
- UI-Texte sind deutsch. Keine UI-Bibliothek, schlichtes CSS. Farbe ist nie der einzige Träger von Information (Pfeil plus Farbe).
- Keine neuen Dependencies außer: `Confluent.Kafka`, `Yarp.ReverseProxy`, `Microsoft.Extensions.ServiceDiscovery.Yarp`, `Aspire.Hosting.Kafka`, `Aspire.Hosting.JavaScript`, `Testcontainers.Kafka`, `OpenTelemetry.Api` (über ServiceDefaults schon vorhanden).
- Docker muss laufen, für Aspire und Testcontainers. Zum Zeitpunkt der Planung lief der Docker-Daemon **nicht**: vor Task 1 starten.

## Review Focus

1. **Browser-Tab wird geschlossen:** Das SSE-Abo muss enden und der Subscriber aus dem Hub verschwinden, sonst entsteht ein Speicherleck → Test `Subscribe_ends_and_unregisters_on_cancel` (Task 3).
2. **Kaputte Nachricht auf `ticks.v1`:** Quote loggt sie und läuft weiter, statt den Consumer-Loop zu beenden → Test `Consumer_skips_malformed_message` (Task 3).
3. **Langsamer Client:** Ein Tab im Hintergrund darf den Hub nicht blockieren und keinen Speicher anhäufen. Alte Ticks werden verworfen, der neueste kommt an → Test `Slow_subscriber_drops_oldest_keeps_newest` (Task 3).
4. **Quote startet vor dem Topic:** Kein Absturz. Quote wartet in Aspire auf den Simulator (`WaitFor(simulator)`), und der legt das Topic an → Verifikation in Task 3, Step 9.
5. **Proxy puffert SSE:** Über BFF und Nuxt-Proxy müssen Ticks einzeln ankommen, nicht erst gesammelt beim Verbindungsende → `curl -N`-Verifikation in Task 4 und Browser-Prüfung in Task 5.

---

## File Structure

```
Portal.slnx
.gitignore
src/
  AppHost/Portal.AppHost.csproj, AppHost.cs          – Aspire-Orchestrierung
  ServiceDefaults/Portal.ServiceDefaults.csproj, Extensions.cs  – OTel, Health, Service Discovery (Template)
  Messaging/Portal.Messaging.csproj
    Topics.cs            – Topic-Namen + EnsureTopicAsync
    KafkaTracing.cs      – traceparent in Header schreiben/lesen (Landkarte 22)
    Contracts/Tick.cs    – Tick-Vertrag
  Simulator/Portal.Simulator.csproj
    Program.cs           – Endpunkte /instruments, Worker-Registrierung
    SeedInstruments.cs   – feste Instrumente (ponytail: bis Runde 2)
    PriceWalk.cs         – Random Walk
    TickPublisher.cs     – Key-Wahl + Serialisierung + Trace (Landkarte 8)
    SimulatorWorker.cs   – Takt-Schleife
  Quote/Portal.Quote.csproj
    Program.cs           – Endpunkte /quotes/{id}, /quotes/{id}/stream
    QuoteHub.cs          – letzter Kurs + Fan-out an Abonnenten (Landkarte 20)
    TickConsumer.cs      – Broadcast-Consumer (Landkarte 9)
  Bff/Portal.Bff.csproj
    Program.cs, appsettings.json – YARP-Routen
  Frontend/              – Nuxt 4
    nuxt.config.ts
    app/pages/index.vue
    app/pages/instrumente/[id].vue
    app/composables/useQuoteStream.ts
tests/Portal.Tests/Portal.Tests.csproj
  KafkaFixture.cs
  Messaging/KafkaTracingTests.cs
  Simulator/PriceWalkTests.cs
  Simulator/TickPublisherTests.cs
  Quote/QuoteHubTests.cs
  Quote/TickConsumerTests.cs
docs/lernziel-landkarte.md
```

---

### Task 1: Repo-Gerüst und Messaging-Basis (Trace-Weitergabe)

**Files:**
- Create: `.gitignore`, `Portal.slnx`, `src/AppHost/*`, `src/ServiceDefaults/*`, `src/Messaging/Portal.Messaging.csproj`, `src/Messaging/Topics.cs`, `src/Messaging/KafkaTracing.cs`, `src/Messaging/Contracts/Tick.cs`, `docs/lernziel-landkarte.md`
- Test: `tests/Portal.Tests/Messaging/KafkaTracingTests.cs`, `tests/Portal.Tests/KafkaFixture.cs`

**Interfaces:**
- Produces:
  - `namespace Portal.Messaging.Contracts; public sealed record Tick(Guid InstrumentId, long Seq, decimal Price, long Volume, DateTimeOffset EventTime);`
  - `public static class Topics { public const string Ticks = "ticks.v1"; public static Task EnsureTopicAsync(string bootstrapServers, string topic, int partitions = 6); }`: Ignoriert `ErrorCode.TopicAlreadyExists`, wirft bei allen anderen Fehlern.
  - `public static class KafkaTracing { public const string SourceName = "Portal.Messaging"; public static Activity? StartPublish(string topic, Headers headers); public static Activity? StartConsume(string topic, Headers headers); }`
  - `KafkaFixture : IAsyncLifetime` mit `string BootstrapServers`, xUnit-Collection `"kafka"`.

- [ ] **Step 1: Repo und Solution anlegen**

```bash
cd /Users/hanna/coding/ddd-teacher
git init
dotnet new gitignore
printf 'node_modules/\n.nuxt/\n.output/\n' >> .gitignore
dotnet new sln --format slnx -n Portal
dotnet new aspire-apphost -n Portal.AppHost -o src/AppHost
dotnet new aspire-servicedefaults -n Portal.ServiceDefaults -o src/ServiceDefaults
dotnet new classlib -n Portal.Messaging -o src/Messaging
dotnet new xunit -n Portal.Tests -o tests/Portal.Tests
dotnet sln add src/AppHost src/ServiceDefaults src/Messaging tests/Portal.Tests
dotnet add src/Messaging package Confluent.Kafka
dotnet add src/Messaging package OpenTelemetry.Api
dotnet add tests/Portal.Tests package Testcontainers.Kafka
dotnet add tests/Portal.Tests reference src/Messaging
```

Alle Projekte auf `net10.0`. Die Template-Platzhalter (`Class1.cs`, `UnitTest1.cs`) löschen.

- [ ] **Step 2: Failing Test für die Trace-Weitergabe schreiben**

`KafkaTracingTests.Consume_continues_trace_of_publish`: Ein `ActivityListener` lauscht auf `KafkaTracing.SourceName` (`Sample = AllDataAndRecorded`). Dann:

```csharp
var headers = new Headers();
using var publish = KafkaTracing.StartPublish(Topics.Ticks, headers);
publish!.Stop();
using var consume = KafkaTracing.StartConsume(Topics.Ticks, headers);

Assert.Equal(ActivityKind.Producer, publish.Kind);
Assert.Equal(ActivityKind.Consumer, consume!.Kind);
Assert.Equal(publish.TraceId, consume.TraceId);
Assert.Equal(publish.SpanId, consume.ParentSpanId);
Assert.NotNull(headers.FirstOrDefault(h => h.Key == "traceparent"));
```

Ein zweiter Test, `Consume_without_header_starts_new_trace`: Bei leeren Headern gibt `StartConsume` eine Activity ohne Parent zurück (`ParentSpanId == default`).

- [ ] **Step 3: Test laufen lassen, er muss fehlschlagen**

Run: `dotnet test tests/Portal.Tests --filter KafkaTracingTests`
Expected: Build-Fehler, `KafkaTracing` existiert nicht.

- [ ] **Step 4: `Tick`, `Topics` und `KafkaTracing` implementieren**

`KafkaTracing` nutzt `Propagators.DefaultTextMapPropagator` (OpenTelemetry.Api) mit Inject- und Extract-Callbacks auf Confluent `Headers` (UTF-8). Activity-Namen: `"{topic} publish"` / `"{topic} process"`. Tags: `messaging.system = "kafka"`, `messaging.destination.name = topic`. `StartPublish` schreibt immer den Header, auch ohne Listener: Dann wird der Kontext von `Activity.Current` geschrieben, falls vorhanden. Zusätzlich in `src/ServiceDefaults/Extensions.cs` beim Tracing `.AddSource("Portal.Messaging")` ergänzen.

- [ ] **Step 5: Test laufen lassen, er muss grün sein**

Run: `dotnet test tests/Portal.Tests --filter KafkaTracingTests`
Expected: 2 bestanden.

- [ ] **Step 6: `KafkaFixture` anlegen**

Testcontainers `KafkaBuilder` mit Image `confluentinc/cp-kafka:7.9.0`, gestartet in `InitializeAsync`. `[CollectionDefinition("kafka")]` teilt den Container über alle Kafka-Tests. Noch kein eigener Test, Task 2 nutzt die Fixture.

- [ ] **Step 7: Lernziel-Landkarte anlegen**

`docs/lernziel-landkarte.md`: Tabelle mit allen 25 Zeilen aus Spec Abschnitt 8. Spalten: `#`, `Konzept`, `Runde`, `Ort im Code`, `Test`, `Experiment`, `Status` (`offen` / `teilweise` / `drin`). Zeile 22 ausfüllen: `src/Messaging/KafkaTracing.cs`, `KafkaTracingTests`, Status `drin` (Ende-zu-Ende-Nachweis folgt in Task 5).

- [ ] **Step 8: Build prüfen**

Run: `dotnet build Portal.slnx`
Expected: `Build succeeded`, 0 Fehler.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "chore: solution skeleton, messaging tracing helpers, learning map"
```

---

### Task 2: Kurs-Simulator (Key-Wahl und Partitionierung)

**Files:**
- Create: `src/Simulator/Portal.Simulator.csproj` (`dotnet new web`), `Program.cs`, `SeedInstruments.cs`, `PriceWalk.cs`, `TickPublisher.cs`, `SimulatorWorker.cs`
- Modify: `src/AppHost/AppHost.cs`, `docs/lernziel-landkarte.md`
- Test: `tests/Portal.Tests/Simulator/PriceWalkTests.cs`, `tests/Portal.Tests/Simulator/TickPublisherTests.cs`

**Interfaces:**
- Consumes: `Tick`, `Topics`, `KafkaTracing` (Task 1).
- Produces:
  - `public sealed record SeedInstrument(Guid Id, string Isin, string Name, string Currency, decimal StartPrice);` und `public static class SeedInstruments { public static readonly IReadOnlyList<SeedInstrument> All; }`
  - `public static class PriceWalk { public static decimal Next(decimal current, Random rng); }`
  - `public sealed class TickPublisher(IProducer<string, string> producer, string topic = Topics.Ticks) { public Task PublishAsync(Tick tick, CancellationToken ct); }`
  - HTTP: `GET /instruments` → `[{ id, isin, name, currency }]`; `GET /instruments/{id:guid}` → Objekt oder 404.
  - Konfiguration: `Simulator:TicksPerSecondPerInstrument` (Standard 2).

- [ ] **Step 1: Projekt anlegen**

```bash
dotnet new web -n Portal.Simulator -o src/Simulator
dotnet sln add src/Simulator
dotnet add src/Simulator reference src/Messaging src/ServiceDefaults
dotnet add tests/Portal.Tests reference src/Simulator
```

- [ ] **Step 2: Failing Tests schreiben**

`PriceWalkTests.Walk_stays_positive_and_moves_at_most_half_percent`: `new Random(42)`, Start 240.00m, 10 000 Schritte. Pro Schritt: `next > 0`, `Math.Abs(next - current) <= current * 0.005m + 0.01m` (Rundung), `next == Math.Round(next, 2)`.

`TickPublisherTests.Ticks_of_one_instrument_land_on_one_partition_in_order` (`[Collection("kafka")]`):
- Topic `ticks-test-{Guid}` mit 6 Partitionen über `Topics.EnsureTopicAsync` anlegen.
- Für 3 InstrumentIds je 20 Ticks mit `Seq` 1..20 **abwechselnd** über `TickPublisher` publizieren (60 Nachrichten). `TickPublisher` bekommt dafür das Test-Topic als Konstruktorparameter.
- Mit einem Consumer (`AutoOffsetReset.Earliest`) alle 60 lesen, Timeout 30 s.
- Assert pro Instrument: alle Nachrichten haben **dieselbe Partition**, die `Seq` ist streng aufsteigend 1..20, und der Key ist `instrumentId.ToString("D")`.
- Assert insgesamt: Jede Nachricht hat einen `traceparent`-Header.

- [ ] **Step 3: Tests laufen lassen, sie müssen fehlschlagen**

Run: `dotnet test tests/Portal.Tests --filter "PriceWalkTests|TickPublisherTests"`
Expected: Build-Fehler, die Typen fehlen.

- [ ] **Step 4: Implementieren**

- `SeedInstruments.All`, feste Werte:

  | Id | ISIN | Name | Währung | Start |
  |---|---|---|---|---|
  | `6939a8d3-111d-409b-897b-996093dd32f8` | DE0007164600 | SAP SE | EUR | 240.00 |
  | `f30331b5-52ba-4753-955e-e2b1ab299093` | DE0007236101 | Siemens AG | EUR | 190.00 |
  | `624d6f7f-39d4-450f-9ab7-9b174ab0af11` | DE0008404005 | Allianz SE | EUR | 340.00 |
  | `f07c2bf3-78e1-4160-8672-29510ce2a233` | IE00B4L5Y983 | iShares Core MSCI World UCITS ETF | EUR | 105.00 |

  Mit `// ponytail: feste Seed-Liste, ersetzt durch instruments.v1 in Runde 2`.
- `PriceWalk.Next`: gleichverteilte Änderung in [−0,5 %, +0,5 %], auf 2 Nachkommastellen runden, Untergrenze 0.01.
- `TickPublisher.PublishAsync`: `using var activity = KafkaTracing.StartPublish(topic, headers)`, dann `producer.ProduceAsync(topic, new Message<string,string>{ Key = tick.InstrumentId.ToString("D"), Value = JsonSerializer.Serialize(tick, Web), Headers = headers }, ct)`. Kommentar an der Key-Zeile: Warum InstrumentId und nicht ISIN (Spec Abschnitt 5).
- `SimulatorWorker : BackgroundService`: Ruft beim Start `Topics.EnsureTopicAsync(bootstrap, Topics.Ticks)` auf. Startwert von `seq` pro Instrument ist `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()`, danach `+1` pro Tick, mit `// ponytail: monoton über Neustarts, solange < 1000 Ticks/s pro Instrument; sonst persistente seq`. Der Takt kommt aus `PeriodicTimer` mit `1s / TicksPerSecondPerInstrument`. Pro Takt wird pro Instrument ein Tick erzeugt (`Volume` zufällig 1..500, `EventTime = TimeProvider.GetUtcNow()`).
- `Program.cs`: `AddServiceDefaults()`. `IProducer<string,string>` als Singleton mit `BootstrapServers = config.GetConnectionString("kafka")`, `Acks = Acks.All`. `TickPublisher` und Worker registrieren, die zwei `/instruments`-Endpunkte anlegen, `MapDefaultEndpoints()`.

- [ ] **Step 5: Tests laufen lassen, sie müssen grün sein**

Run: `dotnet test tests/Portal.Tests --filter "PriceWalkTests|TickPublisherTests"`
Expected: 2 bestanden.

- [ ] **Step 6: AppHost verdrahten**

```csharp
var kafka = builder.AddKafka("kafka").WithKafkaUI();
var simulator = builder.AddProject<Projects.Portal_Simulator>("simulator")
    .WithReference(kafka).WaitFor(kafka);
```

Paket `Aspire.Hosting.Kafka` zum AppHost hinzufügen.

- [ ] **Step 7: Live prüfen**

Run: `dotnet run --project src/AppHost` (im Hintergrund)
Expected: Im Dashboard sind `kafka`, `kafka-kafkaui` und `simulator` *Running*. In der Kafka UI hat `ticks.v1` 6 Partitionen und wachsende Offsets, alle Nachrichten mit gleichem Key liegen in einer Partition. Danach AppHost beenden.

- [ ] **Step 8: Landkarte aktualisieren und committen**

Zeile 8: `src/Simulator/TickPublisher.cs`, `TickPublisherTests`, Status `drin`.

```bash
git add -A
git commit -m "feat(simulator): publish ticks keyed by InstrumentId to ticks.v1"
```

---

### Task 3: Quote-Service (Broadcast-Consumer, Hub, SSE)

**Files:**
- Create: `src/Quote/Portal.Quote.csproj` (`dotnet new web`), `Program.cs`, `QuoteHub.cs`, `TickConsumer.cs`
- Modify: `src/AppHost/AppHost.cs`, `docs/lernziel-landkarte.md`
- Test: `tests/Portal.Tests/Quote/QuoteHubTests.cs`, `tests/Portal.Tests/Quote/TickConsumerTests.cs`

**Interfaces:**
- Consumes: `Tick`, `Topics`, `KafkaTracing` (Task 1). `TickPublisher` (Task 2), nur in Tests.
- Produces:
  - `public sealed class QuoteHub { public void Publish(Tick tick); public Tick? Latest(Guid instrumentId); public IAsyncEnumerable<Tick> Subscribe(Guid instrumentId, CancellationToken ct); internal int SubscriberCount { get; } }`. `InternalsVisibleTo("Portal.Tests")`.
  - `public sealed class TickConsumer(QuoteHub hub, ConsumerConfig config, ILogger<TickConsumer> log, string topic = Topics.Ticks) : BackgroundService`
  - HTTP: `GET /quotes/{instrumentId:guid}` → Tick oder 404. `GET /quotes/{instrumentId:guid}/stream` → `text/event-stream`, Event-Typ `tick`, Daten = Tick-JSON.

- [ ] **Step 1: Projekt anlegen**

```bash
dotnet new web -n Portal.Quote -o src/Quote
dotnet sln add src/Quote
dotnet add src/Quote reference src/Messaging src/ServiceDefaults
dotnet add tests/Portal.Tests reference src/Quote
```

- [ ] **Step 2: Failing Tests für `QuoteHub` schreiben**

- `Subscribe_yields_latest_first`: `Publish(seq 1)`, danach `Subscribe`. Das erste Element hat `Seq == 1`.
- `Subscribe_receives_only_its_instrument`: Abo auf A, dann Publish B(seq 1) und A(seq 2). Das erste Element hat `InstrumentId == A`, `Seq == 2`.
- `Slow_subscriber_drops_oldest_keeps_newest`: Abo auf A ohne zu lesen. 100 Ticks (seq 1..100) publizieren, dann lesen, bis `Seq == 100` kommt. Assert: höchstens 16 Elemente gelesen, das letzte ist `Seq == 100`.
- `Subscribe_ends_and_unregisters_on_cancel`: Abo starten, das erste `MoveNextAsync` anstoßen und dann `cts.Cancel()`. Die Enumeration endet ohne unbehandelte Exception (`OperationCanceledException` ist erlaubt), und `SubscriberCount == 0`.
- `Latest_is_null_for_unknown_instrument`.

- [ ] **Step 3: Tests laufen lassen, sie müssen fehlschlagen**

Run: `dotnet test tests/Portal.Tests --filter QuoteHubTests`
Expected: Build-Fehler, `QuoteHub` fehlt.

- [ ] **Step 4: `QuoteHub` implementieren**

`ConcurrentDictionary<Guid, Tick>` für die letzten Kurse. Pro Abonnent ein `Channel.CreateBounded<Tick>(new BoundedChannelOptions(16){ FullMode = DropOldest })`. Abonnenten liegen in einem `ConcurrentDictionary<Guid, ConcurrentDictionary<Channel<Tick>, byte>>`. Beim Abonnieren erst registrieren, dann den letzten Kurs in den Channel schreiben. Im `finally` der Enumeration wird der Channel wieder abgemeldet. Dazu `// ponytail: nur im Speicher, nach Neustart leer bis zum nächsten Tick; Ausbau: kompaktiertes quotes.latest`.

- [ ] **Step 5: Tests laufen lassen, sie müssen grün sein**

Run: `dotnet test tests/Portal.Tests --filter QuoteHubTests`
Expected: 5 bestanden.

- [ ] **Step 6: Failing Tests für `TickConsumer` schreiben** (`[Collection("kafka")]`)

- `Two_instances_with_own_groups_both_receive_every_tick` (Broadcast, Landkarte 9): Eigenes Test-Topic. Zwei Hubs, zwei `TickConsumer` mit Group-IDs `quote-{Guid}` / `quote-{Guid}`, `AutoOffsetReset.Latest`, beide starten. Danach alle 200 ms einen Tick für Instrument A mit steigender Seq publizieren, bis **beide** Hubs `Latest(A)` haben (Timeout 30 s). Assert: Beide Hubs haben einen Tick.
- `Consumer_skips_malformed_message`: Ein Consumer mit `AutoOffsetReset.Earliest` auf ein frisches Topic. Erst eine Nachricht mit Wert `"kein json"` direkt per Producer, dann einen gültigen Tick über `TickPublisher`. Assert: `Latest(A)` ist innerhalb von 30 s gesetzt, und der Consumer-Task ist nicht im Zustand Faulted.

- [ ] **Step 7: `TickConsumer` implementieren und Tests laufen lassen**

`ExecuteAsync` startet die blockierende `Consume`-Schleife per `Task.Run(..., ct)`. Pro Nachricht: `using var activity = KafkaTracing.StartConsume(topic, result.Message.Headers)`, deserialisieren, dann `hub.Publish`. Bei `JsonException` oder einem `null`-Ergebnis wird mit Partition und Offset geloggt und übersprungen, dazu `// ponytail: kein DLT, kommt in Runde 3`. Beim Stoppen `consumer.Close()`. Kein Offset-Commit (`EnableAutoCommit = false`), mit `// ponytail: Broadcast-Group startet immer bei latest, Offsets sind bedeutungslos`.

Run: `dotnet test tests/Portal.Tests --filter TickConsumerTests`
Expected: 2 bestanden.

- [ ] **Step 8: Endpunkte und Registrierung in `Program.cs`**

`AddServiceDefaults()`, `QuoteHub` als Singleton, `TickConsumer` als Hosted Service. Die `ConsumerConfig` hat `GroupId = $"quote-{Guid.NewGuid():N}"`, `AutoOffsetReset.Latest`, `EnableAutoCommit = false`, und der Kommentar zur Group-ID erklärt Broadcast vs. Arbeitsteilung. Der Stream-Endpunkt gibt `TypedResults.ServerSentEvents(hub.Subscribe(id, ct).Select(t => new SseItem<Tick>(t, "tick")))` zurück, die .NET-10-API.

- [ ] **Step 9: AppHost verdrahten und live prüfen**

```csharp
var quote = builder.AddProject<Projects.Portal_Quote>("quote")
    .WithReference(kafka).WaitFor(simulator).WithReplicas(2);
```

Run: AppHost starten, dann `curl -N http://localhost:<quote-port>/quotes/6939a8d3-111d-409b-897b-996093dd32f8/stream` (Port aus dem Dashboard).
Expected: Etwa alle 500 ms ein `event: tick`-Block mit SAP-Kursen. In der Kafka UI gibt es zwei Consumer-Groups `quote-…`. Danach AppHost beenden.

- [ ] **Step 10: Landkarte aktualisieren und committen**

Zeile 9: `src/Quote/TickConsumer.cs` (Broadcast-Seite, Arbeitsteilung folgt in Runde 3), Test `Two_instances_with_own_groups_both_receive_every_tick`, Status `teilweise`. Zeile 20: `src/Quote/QuoteHub.cs`, `QuoteHubTests`, Status `drin`.

```bash
git add -A
git commit -m "feat(quote): broadcast tick consumer, quote hub and SSE stream"
```

---

### Task 4: BFF (YARP-Proxy)

**Files:**
- Create: `src/Bff/Portal.Bff.csproj` (`dotnet new web`), `Program.cs`, `appsettings.json`
- Modify: `src/AppHost/AppHost.cs`

**Interfaces:**
- Consumes: Simulator `GET /instruments[/{id}]` (Task 2), Quote `GET /quotes/{id}[/stream]` (Task 3).
- Produces: `GET /api/instruments[/{id}]`, `GET /api/quotes/{id}`, `GET /api/quotes/{id}/stream`. Das sind dieselben Antworten ohne das Präfix `/api`.

- [ ] **Step 1: Projekt anlegen**

```bash
dotnet new web -n Portal.Bff -o src/Bff
dotnet sln add src/Bff
dotnet add src/Bff reference src/ServiceDefaults
dotnet add src/Bff package Yarp.ReverseProxy
dotnet add src/Bff package Microsoft.Extensions.ServiceDiscovery.Yarp
```

- [ ] **Step 2: Routen konfigurieren**

`appsettings.json` → `ReverseProxy`: Route `instruments` (`/api/instruments/{**rest}` → Cluster `simulator`, Adresse `http://simulator`) und Route `quotes` (`/api/quotes/{**rest}` → Cluster `quote`, Adresse `http://quote`). Beide mit Transform `PathRemovePrefix: "/api"`. `Program.cs`: `AddServiceDefaults()`, `AddReverseProxy().LoadFromConfig(...).AddServiceDiscoveryDestinationResolver()`, `MapReverseProxy()`, `MapDefaultEndpoints()`. Am Cluster `simulator` steht der Kommentar `// ponytail: Instrumente kommen in Runde 1 aus dem Simulator, Runde 2 biegt die Route auf den Instrument-Service um`.

- [ ] **Step 3: AppHost verdrahten**

```csharp
var bff = builder.AddProject<Projects.Portal_Bff>("bff")
    .WithReference(simulator).WithReference(quote).WithExternalHttpEndpoints();
```

- [ ] **Step 4: Durch das BFF prüfen, auch die SSE-Pufferung**

Run: AppHost starten.
- `curl -s http://localhost:<bff-port>/api/instruments`: Erwartet wird ein JSON-Array mit 4 Instrumenten.
- `curl -s -o /dev/null -w '%{http_code}' http://localhost:<bff-port>/api/instruments/00000000-0000-0000-0000-000000000000`: Erwartet wird `404`.
- `curl -N http://localhost:<bff-port>/api/quotes/6939a8d3-111d-409b-897b-996093dd32f8/stream`: Ticks erscheinen **einzeln, laufend** und nicht erst gesammelt.

AppHost beenden.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(bff): YARP proxy for instruments and quotes"
```

---

### Task 5: Nuxt-Frontend mit PDP-Live-Ticker und Abnahme der Runde

**Files:**
- Create: `src/Frontend/*` (Nuxt 4, Template `minimal`), `app/pages/index.vue`, `app/pages/instrumente/[id].vue`, `app/composables/useQuoteStream.ts`
- Modify: `src/AppHost/AppHost.cs`, `docs/lernziel-landkarte.md`

**Interfaces:**
- Consumes: BFF `/api/instruments`, `/api/instruments/{id}`, `/api/quotes/{id}/stream` (Task 4).
- Produces: `useQuoteStream(instrumentId: string): { tick: Ref<Tick | null>, previous: Ref<Tick | null>, connected: Ref<boolean> }`

- [ ] **Step 1: Nuxt-Projekt anlegen**

```bash
cd /Users/hanna/coding/ddd-teacher/src
npx nuxi@latest init Frontend --template minimal --packageManager npm --gitInit false
```

Dann in `nuxt.config.ts` eintragen: `routeRules: { '/api/**': { proxy: \`${process.env.NUXT_BFF_URL}/api/**\` } }`, mit `// ponytail: Proxy-Ziel wird beim Start gelesen, reicht für lokal; Produktions-Build in Runde 5`.

- [ ] **Step 2: AppHost verdrahten**

`Aspire.Hosting.JavaScript` zum AppHost hinzufügen:

```csharp
builder.AddJavaScriptApp("frontend", "../Frontend", "dev")
    .WithHttpEndpoint(env: "PORT")
    .WithEnvironment("NUXT_BFF_URL", bff.GetEndpoint("http"))
    .WithReference(bff).WaitFor(bff).WithExternalHttpEndpoints();
```

Falls die API in der installierten 13.x-Version anders heißt, gilt die äquivalente Methode aus `Aspire.Hosting.JavaScript`.

- [ ] **Step 3: `useQuoteStream` und die Seiten bauen**

- `useQuoteStream`: In `onMounted` eine `EventSource('/api/quotes/{id}/stream')` öffnen und auf das Event `tick` hören, das `previous` und `tick` setzt. `onopen` und `onerror` setzen `connected`. In `onUnmounted` wird `close()` aufgerufen.
- `index.vue`: Überschrift „Instrumente“, Liste aus `useFetch('/api/instruments')`. Jeder Eintrag (Name, ISIN) ist ein Link auf `/instrumente/{id}`.
- `instrumente/[id].vue`: Name, ISIN und Währung aus `useFetch('/api/instruments/{id}')`. Bei 404 erscheint „Instrument nicht gefunden“ mit einem Link zurück. Der Ticker zeigt den Preis im Format `de-DE` mit Währung, den Pfeil ▲/▼ plus Farbe gegenüber `previous` und die Uhrzeit des Ticks (`HH:mm:ss`). Ist `connected` falsch, steht dort „Verbindung wird hergestellt …“. Der Preis bekommt **kein** `aria-live`, mit `<!-- ponytail: 2 Updates/s wären für Screenreader zu laut -->`.

- [ ] **Step 4: Ende-zu-Ende im Browser prüfen**

Run: AppHost starten, dann die Frontend-URL aus dem Dashboard öffnen.
Expected:
- Die Startseite listet 4 Instrumente.
- Ein Klick auf SAP öffnet die PDP, und der Preis aktualisiert sich etwa 2-mal pro Sekunde mit Pfeil.
- `/instrumente/00000000-0000-0000-0000-000000000000` zeigt „Instrument nicht gefunden“.

- [ ] **Step 5: Experimente der Runde durchspielen (Abnahme)**

- **20 Fan-out:** 3 Tabs auf derselben PDP zeigen alle denselben Kurs. Dann eine Quote-Replika im Dashboard stoppen und neu starten. Der betroffene Tab zeigt kurz „Verbindung wird hergestellt …“, `EventSource` verbindet sich neu, und die Ticks laufen weiter.
- **9 Broadcast:** In der Kafka UI gibt es zwei `quote-…`-Groups, jede mit allen 6 Partitionen.
- **22 Tracing:** Im Aspire-Dashboard unter *Traces* einen Trace mit `ticks.v1 publish` (simulator) → `ticks.v1 process` (quote) finden.
- **8 Key-Wahl:** In der Kafka UI liegen alle Nachrichten eines Keys in einer Partition.

Abweichungen werden notiert und in diesem Task behoben, bevor committet wird.

- [ ] **Step 6: Landkarte abschließen und committen**

Zeilen 8, 9, 20 und 22 bekommen die Spalte Experiment mit Ablauf und Beobachtung aus Step 5. Den Status von Zeile 22 auf `drin` bestätigen.

```bash
git add -A
git commit -m "feat(frontend): Nuxt PDP with live ticker via BFF; round 1 learning map"
```

---

## Nicht in dieser Runde

Event-Umschlag, Outbox, Instrument-Service und Suche kommen in Runde 2. Arbeitsteilende Consumer Groups, DLT und Störschalter folgen in Runde 3, Keycloak-Login im BFF in Runde 4. Architektur-Tests kommen mit dem ersten Service mit Schichten (Runde 2). Frontend-Unit-Tests gibt es keine: Die Logik ist trivial, Step 4 und Step 5 in Task 5 decken das Verhalten ab.
