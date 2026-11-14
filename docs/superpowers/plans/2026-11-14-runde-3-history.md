# Runde 3 – History, Störungen, Retry/DLT: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ein History-Service verdichtet `ticks.v1` zu 1-Minuten-Kerzen (Event-Time, Sequenz-Deduplizierung, verspätete Ticks). Daraus zeigt die PDP einen Kerzen-Chart mit 1m/1h/1d. Der Simulator bekommt Störschalter, und fehlerhafte Nachrichten landen nach Retries in einem Dead-Letter-Topic.

**Architecture:** History konsumiert arbeitsteilend mit der Group `history` und committet manuell. Pro Tick laufen Deduplizierung (`last_seq` pro Instrument) und Kerzen-Upsert in **einer** Transaktion. Fehler werden im Prozess mit Backoff wiederholt, danach geht die Nachricht ins DLT `ticks.v1.history.dlt` mit Fehler-Headern und wird committet. 1h und 1d entstehen per SQL-Rollup. Ist die Kerzen-Tabelle leer, baut History sie aus `ticks.v1` neu auf, so wie Search. Der Simulator bekommt HTTP-Störschalter (Duplikate, verspätete Ticks, Rate, Poison, Pause).

**Tech Stack:** .NET 10, Npgsql + Dapper, Confluent.Kafka, Aspire 13.6.5, xUnit + Testcontainers, Nuxt 4, `lightweight-charts` 5.2.1 (Apache-2.0, laut Spec Abschnitt 11).

**Spec:** `docs/superpowers/specs/2026-11-13-instrumenten-portal-design.md` (Abschnitte 5 „History“, 7 „Tick-Deduplizierung“, „Verspätete Ticks“, „Retry und DLT“, „Neuaufbau“, „Störschalter“, 8, 10, Runde 3)

## Global Constraints

- Alles aus Runde 1 und 2 gilt weiter: `net10.0`, Aspire 13.6.5, `Confluent.Kafka` direkt, kein MediatR, Dapper für SQL, `NpgsqlDataSource.Create`, `// ponytail:` für bewusste Vereinfachungen, ein Konzept an einer Stelle, deutsche UI-Texte.
- **History ist ein Projekt** (`src/History/Portal.History`), so wie Search. Die „dünne Domäne“ aus der Spec, also die Kerzen-Regeln, steckt als reine, unit-getestete Logik im SQL-Upsert und in einer Klasse `CandleBucket`. **Abweichung von Spec Abschnitt 5 (4 Schichten):** muss im Ledger stehen und der Nutzerin gemeldet werden.
- Tick-Deduplizierung über `seq` pro Instrument, **nicht** über die Inbox (Spec Abschnitt 7). Kerzen nach **Event-Time** (`Tick.EventTime`), nicht nach Verarbeitungszeit.
- DLT-Name `<topic>.<group>.dlt` (Spec Abschnitt 5), also `ticks.v1.history.dlt`. Header: `dlt-error`, `dlt-original-topic`, `dlt-original-partition`, `dlt-original-offset`, `dlt-attempts`.
- Retries im Prozess: **3 Versuche** mit 200 ms und 1 s Pause dazwischen. Nachrichten, die sich nicht parsen lassen (Poison), gehen **ohne** Retry sofort ins DLT.
- Störschalter nur am Simulator, nur per HTTP (`/chaos/...`), nicht über das BFF erreichbar.
- Die Split-Anpassung über Faktoren (Spec Abschnitt 6) gehört in **Runde 4**, nicht hierher.
- Für `lightweight-charts` gilt **5.2.1**. 5.3.0 ist erst 4 Tage alt.

## Review Focus

1. **Verspäteter Tick erreicht Quote:** Der Live-Ticker darf nicht auf einen alten Kurs zurückspringen. Quote ignoriert Ticks mit älterer `EventTime` als dem letzten bekannten Tick → Test `Older_event_time_does_not_replace_latest` (Task 2).
2. **Doppelter Tick erreicht History:** Volumen und Close bleiben unverändert → Test `Duplicate_seq_is_ignored` (Task 4).
3. **Poison Message auf `ticks.v1`:** History blockiert nicht. Die Nachricht landet sofort im DLT, die nachfolgenden Ticks werden verarbeitet → Test `Poison_goes_to_dlt_and_next_message_is_processed` (Task 6).
4. **Abfrage mit unbekanntem Intervall oder ohne Daten:** `400` bzw. eine leere Liste, kein 500 → Tests `Unknown_interval_is_rejected` und `Empty_history_returns_empty_list` (Task 5).
5. **Chart bei Instrument ohne Historie oder bei History-Ausfall:** Die PDP zeigt den Ticker weiter und statt des Charts einen Hinweis → Browser-Prüfung (Task 8, Step 3).

---

## File Structure

