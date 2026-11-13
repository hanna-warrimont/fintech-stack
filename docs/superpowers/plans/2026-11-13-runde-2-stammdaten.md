# Runde 2 – Stammdaten: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Instrumente werden in einem Instrument-Service mit DDD-Domäne gepflegt und über eine Transactional Outbox als Snapshots nach `instruments.v1` veröffentlicht. Ein Search-Service projiziert sie in eine Postgres-Volltextsuche, und der Simulator holt seine Instrumente aus dem Topic statt aus der festen Liste.

**Architecture:** Der Instrument-Service hat vier Schichten wie im Kurs (Domain → Application → Infrastructure → Api). Beim Speichern werden Domain Events in Integration Events umgesetzt (kompletter Snapshot) und in derselben Transaktion in `outbox_messages` geschrieben. Ein Dispatcher (`FOR UPDATE SKIP LOCKED`, idempotenter Producer) sendet sie nach `instruments.v1` (kompaktiert, Key = InstrumentId). Der Search-Service konsumiert arbeitsteilend mit Group `search`, manuellem Offset-Commit und Inbox-Tabelle. Er baut sich automatisch von vorn neu auf, wenn seine Projektion leer ist.

**Tech Stack:** .NET 10, EF Core 10 + Npgsql, Dapper, Confluent.Kafka, Aspire 13.6.5 (Postgres), xUnit, Testcontainers (Kafka, PostgreSql), Nuxt 4.

**Spec:** `docs/superpowers/specs/2026-11-13-instrumenten-portal-design.md` (Abschnitte 5, 6 „Instrument“, 7, 8, 9 und 10, Runde 2)

## Global Constraints