```
src/Messaging/
  Contracts/Tick.cs                 (+ TryParse streng)
  DeadLetter.cs                     Retry mit Backoff + DLT-Publish (Landkarte 14)
src/Quote/QuoteHub.cs               (ältere EventTime ignorieren)
src/Simulator/
  ChaosSettings.cs                  Störschalter-Zustand
  TickPlan.cs                       reine Funktion: welche Ticks pro Takt (normal/Duplikat/verspätet)
  ChaosEndpoints.cs                 POST /chaos/...
  SimulatorWorker.cs, Program.cs    (Rate zur Laufzeit, Pause, TickPlan)
src/History/Portal.History/
  Program.cs, HistorySchema.cs
  CandleBucket.cs                   Minute einer EventTime (UTC, abgeschnitten)
  CandleStore.cs                    ApplyAsync: Dedupe + Upsert in einer Transaktion (Landkarte 12, 13)
  CandleQuery.cs                    1m/1h/1d-Rollup
  TickConsumer.cs                   Group history, manueller Commit, Retry/DLT, Neuaufbau
src/Frontend/app/components/CandleChart.vue
src/Frontend/app/pages/instrumente/[id].vue (Chart einbinden)
tests/Portal.Tests/
  Messaging/DeadLetterTests.cs, Messaging/TickTests.cs
  Quote/QuoteHubTests.cs            (+1)
  Simulator/TickPlanTests.cs
  History/CandleStoreTests.cs, History/CandleQueryTests.cs, History/TickConsumerTests.cs
```

---

### Task 1: Strenges Tick-Parsing und Retry/DLT in Messaging

**Files:**
- Modify: `src/Messaging/Contracts/Tick.cs`
- Create: `src/Messaging/DeadLetter.cs`
- Test: `tests/Portal.Tests/Messaging/TickTests.cs`, `tests/Portal.Tests/Messaging/DeadLetterTests.cs`

**Interfaces:**
- Produces:
  - `public static Tick? Tick.TryParse(string? value)` liefert `null` bei Tombstone, ungültigem JSON oder fehlenden Feldern. Dafür gilt dieselbe strenge Option wie bei `InstrumentSnapshot` (`RespectNullableAnnotations`, `RespectRequiredConstructorParameters`).
  - `public sealed class DeadLetter(IProducer<string, string> producer, string group, ILogger log)` mit:
    - `public static string TopicFor(string topic, string group) => $"{topic}.{group}.dlt"`
    - `public async Task HandleAsync(ConsumeResult<string, string> result, Func<Task> process, CancellationToken ct)`: Führt `process` mit bis zu 3 Versuchen aus (Pausen 200 ms, 1 s). Scheitert der letzte Versuch, wird die Original-Nachricht mit Key, Value und Original-Headern plus den DLT-Headern ins DLT geschrieben. Wirft nur bei Abbruch (`OperationCanceledException`).
    - `public Task SendAsync(ConsumeResult<string, string> result, Exception error, int attempts, CancellationToken ct)`: direkt ins DLT (für Poison).
  - Der Aufrufer legt das DLT-Topic beim Start mit `Topics.EnsureTopicAsync(…, DeadLetter.TopicFor(topic, group))` an.

- [ ] **Step 1: Failing Tests**

- `TickTests`:
  - `Valid_tick_is_parsed`: JSON aus `JsonSerializer.Serialize(tick, Web)`.
  - `Invalid_ticks_are_rejected`: als Theory für `null`, `"kein json"`, `"{}"` und `{"instrumentId":"<guid>","seq":1}` (ohne price/eventTime) → `null`.
- `DeadLetterTests` (`[Collection("infra")]`, Topic `t-{guid}`, DLT per `TopicFor`):
  - `Succeeds_after_transient_failures_without_dlt`: `process` wirft zweimal und gelingt beim dritten Versuch. Erwartet: 3 Aufrufe, DLT leer (3 s konsumieren, 0 Nachrichten).
  - `Gives_up_after_three_attempts_and_writes_dlt_with_headers`: `process` wirft immer `InvalidOperationException("kaputt")`. Erwartet: 3 Aufrufe und genau 1 DLT-Nachricht mit gleichem Key und Value, `dlt-error` enthält `"kaputt"`, `dlt-original-topic == topic`, `dlt-original-offset == result.Offset.Value.ToString()`, `dlt-attempts == "3"`, außerdem einen Original-Header `traceparent`.
  - `Send_writes_poison_without_retry`: `SendAsync(result, new JsonException(), 1, ct)` → 1 DLT-Nachricht mit `dlt-attempts == "1"`.

  `ConsumeResult` wird im Test von Hand gebaut: `new ConsumeResult<string,string> { Topic = topic, Partition = 0, Offset = 42, Message = new Message<string,string>{ Key = "k", Value = "v", Headers = new Headers { { "traceparent", "00-…"u8.ToArray() } } } }`.

- [ ] **Step 2: Tests laufen lassen, sie müssen fehlschlagen**

Run: `dotnet test tests/Portal.Tests --filter "TickTests|DeadLetterTests"`
Expected: Build-Fehler.

- [ ] **Step 3: Implementieren**

`DeadLetter`: Die Pausen stehen als `static readonly TimeSpan[] Backoff = [200ms, 1s]` in der Klasse. Der Kommentar beschreibt die Regel: Retry für vorübergehende Fehler, DLT für alles, was nach 3 Versuchen noch scheitert, und Poison ohne Retry, weil sich am Inhalt nichts ändert. Dazu `// ponytail: feste Werte; konfigurierbar, falls verschiedene Consumer verschiedene Geduld brauchen`.

- [ ] **Step 4: Tests laufen lassen, sie müssen grün sein**

Run: `dotnet test tests/Portal.Tests --filter "TickTests|DeadLetterTests"`
Expected: alle bestanden.

- [ ] **Step 5: Quote auf `Tick.TryParse` umstellen und committen**

In `src/Quote/TickConsumer.cs` ersetzt `Tick.TryParse(result.Message.Value)` die Deserialisierung und das `JsonException`-Catch. Das Verhalten bleibt gleich (überspringen und loggen), die bestehenden `TickConsumerTests` bleiben grün.

Run: `dotnet test tests/Portal.Tests`
Expected: alle bestanden.

```bash
git add -A && git commit -m "feat(messaging): strict tick parsing, retry with backoff and dead-letter topic"
```

---

### Task 2: Quote ignoriert verspätete Ticks

**Files:**
- Modify: `src/Quote/QuoteHub.cs`
- Test: `tests/Portal.Tests/Quote/QuoteHubTests.cs`

**Interfaces:**
- Produces: `QuoteHub.Publish(Tick)` verwirft Ticks, deren `EventTime` älter ist als die des aktuell letzten Ticks für dieses Instrument. Sie werden weder `Latest` noch an Abonnenten verteilt.

- [ ] **Step 1: Failing Test**

`Older_event_time_does_not_replace_latest`: `Publish(seq 1, t0)`, danach `Publish(seq 2, t0 - 2 min)`. Erwartet: `Latest(A).Seq == 1`. Ein Abonnent, der vor dem verspäteten Tick abonniert hat, bekommt danach als nächstes den Tick `seq 3, t0 + 1 s` und nicht `seq 2`.

- [ ] **Step 2: Test laufen lassen, er muss fehlschlagen**

Run: `dotnet test tests/Portal.Tests --filter QuoteHubTests`
Expected: 1 Fehler.

- [ ] **Step 3: Implementieren**

`_latest.AddOrUpdate(..., (_, current) => tick.EventTime >= current.EventTime ? tick : current)`. Danach nur verteilen, wenn der Tick übernommen wurde (Rückgabewert mit `ReferenceEquals` vergleichen). Kommentar: Für die Live-Anzeige zählt der neueste Stand, verspätete Ticks sind Sache von History.

- [ ] **Step 4: Tests laufen lassen, sie müssen grün sein, dann committen**

Run: `dotnet test tests/Portal.Tests --filter QuoteHubTests`
Expected: alle bestanden.

```bash
git add -A && git commit -m "fix(quote): ignore late ticks for the live price"
```

---

### Task 3: Störschalter im Simulator

**Files:**
- Create: `src/Simulator/ChaosSettings.cs`, `src/Simulator/TickPlan.cs`, `src/Simulator/ChaosEndpoints.cs`
- Modify: `src/Simulator/SimulatorWorker.cs`, `src/Simulator/Program.cs`
- Test: `tests/Portal.Tests/Simulator/TickPlanTests.cs`

**Interfaces:**
- Produces:
  - `public sealed class ChaosSettings { public volatile bool Duplicates; public volatile bool Late; public volatile bool Paused; public double TicksPerSecondPerInstrument { get; set; } }`. Startwert der Rate kommt aus `Simulator:TicksPerSecondPerInstrument` (2).
  - `public static class TickPlan { public static IReadOnlyList<Tick> For(Guid instrumentId, decimal price, long volume, DateTimeOffset now, ref long seq, ChaosSettings chaos); }`. Normal kommt 1 Tick mit `++seq`. Bei `Duplicates` wird derselbe Tick zweimal geliefert. Bei `Late` kommt zusätzlich **jeder 10.** Takt (`seq % 10 == 0` nach dem Hochzählen) ein Tick mit **neuer** `seq` und `EventTime = now - 2 min`.
  - HTTP am Simulator (nur `MapChaosEndpoints`, kein BFF):
    - `POST /chaos/duplicates?on=true|false`
    - `POST /chaos/late?on=…`
    - `POST /chaos/pause?on=…`
    - `POST /chaos/rate?ticksPerSecond=<0.1..1000>`, sonst 400
    - `POST /chaos/poison`: schreibt **einmal** `"kein json"` mit dem Key des ersten aktiven Instruments nach `ticks.v1`
    - `GET /chaos` liefert den Zustand.

  **Hinweis zu `seq`:** Bisher teilen sich alle Instrumente einen Zähler pro Takt. Der Worker zählt jetzt **pro Instrument** (`Dictionary<Guid,long>`, Start `time.GetUtcNow().ToUnixTimeMilliseconds()`), weil `TickPlan` bei `Late` zusätzliche Nummern verbraucht.