- Alles aus Runde 1 gilt weiter: `net10.0`, Aspire 13.6.5, `Confluent.Kafka` direkt, `// ponytail:` für bewusste Vereinfachungen, ein Konzept an einer Stelle, deutsche UI-Texte.
- **Kein MediatR, kein AutoMapper.** Handler sind einfache Klassen, die per DI registriert werden. MediatR ist seit v13 kommerziell, und der direkte Aufruf ist im Lückentext leichter zu sehen.
- EF Core **10.x** (`Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3, `Microsoft.EntityFrameworkCore.Design` 10.0.15, lokales Tool `dotnet-ef` 10.0.15). EF 11 passt nicht zu `net10.0`.
- Lesende Abfragen laufen über Dapper (CQRS wie im Kurs). Geschrieben wird nur über das Aggregat und EF.
- Postgres-Zugriff über `NpgsqlDataSource.Create(connectionString)` und `UseNpgsql(dataSource)`. Keine Aspire-Client-Pakete für Npgsql. Tracing per `.AddSource("Npgsql")` in ServiceDefaults.
- Jeder Service hat seine eigene Datenbank: `instrumentdb` und `searchdb` auf einem Aspire-Postgres `postgres`.
- `instruments.v1`: Key = InstrumentId (`"D"`-Format), `cleanup.policy=compact`, 6 Partitionen. Wert = `Envelope<InstrumentSnapshot>` als JSON (`JsonSerializerOptions.Web`).
- Event-Umschlag laut Spec: `{ messageId, type, version, occurredAt, payload }`, Typ `"InstrumentSnapshot"`, Version `1`.
- Schreib-Endpunkte des Instrument-Service sind in Runde 2 **ohne Login** und werden nicht über das BFF erreichbar gemacht (`// ponytail:` bis Runde 4).
- **Abweichung von der Spec, Abschnitt 5:** Search ist **ein Projekt** statt drei Schichten. Ohne Domäne entstünden nur Schnittstellen mit genau einer Implementierung. Die Entscheidung kommt ins Ledger und wird der Nutzerin gemeldet.

## Review Focus

1. **Gleiche ISIN zweimal gleichzeitig angelegt:** Das Ergebnis ist ein sauberes 409, kein 500. Der Unique-Index fängt den Wettlauf ab, und der Handler übersetzt die Postgres-Verletzung `23505` → Test `Concurrent_duplicate_isin_returns_conflict` (Task 3).
2. **Snapshots kommen in falscher Reihenfolge an** (mehrere Dispatcher, Retry nach Teil-Batch): Ein älterer Snapshot überschreibt nie einen neueren → Tests `Older_revision_does_not_overwrite_newer` (Task 6) und `Older_revision_is_ignored` (Task 7).
3. **Kaputte oder fremde Nachricht auf `instruments.v1`** (kein JSON, Tombstone, anderer `type`): Search und Simulator überspringen sie und laufen weiter → Test `Consumer_skips_invalid_messages` (Task 6).
4. **Suchbegriff mit Sonderzeichen** (`'`, `&`, `:*`, `%`, nur Leerzeichen): kein 500 und keine Injection, sondern leere Treffer bzw. die Standardliste → Testfälle in `Search_handles_special_characters` (Task 6).
5. **Search-Datenbank kurz nicht erreichbar:** Der Offset wird nicht committet, die Nachricht wird erneut verarbeitet, nichts geht verloren → Test `Failed_apply_is_retried_not_skipped` (Task 6).

---

## File Structure

```
src/Messaging/
  Topics.cs                         (+ Instruments, EnsureTopicAsync mit config)
  KafkaTracing.cs                   (+ StartPublish mit explizitem Parent)
  Contracts/Envelope.cs             Umschlag für fachliche Events
  Contracts/InstrumentSnapshot.cs   Integration Event (State Transfer)
src/Instrument/
  Portal.Instrument.Domain/
    Abstractions/Result.cs, Error.cs, Entity.cs, IDomainEvent.cs
    Instruments/Instrument.cs, Isin.cs, Wkn.cs, Currency.cs, InstrumentType.cs,
                InstrumentStatus.cs, InstrumentErrors.cs, InstrumentEvents.cs
  Portal.Instrument.Application/
    Abstractions/IInstrumentRepository.cs, IUnitOfWork.cs
    Instruments/CreateInstrument.cs, RenameInstrument.cs, GetInstrument.cs
    DependencyInjection.cs
  Portal.Instrument.Infrastructure/
    InstrumentDbContext.cs          (IUnitOfWork, Domain Event → Outbox)
    Configurations/InstrumentConfiguration.cs, OutboxMessageConfiguration.cs
    InstrumentRepository.cs
    Outbox/OutboxMessage.cs, IntegrationEventMapper.cs, OutboxDispatcher.cs
    DesignTimeDbContextFactory.cs
    Migrations/…
    DependencyInjection.cs
  Portal.Instrument.Api/
    Program.cs, InstrumentEndpoints.cs, ResultExtensions.cs, SeedData.cs
src/Search/Portal.Search/
  Program.cs, SearchSchema.cs, InstrumentProjection.cs, SnapshotConsumer.cs, SearchQuery.cs
src/Simulator/
  InstrumentCatalog.cs              (neu, ersetzt SeedInstruments.cs)
  StartPrices.cs                    (neu)
src/Frontend/app/pages/index.vue    (Suche)
tests/Portal.Tests/
  PostgresFixture.cs, InfraCollection.cs (ersetzt KafkaCollection)
  Messaging/TopicsTests.cs
  Instrument/IsinTests.cs, CurrencyTests.cs, WknTests.cs, InstrumentTests.cs,
             InstrumentPersistenceTests.cs, OutboxDispatcherTests.cs, ArchitectureTests.cs
  Search/SearchTests.cs
  Simulator/InstrumentCatalogTests.cs
.config/dotnet-tools.json
```

---

### Task 1: Messaging-Erweiterungen und Postgres-Fixture

**Files:**
- Modify: `src/Messaging/Topics.cs`, `src/Messaging/KafkaTracing.cs`, `tests/Portal.Tests/KafkaFixture.cs`, `tests/Portal.Tests/Quote/TickConsumerTests.cs`, `tests/Portal.Tests/Simulator/TickPublisherTests.cs`, `src/ServiceDefaults/Extensions.cs`
- Create: `src/Messaging/Contracts/Envelope.cs`, `src/Messaging/Contracts/InstrumentSnapshot.cs`, `tests/Portal.Tests/PostgresFixture.cs`
- Test: `tests/Portal.Tests/Messaging/TopicsTests.cs`, `tests/Portal.Tests/Messaging/KafkaTracingTests.cs`

**Interfaces:**
- Produces:
  - `Topics.Instruments = "instruments.v1"`; `Topics.EnsureTopicAsync(string bootstrapServers, string topic, int partitions = 6, IReadOnlyDictionary<string, string>? config = null)`
  - `public static readonly IReadOnlyDictionary<string, string> Topics.Compacted = { ["cleanup.policy"] = "compact" }`
  - `public sealed record Envelope<T>(Guid MessageId, string Type, int Version, DateTimeOffset OccurredAt, T Payload);`
  - `public sealed record InstrumentSnapshot(Guid Id, string Isin, string? Wkn, string Name, string Type, string Currency, string Status, int Revision) { public const string MessageType = "InstrumentSnapshot"; public const int MessageVersion = 1; }`
  - `KafkaTracing.StartPublish(string topic, Headers headers, ActivityContext parent)` (Overload; die bestehende Signatur bleibt)
  - `PostgresFixture : IAsyncLifetime` mit `Task<string> CreateDatabaseAsync()`: legt pro Aufruf eine frische Datenbank `t_{guid:N}` an und gibt deren Connection-String zurück.
  - `[CollectionDefinition("infra")] InfraCollection : ICollectionFixture<KafkaFixture>, ICollectionFixture<PostgresFixture>`. Alle bisherigen `[Collection("kafka")]` werden zu `[Collection("infra")]`.

- [ ] **Step 1: Pakete und Fixture**

```bash
dotnet add tests/Portal.Tests package Testcontainers.PostgreSql --version 4.16.0
```

`PostgresFixture`: `new PostgreSqlBuilder("postgres:17-alpine").Build()`. `CreateDatabaseAsync` führt `CREATE DATABASE` über eine Admin-Verbindung aus und gibt den Connection-String mit `Database=t_…` zurück. `KafkaCollection` wird durch `InfraCollection` ersetzt, das Attribut in den zwei bestehenden Testklassen wird umbenannt. In ServiceDefaults bekommt das Tracing zusätzlich `.AddSource("Npgsql")`.

- [ ] **Step 2: Failing Tests**

- `TopicsTests.EnsureTopic_applies_config` (`[Collection("infra")]`): Topic `t-{guid}` mit `Topics.Compacted` anlegen, dann per `AdminClient.DescribeConfigsAsync` lesen. Erwartet: `cleanup.policy == "compact"` und 6 Partitionen (`GetMetadata`).
- `TopicsTests.EnsureTopic_is_idempotent`: zweimal aufrufen, keine Exception.
- `KafkaTracingTests.Publish_with_explicit_parent_continues_that_trace`: `var parent = new ActivityContext(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded);`. Danach muss gelten: `StartPublish(topic, headers, parent).TraceId == parent.TraceId` und `ParentSpanId == parent.SpanId`.

- [ ] **Step 3: Tests laufen lassen, sie müssen fehlschlagen**

Run: `dotnet test tests/Portal.Tests --filter "TopicsTests|KafkaTracingTests"`
Expected: Build-Fehler (`Compacted`, Overload fehlen).

- [ ] **Step 4: Implementieren**

`EnsureTopicAsync` reicht `config` an `TopicSpecification.Configs` durch. Die bestehende Publish-Methode delegiert an den neuen Overload mit `parent = default`. Bei `default` gilt das bisherige Verhalten: Parent ist `Activity.Current`.

- [ ] **Step 5: Tests laufen lassen, sie müssen grün sein**

Run: `dotnet test tests/Portal.Tests`
Expected: alle bestehenden 14 Tests plus 3 neue bestanden.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat(messaging): envelope, instrument snapshot contract, compacted topics; postgres fixture"
```

---

### Task 2: Instrument-Domäne (Value Objects, Aggregat, Result)

**Files:**
- Create: `src/Instrument/Portal.Instrument.Domain/**` (siehe File Structure)
- Test: `tests/Portal.Tests/Instrument/IsinTests.cs`, `CurrencyTests.cs`, `WknTests.cs`, `InstrumentTests.cs`

**Interfaces:**
- Produces (Namespace `Portal.Instrument.Domain.Abstractions` bzw. `.Instruments`):
  - `public enum ErrorType { Validation, NotFound, Conflict }`
  - `public sealed record Error(string Code, string Message, ErrorType Type) { public static readonly Error None; }`
  - `public class Result { bool IsSuccess; bool IsFailure; Error Error; static Result Success(); static Result Failure(Error); static Result<T> Success<T>(T); static Result<T> Failure<T>(Error); }`, dazu `public class Result<T> : Result { T Value; }`. `Value` wirft bei einem Fehler-Ergebnis eine `InvalidOperationException`.
  - `public interface IDomainEvent;` und `public abstract class Entity { IReadOnlyList<IDomainEvent> DomainEvents; protected void Raise(IDomainEvent e); void ClearDomainEvents(); }`
  - `public sealed record Isin { string Value; static Result<Isin> Create(string? value); }`, ebenso `Wkn` und `Currency`
  - `public enum InstrumentType { Share, Bond, Fund, Etf }`, `public enum InstrumentStatus { Active, Delisted }`
  - `public sealed class Instrument : Entity { Guid Id; Isin Isin; Wkn? Wkn; string Name; InstrumentType Type; Currency Currency; InstrumentStatus Status; int Revision; static Result<Instrument> Create(Isin isin, Wkn? wkn, string name, InstrumentType type, Currency currency); Result Rename(string name); }`
  - `public sealed record InstrumentCreated(Guid InstrumentId) : IDomainEvent;` und `public sealed record InstrumentRenamed(Guid InstrumentId, string Name) : IDomainEvent;`
  - `InstrumentErrors`: `InvalidIsin`, `InvalidWkn`, `InvalidCurrency`, `NameRequired`, `NameTooLong` (Validation), `NotFound` (NotFound), `DuplicateIsin` (Conflict). Codes nach dem Muster `"Instrument.InvalidIsin"`, Meldungen auf Deutsch.

- [ ] **Step 1: Projekt anlegen**

```bash
dotnet new classlib -n Portal.Instrument.Domain -o src/Instrument/Portal.Instrument.Domain -f net10.0
dotnet sln add src/Instrument/Portal.Instrument.Domain
dotnet add tests/Portal.Tests reference src/Instrument/Portal.Instrument.Domain
```

- [ ] **Step 2: Failing Tests**

- `IsinTests`:
  - `Valid_isins_are_accepted`: als Theory für `DE0007164600`, `US0378331005`, `IE00B4L5Y983`.
  - `Input_is_trimmed_and_uppercased`: `" de0007164600 "` → `Value == "DE0007164600"`.
  - `Wrong_check_digit_is_rejected`: `DE0007164601` → `Error == InstrumentErrors.InvalidIsin`.
  - `Wrong_length_or_characters_are_rejected`: als Theory für `DE000716460`, `DE00071646000`, `DE0007164-00`, `""` und `null`.
- `CurrencyTests`: `EUR` ist ok. `" usd "` → `USD`. `XYZ`, `EU`, `""` und `null` → `InvalidCurrency`.
- `WknTests`: `716460` und `A0RPWH` sind ok, `" a0rpwh "` → `A0RPWH`. `71646`, `716460X` und `71646!` → `InvalidWkn`.
- `InstrumentTests`:
  - `Create_starts_active_with_revision_1_and_raises_created`: `Status == Active`, `Revision == 1`, `Id != Guid.Empty`, `DomainEvents` ist genau `[new InstrumentCreated(Id)]`.
  - `Create_trims_name`: `"  SAP SE "` → `"SAP SE"`.
  - `Create_rejects_blank_or_too_long_name`: `"  "` → `NameRequired`, 201 Zeichen → `NameTooLong`.
  - `Rename_changes_name_increments_revision_and_raises_renamed`: `Revision == 2`, letztes Event ist `InstrumentRenamed(Id, "Neu")`.
  - `Rename_to_same_name_changes_nothing`: `Revision` bleibt 1, kein neues Event, Ergebnis ist Success.

- [ ] **Step 3: Tests laufen lassen, sie müssen fehlschlagen**

Run: `dotnet test tests/Portal.Tests --filter "FullyQualifiedName~Portal.Tests.Instrument"`
Expected: Build-Fehler, die Typen fehlen.

- [ ] **Step 4: Implementieren**

- `Isin.Create`: trimmen, in Großbuchstaben, dann Regex `^[A-Z]{2}[A-Z0-9]{9}[0-9]$`, danach die Prüfziffer nach ISO 6166 (Luhn über die Ziffernfolge, Buchstaben zählen als A=10 bis Z=35):

  ```csharp
  var digits = string.Concat(value[..11].Select(c => char.IsDigit(c) ? $"{c - '0'}" : $"{c - 'A' + 10}"));
  var sum = 0;
  for (int i = digits.Length - 1, doubleIt = 1; i >= 0; i--, doubleIt ^= 1)
  {
      var d = (digits[i] - '0') * (doubleIt == 1 ? 2 : 1);
      sum += d > 9 ? d - 9 : d;
  }
  return (10 - sum % 10) % 10 == value[11] - '0';
  ```
- `Currency.Create`: trimmen, in Großbuchstaben. Gültig ist, was in der Menge aller `new RegionInfo(c.Name).ISOCurrencySymbol` über `CultureInfo.GetCultures(CultureTypes.SpecificCultures)` vorkommt (Stdlib statt eigener ISO-4217-Liste, einmal statisch berechnet).
- `Wkn.Create`: trimmen, in Großbuchstaben, Regex `^[A-Z0-9]{6}$`.
- Der Name ist getrimmt und 1 bis 200 Zeichen lang. `Rename` validiert genauso.

- [ ] **Step 5: Tests laufen lassen, sie müssen grün sein**

Run: `dotnet test tests/Portal.Tests --filter "FullyQualifiedName~Portal.Tests.Instrument"`
Expected: alle bestanden.

- [ ] **Step 6: Landkarte und Commit**

Zeile 1: `src/Instrument/Portal.Instrument.Domain/Instruments/Isin.cs` (dazu `Currency.cs`), Tests `IsinTests`/`CurrencyTests`, Status `drin`. Zeile 3: `Domain/Abstractions/Result.cs` und `InstrumentErrors.cs`, Test `InstrumentTests`, Status `drin`.

```bash
git add -A && git commit -m "feat(instrument): domain model with ISIN/WKN/currency value objects and result pattern"
```

---

### Task 3: Application und Infrastructure (EF, Domain Event → Outbox)

**Files:**
- Create: `src/Instrument/Portal.Instrument.Application/**`, `src/Instrument/Portal.Instrument.Infrastructure/**` (ohne `OutboxDispatcher.cs`), `.config/dotnet-tools.json`
- Test: `tests/Portal.Tests/Instrument/InstrumentPersistenceTests.cs`

**Interfaces:**
- Consumes: Domäne (Task 2), `Envelope<T>`, `InstrumentSnapshot`, `Topics.Instruments` (Task 1).
- Produces:
  - `public interface IInstrumentRepository { Task<Instrument?> GetAsync(Guid id, CancellationToken ct); Task<bool> IsinExistsAsync(Isin isin, CancellationToken ct); void Add(Instrument instrument); }`
  - `public interface IUnitOfWork { Task SaveChangesAsync(CancellationToken ct); }`. Bei einer Unique-Verletzung wirft es die Exception `DuplicateKeyException : Exception` (definiert in Application/Abstractions), damit Application nichts über Postgres wissen muss.
  - `public sealed record CreateInstrumentCommand(string Isin, string? Wkn, string Name, InstrumentType Type, string Currency);` und `public sealed class CreateInstrumentHandler { Task<Result<Guid>> Handle(CreateInstrumentCommand cmd, CancellationToken ct); }`
  - `public sealed record RenameInstrumentCommand(Guid Id, string Name);` und `RenameInstrumentHandler.Handle(...) : Task<Result>`
  - `public sealed record InstrumentResponse(Guid Id, string Isin, string? Wkn, string Name, string Type, string Currency, string Status);` und `public sealed class GetInstrumentQuery(NpgsqlDataSource db) { Task<InstrumentResponse?> Handle(Guid id, CancellationToken ct); }` (Dapper)
  - `Application.DependencyInjection.AddInstrumentApplication(this IServiceCollection)`, `Infrastructure.DependencyInjection.AddInstrumentInfrastructure(this IServiceCollection, string connectionString)`. Letztere registriert `NpgsqlDataSource`, den DbContext, das Repository und `IUnitOfWork`.
  - `InstrumentDbContext` (öffentlich, Tests nutzen `Database.MigrateAsync()`)
  - Tabelle `outbox_messages(id uuid pk, topic text, key text, type text, payload jsonb, occurred_at timestamptz, trace_parent text null, processed_at timestamptz null)`, dazu ein partieller Index auf `occurred_at WHERE processed_at IS NULL`.
  - Tabelle `instruments`: `isin` als Spalte mit **Unique-Index**, `revision int`, Enums als Text.

- [ ] **Step 1: Projekte und Tooling**

```bash
dotnet new classlib -n Portal.Instrument.Application -o src/Instrument/Portal.Instrument.Application -f net10.0
dotnet new classlib -n Portal.Instrument.Infrastructure -o src/Instrument/Portal.Instrument.Infrastructure -f net10.0
dotnet sln add src/Instrument/Portal.Instrument.Application src/Instrument/Portal.Instrument.Infrastructure
dotnet add src/Instrument/Portal.Instrument.Application reference src/Instrument/Portal.Instrument.Domain
dotnet add src/Instrument/Portal.Instrument.Application package Dapper --version 2.1.89
dotnet add src/Instrument/Portal.Instrument.Application package Npgsql
dotnet add src/Instrument/Portal.Instrument.Infrastructure reference src/Instrument/Portal.Instrument.Application src/Messaging
dotnet add src/Instrument/Portal.Instrument.Infrastructure package Npgsql.EntityFrameworkCore.PostgreSQL --version 10.0.3
dotnet add src/Instrument/Portal.Instrument.Infrastructure package Microsoft.EntityFrameworkCore.Design --version 10.0.15
dotnet add tests/Portal.Tests reference src/Instrument/Portal.Instrument.Infrastructure
dotnet new tool-manifest
dotnet tool install dotnet-ef --version 10.0.15
```

Die `Npgsql`-Version in Application ist die, die Npgsql.EFCore 10.0.3 mitbringt.

- [ ] **Step 2: Failing Tests** (`[Collection("infra")]`, jeder Test mit eigener Datenbank aus `PostgresFixture.CreateDatabaseAsync()` und `MigrateAsync()`)

- `Create_persists_and_writes_snapshot_to_outbox_in_same_transaction`: SAP anlegen (`DE0007164600`, `716460`, `SAP SE`, Share, `EUR`). Erwartet: `instruments` hat 1 Zeile und `outbox_messages` hat 1 Zeile mit `topic == "instruments.v1"`, `key == id.ToString("D")`, `type == "InstrumentSnapshot"` und `processed_at IS NULL`. Das deserialisierte `payload` als `Envelope<InstrumentSnapshot>` muss `Version == 1`, `Payload.Isin == "DE0007164600"`, `Payload.Type == "Share"`, `Payload.Status == "Active"` und `Payload.Revision == 1` haben.
- `Rename_writes_second_snapshot_with_higher_revision`: Danach gibt es 2 Outbox-Zeilen, die neuere hat `Revision == 2` und `Name == "SAP"`.
- `Duplicate_isin_returns_conflict`: zweites Anlegen mit derselben ISIN → `Error == InstrumentErrors.DuplicateIsin`, Outbox hat weiterhin 1 Zeile.
- `Concurrent_duplicate_isin_returns_conflict`: zwei Handler mit **eigenen** DbContexts, beide laufen per `Task.WhenAll` mit derselben ISIN. Genau einer ist Success, der andere hat `DuplicateIsin`, `instruments` hat 1 Zeile, Outbox hat 1 Zeile.
- `Failed_save_writes_no_outbox_row`: direkt über den DbContext ein zweites Instrument mit bestehender ISIN `Add`en. Erwartet: `SaveChangesAsync` wirft `DuplicateKeyException`, und die Outbox enthält nur die Zeile des ersten Instruments (Atomarität).
- `Outbox_captures_trace_parent`: Mit Listener und gestarteter Activity anlegen. Erwartet: `trace_parent == activity.Id`.
- `Get_returns_response_or_null`: Nach dem Anlegen liefert `GetInstrumentQuery` `Name == "SAP SE"` und `Wkn == "716460"`. Eine unbekannte ID liefert `null`.

- [ ] **Step 3: Tests laufen lassen, sie müssen fehlschlagen**

Run: `dotnet test tests/Portal.Tests --filter InstrumentPersistenceTests`
Expected: Build-Fehler.

- [ ] **Step 4: Implementieren**

- `InstrumentDbContext.SaveChangesAsync`: Für jede getrackte `Instrument`-Entity mit `DomainEvents.Count > 0` erzeugt `IntegrationEventMapper.ToOutbox(Instrument, DateTimeOffset now, string? traceParent)` **eine** Snapshot-Zeile. Danach `ClearDomainEvents()` und `base.SaveChangesAsync`. Ein `DbUpdateException` mit `PostgresException { SqlState: "23505" }` wird in `DuplicateKeyException` übersetzt. `traceParent = Activity.Current?.Id`. Kommentar: Feingranulare Domain Events (`InstrumentCreated`/`Renamed`) werden zu **einem** groben Integration Event (Snapshot, Event-carried State Transfer). Mehrere Änderungen in einer Transaktion ergeben einen Snapshot.
- Der `CreateInstrumentHandler` validiert die Value Objects, prüft `IsinExistsAsync`, fügt hinzu und speichert. `DuplicateKeyException` wird zu `DuplicateIsin`: Das deckt den Wettlauf ab, den die Vorabprüfung nicht sieht.
- EF-Konfiguration: Value Objects über `HasConversion` (string ↔ VO via `Create(...).Value`), Enums mit `HasConversion<string>()`, `Name` mit `HasMaxLength(200)`, `DomainEvents` mit `Ignore`. Tabellennamen in snake_case per explizitem `ToTable`/`HasColumnName`.
- `DesignTimeDbContextFactory` mit Dummy-Connection-String `Host=localhost;Database=design`.
- Migration erzeugen:

```bash
dotnet ef migrations add Initial -p src/Instrument/Portal.Instrument.Infrastructure -s src/Instrument/Portal.Instrument.Infrastructure -o Migrations
```

- [ ] **Step 5: Tests laufen lassen, sie müssen grün sein**

Run: `dotnet test tests/Portal.Tests --filter InstrumentPersistenceTests`
Expected: 7 bestanden.

- [ ] **Step 6: Landkarte und Commit**

Zeile 4: `src/Instrument/Portal.Instrument.Infrastructure/Outbox/IntegrationEventMapper.cs` (Aufruf in `InstrumentDbContext.SaveChangesAsync`), Test `Create_persists_and_writes_snapshot_to_outbox_in_same_transaction`, Status `drin`. Zeile 6 auf `teilweise` setzen (Schreibseite).

```bash
git add -A && git commit -m "feat(instrument): application handlers, EF persistence, domain events to outbox"
```

---

### Task 4: Outbox-Dispatcher (SKIP LOCKED, idempotenter Producer)

**Files:**
- Create: `src/Instrument/Portal.Instrument.Infrastructure/Outbox/OutboxDispatcher.cs`
- Modify: `src/Instrument/Portal.Instrument.Infrastructure/DependencyInjection.cs`
- Test: `tests/Portal.Tests/Instrument/OutboxDispatcherTests.cs`

**Interfaces:**
- Consumes: `outbox_messages` (Task 3), `KafkaTracing.StartPublish(topic, headers, parent)` (Task 1).
- Produces: `public sealed class OutboxDispatcher(NpgsqlDataSource db, IProducer<string, string> producer, TimeProvider time, ILogger<OutboxDispatcher> log) : BackgroundService { public Task<int> DispatchBatchAsync(CancellationToken ct); }`, dazu `public static ProducerConfig OutboxDispatcher.ProducerConfig(string bootstrapServers)`.

- [ ] **Step 1: Failing Tests** (`[Collection("infra")]`. Der Dispatcher sendet an `outbox_messages.topic`. Die Tests biegen ihre Zeilen deshalb nach dem Anlegen per `UPDATE outbox_messages SET topic = 'instruments-test-{guid}'` auf ein eigenes Topic um.)

- `Dispatches_pending_messages_and_marks_them_processed`: 3 Instrumente anlegen, `DispatchBatchAsync` liefert `3`. Im Topic stehen 3 Nachrichten, `Key == instrumentId`, der Wert ist als `Envelope<InstrumentSnapshot>` deserialisierbar, und jede hat einen `traceparent`-Header. Alle Zeilen haben `processed_at` gesetzt. Ein zweiter Aufruf liefert `0`, und das Topic hat weiterhin 3 Nachrichten.
- `Two_dispatchers_never_send_a_message_twice`: 200 Instrumente anlegen. Zwei Dispatcher laufen parallel, jeder ruft `DispatchBatchAsync` auf, bis beide `0` liefern. Erwartet: Das Topic enthält genau 200 Nachrichten mit 200 verschiedenen `MessageId`s.
- `Unreachable_kafka_leaves_messages_pending`: Producer mit `BootstrapServers = "localhost:1"` und `MessageTimeoutMs = 1000`. `DispatchBatchAsync` wirft `ProduceException<string,string>` oder `KafkaException`, und alle Zeilen haben weiter `processed_at IS NULL`.
- `Publish_continues_trace_of_the_request`: Instrument innerhalb einer Activity anlegen und dispatchen. Erwartet: Der Header `traceparent` der Nachricht enthält die TraceId dieser Activity.

- [ ] **Step 2: Tests laufen lassen, sie müssen fehlschlagen**

Run: `dotnet test tests/Portal.Tests --filter OutboxDispatcherTests`
Expected: Build-Fehler, `OutboxDispatcher` fehlt.

- [ ] **Step 3: Implementieren**

`DispatchBatchAsync` läuft so ab:
1. Transaktion öffnen und `SELECT id, topic, key, payload, trace_parent FROM outbox_messages WHERE processed_at IS NULL ORDER BY occurred_at LIMIT 50 FOR UPDATE SKIP LOCKED` ausführen (Dapper).
2. Pro Zeile **nacheinander** `await producer.ProduceAsync(...)`. Der Parent kommt aus `ActivityContext.TryParse(trace_parent)`.
3. `UPDATE … SET processed_at = @now WHERE id = ANY(@ids)` ausführen, dann committen.
4. Wirft `ProduceAsync`, wird zurückgerollt und weitergeworfen.

Zwei Kommentare gehören dazu:
- *at-least-once:* Bereits gesendete Nachrichten eines abgebrochenen Batches gehen erneut raus. Das fangen die Consumer per Inbox ab.
- *ponytail:* Mit mehreren Dispatchern ist die Reihenfolge pro Key nicht garantiert. Darum tragen Snapshots eine `Revision`.

`ExecuteAsync`: In einer Schleife `DispatchBatchAsync` aufrufen. Bei `0` wird 500 ms gewartet. Bei einer Exception wird geloggt und 5 s gewartet.

`ProducerConfig(...)`: `Acks = Acks.All`, `EnableIdempotence = true`. Kommentar: Idempotenz verhindert Duplikate durch *Producer-interne* Retries, nicht durch einen Neustart des Dispatchers. Dafür gibt es die Inbox.

- [ ] **Step 4: Tests laufen lassen, sie müssen grün sein**

Run: `dotnet test tests/Portal.Tests --filter OutboxDispatcherTests`
Expected: 4 bestanden.

- [ ] **Step 5: Landkarte und Commit**

Zeile 6: `Outbox/OutboxDispatcher.cs` und `InstrumentDbContext.cs`, Tests `OutboxDispatcherTests` und `Failed_save_writes_no_outbox_row`, Status `drin`. Zeile 7: `OutboxDispatcher.ProducerConfig`, Test „–“ (Konfiguration), Status `drin`.

```bash
git add -A && git commit -m "feat(instrument): outbox dispatcher with SKIP LOCKED and idempotent producer"
```

---

### Task 5: Instrument-API, Seed, AppHost, BFF-Route, Architektur-Tests

**Files:**
- Create: `src/Instrument/Portal.Instrument.Api/**` (`dotnet new web`), `tests/Portal.Tests/Instrument/ArchitectureTests.cs`
- Modify: `src/AppHost/AppHost.cs`, `src/Bff/appsettings.json`

**Interfaces:**
- Consumes: Handler und Query (Task 3), `OutboxDispatcher` (Task 4), `Topics.EnsureTopicAsync(…, Topics.Compacted)`.
- Produces (HTTP am Instrument-Service):
  - `POST /instruments` `{ isin, wkn?, name, type, currency }` → `201` mit `Location: /instruments/{id}` und Body `{ id }`. `400` als `ProblemDetails` mit `extensions.code`. `409` bei doppelter ISIN.
  - `PUT /instruments/{id}/name` `{ name }` → `204` / `400` / `404`
  - `GET /instruments/{id}` → `200 InstrumentResponse` / `404`
  - JSON-Enums als Strings (`JsonStringEnumConverter`).
- BFF: Route `instruments` (`/api/instruments/{**rest}`) zeigt jetzt auf den Cluster `instrument` (`http://instrument`).

- [ ] **Step 1: Failing Architektur-Tests**

`ArchitectureTests` arbeitet ohne zusätzliche Dependency über `Assembly.GetReferencedAssemblies()`. Typ-Anker sind `typeof(Instrument)`, `typeof(CreateInstrumentHandler)` und `typeof(InstrumentDbContext)`, nicht `Program`, das in mehreren referenzierten Web-Projekten vorkommt:
- `Domain_depends_on_nothing_outside_the_bcl`: Die Domain-Assembly referenziert keine Assembly, deren Name mit `Portal.` beginnt, keine `Microsoft.EntityFrameworkCore*`, `Npgsql*`, `Confluent*` und `Dapper`.
- `Application_does_not_depend_on_infrastructure_or_api`: Application referenziert weder `Portal.Instrument.Infrastructure` noch `Portal.Instrument.Api` noch `Microsoft.EntityFrameworkCore*`.
- `Infrastructure_does_not_depend_on_api`.

Run: `dotnet test tests/Portal.Tests --filter ArchitectureTests`
Expected: grün, denn die Schichten aus Task 2 und 3 halten die Regeln schon ein. Der RED-Nachweis läuft über eine Gegenprobe: Eine temporär in Domain eingefügte Paketreferenz plus Verwendung von `Microsoft.EntityFrameworkCore` muss `Domain_depends_on_nothing_outside_the_bcl` rot machen. Danach die Referenz wieder entfernen.

- [ ] **Step 2: Api-Projekt**

```bash
dotnet new web -n Portal.Instrument.Api -o src/Instrument/Portal.Instrument.Api -f net10.0
dotnet sln add src/Instrument/Portal.Instrument.Api
dotnet add src/Instrument/Portal.Instrument.Api reference src/Instrument/Portal.Instrument.Infrastructure src/ServiceDefaults
dotnet add src/AppHost reference src/Instrument/Portal.Instrument.Api
dotnet add src/AppHost package Aspire.Hosting.PostgreSQL --version 13.6.5
```

`Program.cs`: `AddServiceDefaults`, `AddInstrumentApplication`, `AddInstrumentInfrastructure(GetConnectionString("instrumentdb"))`, Producer (`OutboxDispatcher.ProducerConfig(GetConnectionString("kafka"))`) und `AddHostedService<OutboxDispatcher>`. Beim Start: `EnsureTopicAsync(kafka, Topics.Instruments, config: Topics.Compacted)`, `MigrateAsync()` und `SeedData.EnsureAsync`. Für Ergebnisse gibt es `ResultExtensions.ToProblem(this Error)`, das `ErrorType` auf 400/404/409 abbildet. Kommentar an den Schreib-Endpunkten: `// ponytail: ohne Login und nicht im BFF bis Runde 4`.

`SeedData.EnsureAsync`: Ist `instruments` leer, werden über den `CreateInstrumentHandler` (also über denselben Weg mit der Outbox) diese Instrumente angelegt:

| ISIN | WKN | Name | Typ | Währung |
|---|---|---|---|---|
| DE0007164600 | 716460 | SAP SE | Share | EUR |
| DE0007236101 | 723610 | Siemens AG | Share | EUR |
| DE0008404005 | 840400 | Allianz SE | Share | EUR |
| IE00B4L5Y983 | A0RPWH | iShares Core MSCI World UCITS ETF | Etf | EUR |

- [ ] **Step 3: AppHost und BFF**

```csharp
var postgres = builder.AddPostgres("postgres");
var instrumentDb = postgres.AddDatabase("instrumentdb");
var instrument = builder.AddProject<Projects.Portal_Instrument_Api>("instrument")
    .WithReference(instrumentDb).WithReference(kafka).WaitFor(instrumentDb).WaitFor(kafka);
```

Das BFF bekommt `.WithReference(instrument)`, `.WithReference(simulator)` entfällt. In `appsettings.json` heißt der Cluster `simulator` jetzt `instrument` mit `http://instrument`, und der `ponytail`-Kommentar aus Runde 1 wird entfernt.

- [ ] **Step 4: Tests und Live-Prüfung**

Run: `dotnet test tests/Portal.Tests`
Expected: alle bestanden.

Run: AppHost starten (`.superpowers/run-apphost.sh`, Ablauf wie in Runde 1).
- `curl -X POST http://<instrument>/instruments -H 'content-type: application/json' -d '{"isin":"DE0007100000","wkn":"710000","name":"Mercedes-Benz Group AG","type":"Share","currency":"EUR"}'` → `201`.
- Dasselbe noch einmal → `409`. Mit `"isin":"DE0007100001"` → `400` und `code` `Instrument.InvalidIsin`.
- `curl http://<bff>/api/instruments/<id>` → `200` mit Name.
- `kafka-get-offsets --topic instruments.v1` → 5 Nachrichten. `kafka-configs --describe --topic instruments.v1` → `cleanup.policy=compact`.

AppHost danach beenden und auf das Aufräumen warten.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat(instrument): HTTP API, seed via outbox, AppHost postgres, BFF route"
```

---

### Task 6: Search-Service (Projektion, Inbox, manueller Commit, Neuaufbau)

**Files:**
- Create: `src/Search/Portal.Search/` (`dotnet new web`) mit `Program.cs`, `SearchSchema.cs`, `InstrumentProjection.cs`, `SnapshotConsumer.cs`, `SearchQuery.cs`
- Modify: `src/AppHost/AppHost.cs`, `src/Bff/appsettings.json`
- Test: `tests/Portal.Tests/Search/SearchTests.cs`

**Interfaces:**
- Consumes: `Envelope<InstrumentSnapshot>`, `Topics.Instruments` (Task 1).
- Produces:
  - `public static class SearchSchema { public static Task EnsureAsync(NpgsqlDataSource db, CancellationToken ct); }`. Das DDL steht unten.
  - `public sealed class InstrumentProjection(NpgsqlDataSource db) { Task<bool> ApplyAsync(Envelope<InstrumentSnapshot> e, CancellationToken ct); Task<bool> IsEmptyAsync(CancellationToken ct); Task ClearAsync(CancellationToken ct); }`
  - `public sealed class SnapshotConsumer(InstrumentProjection projection, ConsumerConfig config, ILogger<SnapshotConsumer> log, string topic = Topics.Instruments) : BackgroundService`
  - `public sealed record SearchHit(Guid Id, string Isin, string? Wkn, string Name, string Type, string Currency, string Status);` und `public sealed class SearchQuery(NpgsqlDataSource db) { Task<IReadOnlyList<SearchHit>> Handle(string? q, CancellationToken ct); }`
  - HTTP: `GET /search?q=…` → `SearchHit[]` (höchstens 20). BFF-Route `search`: `/api/search/{**rest}` → Cluster `search` (`http://search`), Transform `PathRemovePrefix /api`.

DDL (`SearchSchema`):

```sql
CREATE TABLE IF NOT EXISTS instruments (
  id uuid PRIMARY KEY, isin text NOT NULL, wkn text, name text NOT NULL, type text NOT NULL,
  currency text NOT NULL, status text NOT NULL, revision int NOT NULL,
  search tsvector GENERATED ALWAYS AS (to_tsvector('simple', name || ' ' || isin || ' ' || coalesce(wkn, ''))) STORED);
CREATE INDEX IF NOT EXISTS ix_instruments_search ON instruments USING gin (search);
CREATE TABLE IF NOT EXISTS processed_messages (message_id uuid PRIMARY KEY, processed_at timestamptz NOT NULL);
```

- [ ] **Step 1: Projekt anlegen**

```bash
dotnet new web -n Portal.Search -o src/Search/Portal.Search -f net10.0
dotnet sln add src/Search/Portal.Search
dotnet add src/Search/Portal.Search reference src/Messaging src/ServiceDefaults
dotnet add src/Search/Portal.Search package Dapper --version 2.1.89
dotnet add src/Search/Portal.Search package Npgsql
dotnet add tests/Portal.Tests reference src/Search/Portal.Search
dotnet add src/AppHost reference src/Search/Portal.Search
```

- [ ] **Step 2: Failing Tests** (`[Collection("infra")]`, jeweils eigene Datenbank und eigenes Topic `instruments-test-{guid}`, angelegt mit `Topics.Compacted`)

Hilfsfunktion im Test: `Snap(Guid id, string name, int revision)` baut ein Envelope mit neuer `MessageId`.
- `Apply_is_idempotent_per_message`: Dasselbe Envelope zweimal anwenden ergibt `true`, dann `false`. `instruments` und `processed_messages` haben jeweils 1 Zeile.
- `Older_revision_does_not_overwrite_newer`: Revision 2 (`"Neu"`) anwenden, danach Revision 1 (`"Alt"`). Erwartet: Name `"Neu"`.
- `Search_finds_by_name_prefix_isin_and_wkn`: Die 4 Seed-Instrumente als Snapshots anwenden. Dann gilt:
  - `"sie"` → Siemens
  - `"DE0007164600"` → SAP
  - `"716460"` → SAP
  - `"msci world"` → ETF
  - `"SAP SE"` → SAP
  - `""`/`null` → alle 4, nach Name sortiert
- `Search_handles_special_characters`: `"'"`, `"&"`, `"sap:*"`, `"%"`, `"   "` und `"a'); DROP TABLE instruments; --"` werfen nicht. Erwartet: `"   "` liefert alle 4, `"sap:*"` liefert SAP, der Rest liefert leer, und die Tabelle existiert danach noch.
- `Consumer_redelivers_after_crash_before_commit_and_inbox_dedupes` (Landkarte 10/11):
  1. Snapshot produzieren.
  2. Ein roher Consumer mit Group `search-test` liest ihn, ruft `ApplyAsync` auf (`true`) und schließt **ohne Commit**. Das ist der „Absturz“.
  3. Der `SnapshotConsumer` startet mit derselben Group.

  Erwartet: Innerhalb von 30 s ist der Committed-Offset der Partition (`consumer.Committed` oder `AdminClient.ListConsumerGroupOffsets`) `> 0`, und `instruments` hat weiterhin 1 Zeile.
- `Empty_projection_rebuilds_from_beginning` (Landkarte 16): 2 Snapshots produzieren. Den `SnapshotConsumer` laufen lassen, bis beide da sind und committet ist, dann stoppen. `ClearAsync()` ausführen und denselben Consumer mit derselben Group neu starten. Erwartet: Innerhalb von 30 s sind wieder 2 Zeilen da.
- `Consumer_skips_invalid_messages`: `"kein json"`, ein Tombstone (`Value = null`) und ein Envelope mit `Type = "Other"` produzieren, danach einen gültigen Snapshot. Erwartet: Der gültige landet, und `ExecuteTask` ist nicht Faulted.
- `Failed_apply_is_retried_not_skipped`: Consumer starten, warten bis ein erster Snapshot angewendet ist. Dann `ALTER TABLE instruments RENAME TO instruments_off`, einen zweiten Snapshot produzieren und 3 s warten. Erwartet: Er ist nicht angewendet, der Committed-Offset steht noch hinter dem ersten Snapshot, und der Task ist nicht Faulted. Danach zurückbenennen. Erwartet: Innerhalb von 30 s ist der zweite Snapshot da.

- [ ] **Step 3: Tests laufen lassen, sie müssen fehlschlagen**

Run: `dotnet test tests/Portal.Tests --filter SearchTests`
Expected: Build-Fehler.

- [ ] **Step 4: Implementieren**

- `ApplyAsync` in **einer** Transaktion:
  1. `INSERT INTO processed_messages … ON CONFLICT DO NOTHING`. Betrifft das 0 Zeilen, wird zurückgerollt und `false` geliefert.
  2. `INSERT INTO instruments … ON CONFLICT (id) DO UPDATE SET … WHERE instruments.revision < excluded.revision`, dann commit und `true`.

  Kommentar: Die Inbox schützt vor Duplikaten (gleiche `MessageId`), die Revision vor falscher Reihenfolge. Beides ist nötig, keins ersetzt das andere.
- `SnapshotConsumer`:
  - Konfiguration: Group `search` (in `Program.cs`, Kommentar *Arbeitsteilung*), `EnableAutoCommit = false`, `AutoOffsetReset.Earliest`.
  - Beim Start ruft er `projection.IsEmptyAsync()` auf. Bei `true` folgen `ClearAsync()` (auch die Inbox) und ein `SetPartitionsAssignedHandler`, der bei der ersten Zuweisung `Offset.Beginning` für alle Partitionen liefert. Kommentar: Leere Projektion bedeutet Neuaufbau aus dem kompaktierten Topic. Dazu `// ponytail: Neuaufbau nur mit einer Search-Instanz sicher; sonst Offsets der Group zurücksetzen`.
  - Schleife: `ConsumeException` (nicht fatal) → loggen und weiter. Kein JSON, `null` oder fremder `type` → loggen, **committen** und weiter (`// ponytail: kein DLT bis Runde 3`).
  - `ApplyAsync` und danach `consumer.Commit(result)`. Wirft `ApplyAsync`: loggen, `consumer.Seek(result.TopicPartitionOffset)`, 1 s warten, nicht committen.
- `SearchQuery`: Die Eingabe wird per `Regex.Split(q, @"[^\p{L}\p{N}]+")` zerlegt, leere Teile fallen weg, es bleiben höchstens 5 Tokens in Kleinbuchstaben. Ohne Tokens: `ORDER BY name LIMIT 20`. Sonst wird der Parameter `@q = string.Join(" & ", tokens.Select(t => t + ":*"))` gebildet, die Abfrage lautet `WHERE search @@ to_tsquery('simple', @q) ORDER BY ts_rank(search, to_tsquery('simple', @q)) DESC, name LIMIT 20`. Der Kommentar dazu erklärt, warum durch die Bereinigung keine tsquery-Syntax vom Nutzer durchkommt.
- `Program.cs`: `NpgsqlDataSource.Create(GetConnectionString("searchdb"))`, `SearchSchema.EnsureAsync` beim Start, Endpunkt `/search`, Consumer als Hosted Service.

- [ ] **Step 5: Tests laufen lassen, sie müssen grün sein**

Run: `dotnet test tests/Portal.Tests --filter SearchTests`
Expected: 8 bestanden.

- [ ] **Step 6: AppHost und BFF**

```csharp
var searchDb = postgres.AddDatabase("searchdb");
var search = builder.AddProject<Projects.Portal_Search>("search")
    .WithReference(searchDb).WithReference(kafka).WaitFor(searchDb).WaitFor(kafka);
```

Das BFF bekommt `.WithReference(search)`. In `appsettings.json` kommen die Route und der Cluster `search` dazu.

- [ ] **Step 7: Landkarte und Commit**

- Zeile 10: `src/Search/Portal.Search/SnapshotConsumer.cs`, Test `Consumer_redelivers_after_crash_before_commit_and_inbox_dedupes`, Status `drin`.
- Zeile 11: `InstrumentProjection.cs`, Tests `Apply_is_idempotent_per_message` und `Older_revision_does_not_overwrite_newer`, Status `drin`.
- Zeile 15: `src/Messaging/Contracts/InstrumentSnapshot.cs` und `InstrumentProjection.cs`, Status `drin`.
- Zeile 16: `SnapshotConsumer.cs` (Search-Teil), Test `Empty_projection_rebuilds_from_beginning`, Status `teilweise` (History folgt in Runde 3).

```bash
git add -A && git commit -m "feat(search): instrument projection with inbox, manual commits, rebuild and full-text search"
```

---

### Task 7: Simulator liest Instrumente aus `instruments.v1`

**Files:**
- Create: `src/Simulator/InstrumentCatalog.cs`, `src/Simulator/StartPrices.cs`
- Delete: `src/Simulator/SeedInstruments.cs`
- Modify: `src/Simulator/SimulatorWorker.cs`, `src/Simulator/Program.cs`, `src/AppHost/AppHost.cs`
- Test: `tests/Portal.Tests/Simulator/InstrumentCatalogTests.cs`

**Interfaces:**
- Consumes: `Envelope<InstrumentSnapshot>`, `Topics.Instruments`.
- Produces:
  - `public sealed class InstrumentCatalog { void Apply(InstrumentSnapshot s); IReadOnlyCollection<InstrumentSnapshot> Active { get; } }`
  - `public sealed class InstrumentCatalogConsumer(InstrumentCatalog catalog, ConsumerConfig config, ILogger<InstrumentCatalogConsumer> log, string topic = Topics.Instruments) : BackgroundService`. Group `simulator-{Guid:N}` (Broadcast, liest immer von vorn), `AutoOffsetReset.Earliest`, kein Commit.
  - `public static class StartPrices { public static decimal For(string isin); }`: SAP 240, Siemens 190, Allianz 340, MSCI World 105, sonst 100.

- [ ] **Step 1: Failing Tests**

- `InstrumentCatalogTests`:
  - `Apply_adds_active_instrument`
  - `Newer_revision_replaces`: Name ändert sich.
  - `Older_revision_is_ignored`
  - `Delisted_is_not_active`: Ein Snapshot mit `Status = "Delisted"` und höherer Revision entfernt das Instrument aus `Active`.

Run: `dotnet test tests/Portal.Tests --filter InstrumentCatalogTests`
Expected: Build-Fehler.

- [ ] **Step 2: Implementieren**

- `InstrumentCatalog`: `ConcurrentDictionary<Guid, InstrumentSnapshot>`, Update nur bei höherer `Revision`. `Active` = Status `"Active"`.
- Der Consumer überspringt ungültige Nachrichten und `ConsumeException`s genauso wie Search, nur ohne Commit.
- `SimulatorWorker` iteriert pro Takt über `catalog.Active`. Der Preis pro Instrument wird beim ersten Auftreten mit `StartPrices.For(isin)` initialisiert. Die Endpunkte `/instruments` in `Program.cs` entfallen.
- Im AppHost bekommt der Simulator `.WaitFor(instrument)`, damit das Topic existiert. Das ist nur eine Optimierung, der Consumer übersteht ein fehlendes Topic.

- [ ] **Step 3: Tests laufen lassen, sie müssen grün sein**

Run: `dotnet test tests/Portal.Tests`
Expected: alle bestanden.

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "feat(simulator): take instruments from instruments.v1 instead of a fixed list"
```

---

### Task 8: Suche im Frontend und Abnahme der Runde

**Files:**
- Modify: `src/Frontend/app/pages/index.vue`, `docs/lernziel-landkarte.md`

**Interfaces:**
- Consumes: BFF `/api/search?q=` (Task 6), `/api/instruments/{id}` (Task 5).

- [ ] **Step 1: Suchseite**

`index.vue`:
- Überschrift „Instrumente“, darunter `<label for="q">Instrument suchen</label>` und `<input id="q" type="search" placeholder="Name, ISIN oder WKN">`.
- Der Suchbegriff ist ein `ref`, eine um 250 ms verzögerte Kopie davon (`watch` + `setTimeout`, keine Library) geht an `useFetch('/api/search', { query: { q: debounced } })`.
- Ergebnisliste wie bisher mit Link auf `/instrumente/{id}`, dazu ISIN, WKN und Typ. Bei leerer Trefferliste: „Keine Treffer“. Bei einem Fehler: „Suche nicht verfügbar.“

- [ ] **Step 2: Ende-zu-Ende im Browser**

Run: AppHost starten.
Expected:
- Die Startseite listet 4 Instrumente.
- „sie“ zeigt nur Siemens.
- Die PDP von SAP zeigt einen Live-Kurs, weil der Simulator die ID aus dem Topic kennt.
- Ein per `curl` neu angelegtes Instrument (Task 5, Step 4) ist innerhalb weniger Sekunden suchbar und bekommt Ticks.

- [ ] **Step 3: Experimente (Abnahme)**

- **6 Outbox:** Den Kafka-Container pausieren (`docker pause <kafka>`) und per `curl` ein Instrument anlegen. Erwartet: `201`, und `SELECT count(*) FROM outbox_messages WHERE processed_at IS NULL` ergibt 1. Nach `docker unpause` ist die Zeile verarbeitet, und das Instrument ist suchbar.
- **10 At-least-once:** `kill -9` auf Search, während ein Rename läuft, dann Search im Dashboard neu starten. Erwartet: Der Name ist korrekt, und es gibt keine doppelte Zeile.
- **11 Duplikate:** Search stoppen und `kafka-consumer-groups --group search --reset-offsets --to-earliest --topic instruments.v1 --execute` ausführen. Nach dem Neustart von Search erwartet: Im Log steht „bereits verarbeitet“ einmal je Nachricht, und die Daten bleiben unverändert.
- **16 Neuaufbau:** `TRUNCATE instruments` in `searchdb` und Search neu starten. Erwartet: Alle Instrumente sind wieder da.
- **15 Kompaktierung:** `kafka-configs --describe` zeigt `cleanup.policy=compact`.

Abweichungen werden in diesem Task behoben, bevor committet wird.

- [ ] **Step 4: Landkarte abschließen und committen**

Die Spalte Experiment der Zeilen 6, 10, 11, 15 und 16 bekommt die Beobachtungen aus Step 3.

```bash
git add -A && git commit -m "feat(frontend): instrument search page; round 2 learning map"
```

---

## Nicht in dieser Runde

Kapitalmaßnahmen, Vier-Augen-Prinzip, Optimistic Concurrency (die `Revision` ist vorbereitet) und Login kommen in Runde 4. Retry mit Backoff, DLT, Störschalter und History folgen in Runde 3. Einen Endpunkt für das Delisting gibt es nicht: Das geschieht in Runde 4 über eine Kapitalmaßnahme. Im Code steht `Status` schon, der Simulator beachtet ihn.