- [ ] **Step 1: Failing Tests**

`TickPlanTests`:
- `Normal_tick_increments_seq`: 1 Tick, `seq` vorher 100 → Tick `Seq == 101`, `seq == 101`.
- `Duplicates_repeat_the_same_tick`: 2 identische Ticks (`Assert.Equal(t[0], t[1])`), `seq` ist um 1 gestiegen.
- `Late_adds_old_event_time_tick_every_tenth`: Mit `seq = 99` ergibt `For` 2 Ticks. Der zweite hat `Seq == 101` und `EventTime == now - 2 min`. Mit `seq = 100` gibt es nur 1 Tick.

Run: `dotnet test tests/Portal.Tests --filter TickPlanTests`
Expected: Build-Fehler.

- [ ] **Step 2: Implementieren**

`TickPlan` wie oben beschrieben. `SimulatorWorker`:
- Bei `Paused` wird der Takt übersprungen.
- Weicht `chaos.TicksPerSecondPerInstrument` von der aktuellen Rate ab, wird `timer.Period = TimeSpan.FromSeconds(1 / rate)` gesetzt.
- Jeder Tick aus `TickPlan.For` wird über `TickPublisher` gesendet.

In `Program.cs` werden `ChaosSettings` als Singleton und `app.MapChaosEndpoints()` registriert. Kommentar an den Endpunkten: `// Nur lokal: Störungen gezielt auslösen. Nicht über das BFF erreichbar.`

- [ ] **Step 3: Tests laufen lassen, sie müssen grün sein, dann committen**

Run: `dotnet test tests/Portal.Tests`
Expected: alle bestanden.

```bash
git add -A && git commit -m "feat(simulator): chaos switches for duplicates, late ticks, rate, poison and pause"
```

---

### Task 4: History-Kern (Dedupe und Event-Time-Kerzen)

**Files:**
- Create: `src/History/Portal.History/` (`dotnet new web`) mit `HistorySchema.cs`, `CandleBucket.cs`, `CandleStore.cs`
- Test: `tests/Portal.Tests/History/CandleStoreTests.cs`

**Interfaces:**
- Produces:
  - DDL (`HistorySchema.EnsureAsync(NpgsqlDataSource, CancellationToken)`):
    ```sql
    CREATE TABLE IF NOT EXISTS candles_1m (
      instrument_id uuid NOT NULL, bucket timestamptz NOT NULL,
      open numeric(18,4) NOT NULL, high numeric(18,4) NOT NULL, low numeric(18,4) NOT NULL, close numeric(18,4) NOT NULL,
      volume bigint NOT NULL, open_time timestamptz NOT NULL, close_time timestamptz NOT NULL,
      PRIMARY KEY (instrument_id, bucket));
    CREATE TABLE IF NOT EXISTS instrument_seq (instrument_id uuid PRIMARY KEY, last_seq bigint NOT NULL);
    ```
  - `public static class CandleBucket { public static DateTimeOffset Of(DateTimeOffset eventTime); }`: UTC, auf die Minute abgeschnitten.
  - `public sealed class CandleStore(NpgsqlDataSource db) { Task<bool> ApplyAsync(Tick tick, CancellationToken ct); Task<bool> IsEmptyAsync(CancellationToken ct); Task ClearAsync(CancellationToken ct); }`. `ApplyAsync` liefert `false` bei einem Duplikat bzw. einer veralteten `seq`. `ClearAsync` leert beide Tabellen.

- [ ] **Step 1: Projekt anlegen**

```bash
dotnet new web -n Portal.History -o src/History/Portal.History -f net10.0
dotnet sln add src/History/Portal.History
dotnet add src/History/Portal.History reference src/Messaging src/ServiceDefaults
dotnet add src/History/Portal.History package Dapper --version 2.1.89
dotnet add src/History/Portal.History package Npgsql --version 10.0.3
dotnet add tests/Portal.Tests reference src/History/Portal.History
```

- [ ] **Step 2: Failing Tests** (`[Collection("infra")]`, eigene Datenbank je Test)

Zeiten: `t0 = 2026-11-14T10:00:30Z`. Kerzen werden in den Tests per SQL gelesen.
- `CandleBucket_truncates_to_minute_in_utc`: `2026-11-14T11:00:59.9+01:00` → `2026-11-14T10:00:00Z`.
- `First_tick_creates_candle`: `seq 1, 100.00, vol 5, t0` → eine Kerze mit Bucket `10:00`, `open = high = low = close = 100`, `volume 5`.
- `Ticks_in_same_minute_update_high_low_close_volume`: Ticks `100 @t0`, `105 @t0+10s`, `98 @t0+20s` ergeben `open 100, high 105, low 98, close 98, volume` = Summe.
- `Duplicate_seq_is_ignored`: Derselbe Tick zweimal ergibt beim zweiten Mal `ApplyAsync == false`, und `volume` ist nur einmal gezählt.
- `Older_seq_is_ignored`: `seq 5`, danach `seq 4` → `false`.
- `Late_tick_updates_its_own_minute`: `seq 1 @10:05:10` mit 110, danach `seq 2 @10:00:10` mit 90. Erwartet: zwei Kerzen. Die von 10:00 hat `close 90`, die von 10:05 bleibt bei `close 110`.
- `Earlier_event_time_with_higher_seq_becomes_open`: `seq 1 @t0+20s` mit 100, danach `seq 2 @t0+5s` mit 95 (verspätet in derselben Minute). Erwartet: `open 95, close 100`.
- `Clear_empties_both_tables`, außerdem `IsEmptyAsync` vorher `false` und nachher `true`.

Run: `dotnet test tests/Portal.Tests --filter CandleStoreTests`
Expected: Build-Fehler.

- [ ] **Step 3: Implementieren**

`ApplyAsync` in einer Transaktion:

```sql
-- 1. Dedupe: nur wenn seq neuer ist (oder Instrument neu)
INSERT INTO instrument_seq (instrument_id, last_seq) VALUES (@InstrumentId, @Seq)
ON CONFLICT (instrument_id) DO UPDATE SET last_seq = excluded.last_seq
WHERE instrument_seq.last_seq < excluded.last_seq;
-- 0 Zeilen → Duplikat/veraltet → Rollback, false
-- 2. Kerze der Event-Time-Minute
INSERT INTO candles_1m (instrument_id, bucket, open, high, low, close, volume, open_time, close_time)
VALUES (@InstrumentId, @Bucket, @Price, @Price, @Price, @Price, @Volume, @EventTime, @EventTime)
ON CONFLICT (instrument_id, bucket) DO UPDATE SET
  open  = CASE WHEN excluded.open_time  < candles_1m.open_time  THEN excluded.open  ELSE candles_1m.open  END,
  open_time  = LEAST(candles_1m.open_time, excluded.open_time),
  close = CASE WHEN excluded.close_time >= candles_1m.close_time THEN excluded.close ELSE candles_1m.close END,
  close_time = GREATEST(candles_1m.close_time, excluded.close_time),
  high = GREATEST(candles_1m.high, excluded.high), low = LEAST(candles_1m.low, excluded.low),
  volume = candles_1m.volume + excluded.volume;
```

Kommentare: `seq` ist die Transport-Reihenfolge (Dedupe), `EventTime` die fachliche Zeit (Kerze). Ein verspäteter Tick hat eine neue `seq`, aber eine alte `EventTime`. Er wird also nicht verworfen, sondern landet in seiner alten Minute.

- [ ] **Step 4: Tests laufen lassen, sie müssen grün sein, dann Landkarte und Commit**

Run: `dotnet test tests/Portal.Tests --filter CandleStoreTests`
Expected: 8 bestanden.

Landkarte Zeile 12: `src/History/Portal.History/CandleStore.cs`, Test `Duplicate_seq_is_ignored`, Status `drin`. Zeile 13: dieselbe Datei, Tests `Late_tick_updates_its_own_minute` und `Earlier_event_time_with_higher_seq_becomes_open`, Status `drin`.

```bash
git add -A && git commit -m "feat(history): candle store with seq dedupe and event-time buckets"
```

---

### Task 5: History-Abfrage (1m/1h/1d)

**Files:**
- Create: `src/History/Portal.History/CandleQuery.cs`
- Test: `tests/Portal.Tests/History/CandleQueryTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record CandleDto(long Time, decimal Open, decimal High, decimal Low, decimal Close, long Volume);`, wobei `Time` = Unix-Sekunden des Bucket-Starts (passt direkt zu lightweight-charts).
  - `public sealed class CandleQuery(NpgsqlDataSource db, TimeProvider time) { Task<IReadOnlyList<CandleDto>> Handle(Guid instrumentId, string interval, CancellationToken ct); static bool IsValidInterval(string interval); }`
  - Intervalle und Zeitfenster bis jetzt: `1m` = 2 h, `1h` = 7 Tage, `1d` = 365 Tage. Höchstens 500 Kerzen, aufsteigend nach Zeit.

- [ ] **Step 1: Failing Tests** (`[Collection("infra")]`, Kerzen über `CandleStore.ApplyAsync` anlegen. Die Zeit kommt aus einer Testklasse `FixedTime(DateTimeOffset now) : TimeProvider` mit `GetUtcNow() => now`, also kein zusätzliches Paket.)

- `One_minute_returns_candles_ascending`: Ticks in drei Minuten ergeben 3 Kerzen mit aufsteigendem `Time`.
- `One_hour_rolls_up_minutes`: Kerzen 10:00 (`open 100`, `high 101`), 10:30 (`low 90`) und 10:59 (`close 95`). `1h` ergibt eine Kerze mit `open 100, high 101, low 90, close 95` und der Volumensumme.
- `Window_excludes_old_candles`: Bei `now` = 13:00 ist eine 1m-Kerze von 10:00 nicht dabei (2-h-Fenster).
- `Empty_history_returns_empty_list`.
- `Unknown_interval_is_rejected`: `IsValidInterval("5m") == false` und `IsValidInterval("1h") == true`. Der Endpunkt liefert dafür 400 (Task 7).

Run: `dotnet test tests/Portal.Tests --filter CandleQueryTests`
Expected: Build-Fehler.

- [ ] **Step 2: Implementieren**

Ein SQL mit `date_trunc(@unit, bucket)`, Unit `'minute'|'hour'|'day'` aus einer festen Zuordnung, **nie** aus Nutzereingabe zusammengesetzt:

```sql
SELECT extract(epoch FROM date_trunc(@unit, bucket))::bigint AS time,
       (array_agg(open ORDER BY bucket))[1] AS open, max(high) AS high, min(low) AS low,
       (array_agg(close ORDER BY bucket DESC))[1] AS close, sum(volume)::bigint AS volume
FROM candles_1m WHERE instrument_id = @id AND bucket >= @from
GROUP BY 1 ORDER BY 1 LIMIT 500
```

Kommentar: 1h und 1d werden nicht gespeichert, sondern beim Lesen aus den Minuten gebildet. Eine Wahrheit, keine Synchronisation.

- [ ] **Step 3: Tests laufen lassen, sie müssen grün sein, dann committen**

Run: `dotnet test tests/Portal.Tests --filter CandleQueryTests`
Expected: 5 bestanden.

```bash
git add -A && git commit -m "feat(history): candle query with hourly and daily rollups"
```

---

### Task 6: History-Consumer (Arbeitsteilung, Commit, Retry/DLT, Neuaufbau)

**Files:**
- Create: `src/History/Portal.History/TickConsumer.cs`
- Test: `tests/Portal.Tests/History/TickConsumerTests.cs`

**Interfaces:**
- Consumes: `Tick.TryParse`, `DeadLetter` (Task 1), `CandleStore` (Task 4).
- Produces: `public sealed class TickConsumer(CandleStore store, DeadLetter deadLetter, ConsumerConfig config, ILogger<TickConsumer> log, string topic = Topics.Ticks) : BackgroundService`. Die Klasse heißt im Namespace `Portal.History`. Den Konflikt mit `Portal.Quote.TickConsumer` lösen die Tests per Alias-`using`.

  Ablauf pro Nachricht:
  1. `Tick.TryParse`. Liefert es `null`, folgt `deadLetter.SendAsync(result, …, 1)` und dann der Commit.
  2. Sonst `deadLetter.HandleAsync(result, () => store.ApplyAsync(tick, ct))` und danach der Commit.

  Bei leerer Kerzen-Tabelle beim Start: `ClearAsync` und `Offset.Beginning` bei der ersten Zuweisung, wie `SnapshotConsumer` in Search. Kommentar *Arbeitsteilung* und `// ponytail: Neuaufbau nur mit einer Instanz sicher`. `ConsumeException` (nicht fatal) → loggen und weiter.

- [ ] **Step 1: Failing Tests** (`[Collection("infra")]`, eigenes Topic mit 6 Partitionen, eigene Datenbank, Group `history-test-{guid}`)

- `Ticks_become_candles_and_offsets_are_committed`: 3 Ticks eines Instruments innerhalb einer Minute. Erwartet: Innerhalb von 30 s gibt es eine Kerze mit der Volumensumme, und der Committed-Offset der Partition ist 3.
- `Poison_goes_to_dlt_and_next_message_is_processed`: `"kein json"` und danach ein gültiger Tick, **gleicher Key**. Erwartet: Die Kerze entsteht, im DLT steht 1 Nachricht `"kein json"` mit `dlt-attempts == "1"`, und der Task ist nicht Faulted.
- `Database_failure_goes_to_dlt_after_retries`: Consumer starten, `ALTER TABLE candles_1m RENAME TO candles_off`, dann 1 Tick. Erwartet: Innerhalb von 30 s liegt 1 DLT-Nachricht mit `dlt-attempts == "3"` vor, der Offset ist committet, und der Consumer läuft weiter. Nach dem Zurückbenennen erzeugt ein weiterer Tick eine Kerze.
- `Two_consumers_in_one_group_share_partitions` (Landkarte 9, Arbeitsteilung): Zwei `TickConsumer` mit **derselben** Group starten und Ticks für 12 Instrumente produzieren, bis beide zugewiesen sind. `AdminClient.DescribeConsumerGroupsAsync` liefert dann 2 Mitglieder, deren Partitionen zusammen genau `{0..5}` ergeben, ohne Überschneidung.
- `Empty_store_rebuilds_from_beginning`: wie `Empty_projection_rebuilds_from_beginning` in Search, mit Kerzen.

Run: `dotnet test tests/Portal.Tests --filter "FullyQualifiedName~Portal.Tests.History.TickConsumerTests"`
Expected: Build-Fehler.

- [ ] **Step 2: Implementieren** (wie unter Interfaces beschrieben)

- [ ] **Step 3: Tests laufen lassen, sie müssen grün sein, dann Landkarte und Commit**

Run: `dotnet test tests/Portal.Tests --filter "FullyQualifiedName~Portal.Tests.History"`
Expected: alle bestanden.

Landkarte:
- Zeile 9: dazu `src/History/Portal.History/TickConsumer.cs` (Arbeitsteilung), Test `Two_consumers_in_one_group_share_partitions`, Status `drin`.
- Zeile 14: `src/Messaging/DeadLetter.cs` und `History/TickConsumer.cs`, Tests `DeadLetterTests` und `Poison_goes_to_dlt_and_next_message_is_processed`, Status `drin`.
- Zeile 16: dazu `History/TickConsumer.cs`, Test `Empty_store_rebuilds_from_beginning`, Status `drin`.

```bash
git add -A && git commit -m "feat(history): work-sharing tick consumer with retries, dead letters and rebuild"
```

---

### Task 7: History-API, AppHost, BFF

**Files:**
- Modify: `src/History/Portal.History/Program.cs`, `src/AppHost/AppHost.cs`, `src/Bff/appsettings.json`

**Interfaces:**
- Produces:
  - `GET /history/{instrumentId:guid}?interval=1m|1h|1d` (Standard `1m`) → `CandleDto[]`, bei unbekanntem Intervall `400`.
  - BFF-Route `history`: `/api/history/{**rest}`, nur `GET`, Cluster `history` (`http://history`), `ActivityTimeout` 5 s.
  - AppHost: `historydb`, Projekt `history` mit `WithReplicas(int.Parse(builder.Configuration["HistoryReplicas"] ?? "1"))`. Kommentar: Replikate per Umgebungsvariable `HistoryReplicas=2` für das Experiment Arbeitsteilung. Neuaufbau nur mit 1 Replikat.

- [ ] **Step 1: Program.cs**

`NpgsqlDataSource`, `HistorySchema.EnsureAsync` beim Start, `Topics.EnsureTopicAsync(kafka, DeadLetter.TopicFor(Topics.Ticks, "history"))` beim Start. `CandleStore`, `CandleQuery` und `TimeProvider.System` registrieren. Producer für `DeadLetter` mit `Acks.All`. `TickConsumer` mit `GroupId = "history"`, `EnableAutoCommit = false`, `AutoOffsetReset.Earliest`.

- [ ] **Step 2: AppHost, BFF und `run-apphost.sh` prüfen**

`.WithReference(historyDb).WithReference(kafka).WaitFor(historyDb).WaitFor(kafka)`. Das BFF bekommt `.WithReference(history)`. `.superpowers/run-apphost.sh` reicht Umgebungsvariablen bereits durch (`env … exec`), `HistoryReplicas=2 .superpowers/run-apphost.sh` funktioniert also.

- [ ] **Step 3: Live prüfen**

Run: AppHost starten, 60 s warten.
- `curl http://<bff>/api/history/<sap-id>?interval=1m` → JSON-Array mit mindestens 1 Kerze, `time` sind Minutenwerte.
- `?interval=5m` → `400`.
- `kafka-consumer-groups --describe --group history` → Lag ≈ 0.

AppHost beenden.

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "feat(history): HTTP API, AppHost wiring and BFF route"
```

---

### Task 8: Kerzen-Chart im Frontend und Abnahme der Runde

**Files:**
- Create: `src/Frontend/app/components/CandleChart.vue`
- Modify: `src/Frontend/app/pages/instrumente/[id].vue`, `src/Frontend/package.json`, `docs/lernziel-landkarte.md`

**Interfaces:**
- Consumes: `/api/history/{id}?interval=`, `useQuoteStream` (Runde 1).
- Produces: `<CandleChart :instrument-id :tick />`. `tick` ist der letzte Live-Tick und aktualisiert die laufende Kerze.

- [ ] **Step 1: Abhängigkeit**

```bash
cd src/Frontend && npm install lightweight-charts@5.2.1 --save-exact
```

- [ ] **Step 2: Komponente**

`CandleChart.vue` (in `[id].vue` mit `<ClientOnly>` umhüllt):
- Buttons `1 Min`, `1 Std`, `1 Tag`. Aktiv ist `aria-pressed`, Standard ist `1 Min`.
- Laden per `$fetch('/api/history/{id}', { query: { interval } })`, dann `createChart(el, { autoSize: true, height: 320 })`, `chart.addSeries(CandlestickSeries)`, `setData(...)`.
- Live-Update nur bei `1 Min`: Beim `watch` auf `tick` bildet `series.update({ time: floor(eventTime/60)*60, ... })` die laufende Kerze fort. Open bleibt der erste Wert der Minute, High/Low/Close werden nachgeführt.
- Container mit `role="img"` und `aria-label="Kursverlauf {interval}"`.
- Farben für Hell/Dunkel per `matchMedia('(prefers-color-scheme: dark)')`.
- Fehler oder leere Daten: „Noch keine Kursdaten.“ bzw. „Kursverlauf nicht verfügbar.“ statt des Charts.

`[id].vue`: Die PDP stellt den Tick aus `useQuoteStream` bereit, damit Ticker **und** Chart denselben Stream nutzen (kein zweiter EventSource). Dazu wandert `useQuoteStream` aus `QuoteTicker` hoch in die Seite, und `QuoteTicker` bekommt `tick`, `previous` und `connected` als Props.

- [ ] **Step 3: Ende-zu-Ende im Browser**

Run: AppHost starten.
Expected:
- Die SAP-PDP zeigt den Ticker und darunter Kerzen. Die letzte Kerze bewegt sich mit dem Ticker.
- Ein Wechsel auf `1 Std` zeigt eine Kerze pro Stunde.
- Ein **neu** angelegtes Instrument zeigt kurz „Noch keine Kursdaten.“, nach einer Minute erscheinen Kerzen.
- Nach `kill -9` auf History und Neuladen der PDP: Der Ticker läuft weiter, der Chart zeigt „Kursverlauf nicht verfügbar.“.

- [ ] **Step 4: Experimente (Abnahme)**

Alle über `curl -X POST http://<simulator>/chaos/...`:
- **12 Duplikate:** `duplicates?on=true` für eine volle Minute, dann `off`. Gegenprobe für ein Instrument und diese Minute:
  - Ticks per `kafka-console-consumer --from-beginning` lesen.
  - Nach `seq` deduplizieren und `volume` summieren.
  - Mit `SELECT volume FROM candles_1m WHERE …` vergleichen.

  Erwartet: Beide Werte sind gleich, und im Topic stehen für diese Minute etwa doppelt so viele Nachrichten.
- **13 Verspätete Ticks:** `late?on=true`. Erwartet: Im History-Log bzw. per SQL ändern sich Kerzen von vor 2 Minuten, der Live-Ticker springt **nicht** zurück.
- **14 Poison:** `poison`. Erwartet: Im DLT `ticks.v1.history.dlt` liegt 1 Nachricht mit Headern (`kafka-console-consumer --property print.headers=true`), History läuft weiter, und Quote loggt „Ungültiger Tick übersprungen“.
- **16 Neuaufbau History:** History stoppen, `TRUNCATE candles_1m`, starten. Erwartet: Die Kerzen der letzten Stunden sind wieder da.
- **23 Last und Lag:** `rate?ticksPerSecond=300` für 30 s. Erwartet: `kafka-consumer-groups --describe --group history` zeigt wachsenden `LAG`, nach `rate?ticksPerSecond=2` baut er sich ab.
- **9 Arbeitsteilung:** AppHost mit `HistoryReplicas=2` starten. Erwartet: `kafka-consumer-groups --describe --group history --members` zeigt 2 Mitglieder mit je 3 Partitionen. Im Vergleich hat Quote 2 Groups mit je 6 Partitionen.

Abweichungen werden in diesem Task behoben, bevor committet wird.

- [ ] **Step 5: Landkarte abschließen und committen**

Die Spalte Experiment der Zeilen 9, 12, 13, 14, 16 und 23 bekommt die Beobachtungen. Zeile 23: `Ort` = `src/Simulator/ChaosEndpoints.cs` + `History/TickConsumer.cs`, Test „–“, Status `drin`.

```bash
git add -A && git commit -m "feat(frontend): candle chart with live updates; round 3 learning map"
```

---

## Nicht in dieser Runde

Split-Anpassungsfaktoren in History, Kapitalmaßnahmen und Login kommen in Runde 4, Azure/Bicep in Runde 5. Die aufgeschobenen Kleinigkeiten aus Runde 2 (Neuaufbau bei Absturz mittendrin, werfendes `Seek`/`Commit`, Enum-Zahlen, Event-Version) bleiben offen, es sei denn, eine Task berührt dieselbe Stelle ohnehin.
