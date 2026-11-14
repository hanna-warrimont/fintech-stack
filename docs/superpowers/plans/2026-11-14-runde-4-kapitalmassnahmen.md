# Runde 4 – Kapitalmaßnahmen, Vier-Augen, Login, Split-Folgen: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Pfleger erfassen Kapitalmaßnahmen (Split, ISIN-Wechsel, Delisting), ein anderer Freigeber gibt sie frei. Ein Job macht sie zum Stichtag wirksam. Die Folgen laufen über Kafka zu History (Chart wird rückwirkend angepasst) und zum Simulator (neues Kursniveau). Login läuft über Keycloak und eine Cookie-Session im BFF.

**Architecture:**
- **Instrument-Service:** bekommt ein zweites Aggregat `CorporateAction` mit Zustandsautomat. Jeder Übergang erhöht `Revision` (Optimistic Concurrency). Freigabe, Ablehnung, Storno und Wirksamwerden gehen als fachliche Events über die bestehende Outbox nach `corporate-actions.v1`.
- **Wirksamkeits-Job:** holt fällige Maßnahmen per `FOR UPDATE SKIP LOCKED`, wendet sie in einer Transaktion auf das Instrument an und schreibt die Events dabei in die Outbox.
- **BFF:** erledigt den OIDC-Login (Authorization Code) gegen Keycloak. Die Tokens liegen serverseitig in einem Session-Store, der Browser hat nur ein Cookie. YARP hängt das Access Token an Aufrufe des Instrument-Service. Der Instrument-Service prüft das JWT und die Rollen `pfleger` und `freigeber`.

**Tech Stack:**
- .NET 10, EF Core 10 und Dapper, Confluent.Kafka, Aspire 13.6.5
- `Aspire.Hosting.Keycloak` `13.6.5-preview.1.26559.7`
- `Microsoft.AspNetCore.Authentication.OpenIdConnect` und `…JwtBearer` jeweils 10.0.15
- Nuxt 4

**Spec:** `docs/superpowers/specs/2026-11-13-instrumenten-portal-design.md` (Abschnitte 5 „BFF“, „Kafka-Topics“; 6 „Kapitalmaßnahme“, „Verteilte Folgen eines wirksamen Splits“; 7 „Distributed Tracing“; 8 Zeilen 2, 5, 17, 18, 19, 21, 22; 10 Runde 4)

## Global Constraints

- **Alles aus Runde 1 bis 3 gilt weiter:**
  - `net10.0`, Aspire 13.6.5, `Confluent.Kafka` direkt, kein MediatR
  - Dapper für lesende Abfragen, Result-Pattern mit Fehlerkatalog
  - `// ponytail:` für bewusste Vereinfachungen, ein Konzept an einer Stelle
  - deutsche UI-Texte
- **Neue Pakete, nur diese:**
  - `Aspire.Hosting.Keycloak` 13.6.5-preview.1.26559.7 (nur AppHost; es gibt keine stabile Version, die Preview passt zu Aspire 13.6.5)
  - `Microsoft.AspNetCore.Authentication.OpenIdConnect` 10.0.15 (BFF)
  - `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.15 (Instrument.Api)
- **Zustände** (Spec Abschnitt 6):
  - `Draft` (Entwurf) –einreichen→ `Submitted` (Eingereicht) –freigeben→ `Approved` (Freigegeben) –Stichtag→ `Effective` (Wirksam)
  - `Submitted` –ablehnen→ `Rejected` (Abgelehnt)
  - `Submitted` –zurückziehen→ `Draft`
  - `Draft` –verwerfen→ `Discarded` (Verworfen)
  - `Approved` –stornieren, nur vor dem Stichtag→ `Cancelled` (Storniert)
- **Vier-Augen:** `ApprovedBy != CreatedBy`. Benutzer werden über `preferred_username` identifiziert.
- **Stichtag:** `EffectiveAt` (`DateTimeOffset`) muss bei der Freigabe mindestens `CorporateActions:LeadTime` in der Zukunft liegen. Standard ist `00:01:00`. Die Zeit kommt immer aus `TimeProvider`.
- **Höchstens eine offene Maßnahme gleicher Art pro Instrument und Stichtag.**
  - Offen heißt `Draft`, `Submitted` oder `Approved`.
  - Der Stichtag zählt als UTC-Kalendertag von `EffectiveAt`.
  - Die Regel setzt ein partieller Unique-Index in der Datenbank durch.
- **Split-Verhältnis `alt:neu`:** `1:4` heißt, aus 1 Aktie werden 4. Der Faktor für alte Kurse ist `alt / neu`, hier also 0,25. Ein Reverse-Split `4:1` hat den Faktor 4.
- **Topic `corporate-actions.v1`:**
  - Key `InstrumentId`, 6 Partitionen
  - `cleanup.policy=delete`, `retention.ms=-1`. Die Spec sagt „lang“; ohne Ende, weil History sonst Faktoren verliert.
  - Umschlag `Envelope<CorporateActionEvent>`, Typen `CorporateActionApproved`, `CorporateActionRejected`, `CorporateActionCancelled`, `CorporateActionBecameEffective`
- **Keycloak:**
  - Realm `portal`, fester Port `8080`. So bleibt der Token-Issuer über Neustarts gleich.
  - Rollen `pfleger` und `freigeber`
  - Testnutzer: `pia` (pfleger), `fred` (freigeber), `frieda` (pfleger und freigeber, für die Vier-Augen-Vorführung). Das Passwort ist jeweils der Benutzername. Es sind Testwerte, die nur im Realm-Import stehen.
- **Tokens nur serverseitig:**
  - Das BFF-Cookie enthält nur eine Session-ID, die Tokens liegen in einem `ITicketStore` im Speicher.
  - Schreibende `/api`-Aufrufe brauchen den Header `X-CSRF: 1`.
- **Öffentlich bleiben:**
  - Suche, PDP und Chart
  - `GET /instruments/{id}/corporate-actions`, nur `Approved`, `Effective` und `Cancelled`
  - Alles andere an Kapitalmaßnahmen verlangt eine Rolle.

## Review Focus

1. **Freigeber ist auch Erfasser** (`frieda` hat beide Rollen): Die Freigabe wird mit `CorporateAction.FourEyes` abgewiesen, der Zustand bleibt `Submitted`. Tests:
   - `Approve_by_creator_is_rejected` (Task 1)
   - `Approve_by_creator_via_handler_returns_four_eyes_error` (Task 3)
2. **Stichtag zu nah, in der Vergangenheit oder schon vorbei beim Storno:**
   - Freigabe → `LeadTimeTooShort`, Storno nach dem Stichtag → `AlreadyEffectiveDue`
   - Tests `Approve_with_effective_at_inside_lead_time_is_rejected`, `Cancel_after_effective_at_is_rejected` (Task 1)
3. **Zwei Job-Instanzen oder Neustart mitten im Lauf:**
   - Jede fällige Maßnahme wird genau einmal wirksam, mit genau einem `BecameEffective` in der Outbox.
   - Test `Two_jobs_in_parallel_make_each_action_effective_once` (Task 4)
4. **`BecameEffective` erreicht History zweimal:** genau ein Anpassungsfaktor. Test `Same_message_twice_stores_one_factor` (Task 6).
5. **Schreiben ohne Login, mit falscher Rolle oder ohne CSRF-Header:** Es wird nichts ausgeführt (401, 403 bzw. 400). Tests:
   - `Write_endpoints_require_expected_role` (Task 3)
   - `Unsafe_request_without_csrf_header_is_rejected` (Task 5)
   - live per `curl` (Task 9)

---

## File Structure

```
src/Messaging/
  Topics.cs                                   (+ CorporateActions, LongRetention)
  Contracts/CorporateActionEvent.cs           Vertrag corporate-actions.v1 + TryParse
src/Instruments/Portal.Instruments.Domain/
  Instruments/Instrument.cs                   (+ ChangeIsin, Delist)
  Instruments/InstrumentEvents.cs             (+ InstrumentIsinChanged, InstrumentDelisted)
  CorporateActions/CorporateAction.cs         Aggregat + Zustandsautomat (Landkarte 2)
  CorporateActions/SplitRatio.cs              Value Object
  CorporateActions/CorporateActionErrors.cs, CorporateActionEvents.cs, CorporateActionType.cs, CorporateActionStatus.cs
src/Instruments/Portal.Instruments.Application/
  Abstractions/ICorporateActionRepository.cs
  CorporateActions/CreateCorporateAction.cs   Command + Handler
  CorporateActions/ChangeCorporateAction.cs   Übergänge (submit/withdraw/discard/approve/reject/cancel)
  CorporateActions/ListCorporateActions.cs    Dapper-Abfragen (Arbeitsliste, öffentlich je Instrument)
src/Instruments/Portal.Instruments.Infrastructure/
  Configurations/CorporateActionConfiguration.cs, CorporateActionRepository.cs
  InstrumentDbContext.cs                      (Outbox für beide Aggregate)
  Outbox/IntegrationEventMapper.cs            (+ CorporateAction-Events)
  CorporateActions/EffectivenessJob.cs        FOR UPDATE SKIP LOCKED (Landkarte 17)
  Migrations/…_CorporateActions.cs
src/Instruments/Portal.Instruments.Api/
  CorporateActionEndpoints.cs, Program.cs (JwtBearer, Rollen-Policies), InstrumentEndpoints.cs (Schreiben nur pfleger)
src/AppHost/
  AppHost.cs (+ Keycloak), keycloak/portal-realm.json
src/Bff/
  Program.cs (OIDC, Cookie, Session-Store, Token-Weitergabe, CSRF, PDP-Aggregation)
  MemoryTicketStore.cs, Csrf.cs, PdpEndpoint.cs, appsettings.json
src/History/Portal.History/
  HistorySchema.cs (+ adjustments, processed_messages), AdjustmentStore.cs, CorporateActionConsumer.cs, CandleQuery.cs (Faktoren)
src/Simulator/
  PriceBook.cs (Kurse pro Instrument, Split anwenden), CorporateActionConsumer.cs, SimulatorWorker.cs
src/Frontend/
  server/api/[...path].ts (X-Forwarded-Host/Proto), app/composables/useUser.ts, app/app.vue (An-/Abmelden)
  app/pages/kapitalmassnahmen.vue, app/pages/instrumente/[id].vue (PDP-Aggregation, Maßnahmen, Chart neu laden)
tests/Portal.Tests/
  Instruments/CorporateActionTests.cs, CorporateActionPersistenceTests.cs, EffectivenessJobTests.cs, CorporateActionEndpointTests.cs
  Bff/CsrfTests.cs, History/AdjustmentTests.cs, Simulator/PriceBookTests.cs
```

---

### Task 1: Aggregat Kapitalmaßnahme (Domäne)

**Files:**
- Create: `src/Instruments/Portal.Instruments.Domain/CorporateActions/{CorporateAction,SplitRatio,CorporateActionErrors,CorporateActionEvents,CorporateActionType,CorporateActionStatus}.cs`
- Modify: `Instruments/Instrument.cs`, `Instruments/InstrumentEvents.cs`, `Instruments/InstrumentErrors.cs`
- Test: `tests/Portal.Tests/Instruments/CorporateActionTests.cs`, `InstrumentTests.cs` (+2)

**Interfaces:**
- Produces:
  - `enum CorporateActionType { Split, IsinChange, Delisting }`
  - `enum CorporateActionStatus { Draft, Submitted, Approved, Rejected, Cancelled, Discarded, Effective }`
  - `sealed record SplitRatio(int Old, int New)`:
    - `static Result<SplitRatio> Create(int old, int @new)`. Beide Werte müssen zwischen 1 und 1000 liegen und verschieden sein, sonst `CorporateActionErrors.InvalidSplitRatio`.
    - `decimal Factor => (decimal)Old / New`
  - `sealed class CorporateAction : Entity` mit Eigenschaften:
    - `Id`, `InstrumentId`, `Type`, `SplitRatio? Ratio`, `Isin? NewIsin`
    - `DateTimeOffset EffectiveAt`, `Status`, `string CreatedBy`, `string? DecidedBy`, `int Revision`
  - Fabriken: `static Result<CorporateAction> CreateSplit(Instrument instrument, SplitRatio ratio, DateTimeOffset effectiveAt, string user)`, ebenso `CreateIsinChange(Instrument, Isin newIsin, …)` und `CreateDelisting(Instrument, …)`. Fehler:
    - `InstrumentDelisted`, wenn das Instrument delistet ist
    - `SameIsin`, wenn die neue ISIN gleich der aktuellen ist
  - Übergänge, jeweils `Result`:
    - `Submit()`, `Withdraw()`, `Discard()`, `Reject(string user)`
    - `Approve(string user, DateTimeOffset now, TimeSpan leadTime)`
    - `Cancel(string user, DateTimeOffset now)`
    - `BecomeEffective(Instrument instrument, DateTimeOffset now)`
  - Jeder erlaubte Übergang erhöht `Revision` um 1. Ein falscher Ausgangszustand liefert `InvalidTransition`.
  - Events (`IDomainEvent`, jeweils mit `CorporateActionId` und `InstrumentId`):
    - `CorporateActionApproved`, `CorporateActionRejected`, `CorporateActionCancelled`, `CorporateActionBecameEffective`
    - Nur diese vier gehen später nach Kafka.
    - Für Einreichen, Zurückziehen und Verwerfen gibt es keine Events, nur die `Revision` steigt.
  - `Instrument.ChangeIsin(Isin)` und `Instrument.Delist()` liefern `Result`, erhöhen `Revision` und lösen `InstrumentIsinChanged` bzw. `InstrumentDelisted` aus. Ein zweites `Delist()` liefert den neuen Fehler `InstrumentErrors.AlreadyDelisted` (Conflict).
  - `CorporateActionErrors`, Codes mit Präfix `CorporateAction.`:
    - `NotFound` (NotFound)
    - `InvalidTransition`, `FourEyes`, `Duplicate`, `ConcurrentUpdate` (Conflict)
    - `LeadTimeTooShort`, `AlreadyEffectiveDue`, `InstrumentDelisted`, `SameIsin`, `InvalidSplitRatio` (Validation)
    - Texte auf Deutsch, `FourEyes` = „Wer eine Maßnahme erfasst hat, darf sie nicht freigeben.“

- [ ] **Step 1: Failing Tests** (`CorporateActionTests`, Zeit `now = 2026-11-14T10:00Z`, `leadTime = 1 min`, Ersteller `pia`, Freigeber `fred`)
  - `Split_ratio_must_be_positive_and_different` (Theory: `0:1`, `1:0`, `2:2`, `1:1001` → `InvalidSplitRatio`), außerdem `SplitRatio.Create(1,4).Value.Factor == 0.25m`
  - `New_action_is_draft_created_by_user` (`Status == Draft`, `CreatedBy == "pia"`, `Revision == 1`)
  - `Delisted_instrument_takes_no_new_action` → `InstrumentDelisted`
  - `Isin_change_to_same_isin_is_rejected` → `SameIsin`
  - `Happy_path_submit_approve` → `Approved`, `DecidedBy == "fred"`, `Revision == 3`, genau ein Event `CorporateActionApproved`
  - `Approve_by_creator_is_rejected` → `FourEyes`, Status bleibt `Submitted`
  - `Approve_with_effective_at_inside_lead_time_is_rejected` (`effectiveAt = now + 30 s`) → `LeadTimeTooShort`
  - `Approve_from_draft_is_invalid_transition`
  - `Withdraw_returns_to_draft`, `Discard_only_from_draft`, `Reject_from_submitted`
  - `Cancel_before_effective_at_is_allowed`, `Cancel_after_effective_at_is_rejected` → `AlreadyEffectiveDue`
  - `Become_effective_before_due_is_rejected` → `InvalidTransition`
  - `Become_effective_applies_isin_change_to_instrument`: `instrument.Isin` ist neu, und `Revision` des Instruments steigt.
  - `Become_effective_applies_delisting`
  - `Become_effective_split_leaves_instrument_unchanged`
  - In `InstrumentTests`: `Change_isin_raises_event_and_bumps_revision`, `Delist_twice_is_rejected` (`InstrumentErrors.AlreadyDelisted`)

Run: `dotnet test tests/Portal.Tests --filter "CorporateActionTests|InstrumentTests"`
Expected: Build-Fehler.

- [ ] **Step 2: Implementieren**

Ein `private Result Transition(CorporateActionStatus from, CorporateActionStatus to)` prüft den Ausgangszustand und erhöht `Revision`. Jede öffentliche Methode prüft zuerst ihre Zusatzregel (Vier-Augen, Vorlauf, Stichtag) und ruft dann `Transition` auf. Im Kommentar an der Klasse steht das Zustandsdiagramm aus der Spec.

- [ ] **Step 3: Tests grün, Landkarte, Commit**

Run: `dotnet test tests/Portal.Tests --filter "CorporateActionTests|InstrumentTests|ArchitectureTests"`
Expected: alle bestanden.

Landkarte Zeile 2: `src/Instruments/Portal.Instruments.Domain/CorporateActions/CorporateAction.cs`, Tests `CorporateActionTests`, Status `drin`.

```bash
git add -A && git commit -m "feat(instruments): corporate action aggregate with four-eyes rule"
```

---

### Task 2: Persistenz, Outbox und Topic

**Files:**
- Create:
  - `src/Messaging/Contracts/CorporateActionEvent.cs`
  - `src/Instruments/Portal.Instruments.Infrastructure/Configurations/CorporateActionConfiguration.cs`
  - `src/Instruments/Portal.Instruments.Infrastructure/CorporateActionRepository.cs`
  - `src/Instruments/Portal.Instruments.Application/Abstractions/ICorporateActionRepository.cs`
  - Migration `CorporateActions`
- Modify: `src/Messaging/Topics.cs`, `InstrumentDbContext.cs`, `Outbox/IntegrationEventMapper.cs`, `src/Instruments/Portal.Instruments.Api/Program.cs` (Topic anlegen)
- Test: `tests/Portal.Tests/Instruments/CorporateActionPersistenceTests.cs`

**Interfaces:**
- Consumes: Aggregat aus Task 1.
- Produces:
  - `Topics.CorporateActions = "corporate-actions.v1"` und `Topics.LongRetention` (`retention.ms = -1`)
  - `sealed record CorporateActionEvent(Guid CorporateActionId, Guid InstrumentId, string Kind, int? SplitOld, int? SplitNew, string? NewIsin, DateTimeOffset EffectiveAt)` mit `const int MessageVersion = 1` und `static Envelope<CorporateActionEvent>? TryParse(string? value)`
    - Streng über `StrictJson.Options`, wie bei `InstrumentSnapshot`.
    - Liefert `null` bei Tombstone, ungültigem JSON oder unbekanntem `Type`.
    - Gültige Typen: `CorporateActionApproved`, `CorporateActionRejected`, `CorporateActionCancelled`, `CorporateActionBecameEffective`.
    - `Kind` ist `Split`, `IsinChange` oder `Delisting`.
  - `ICorporateActionRepository { Task<CorporateAction?> GetAsync(Guid id, CancellationToken ct); void Add(CorporateAction action); }`
  - `IInstrumentRepository` braucht für Task 4 kein neues Mitglied. Der Job lädt Instrumente über `GetAsync`.
  - Tabelle `corporate_actions`:
    - Spalten `id`, `instrument_id` (FK auf `instruments`), `type`, `split_old`, `split_new`, `new_isin`, `effective_at`, `status`, `created_by`, `decided_by`, `revision`
    - `revision` ist Concurrency-Token, Enums werden als String gespeichert.
    - Partieller Unique-Index per `migrationBuilder.Sql` (EF kann keine Ausdrucks-Indizes):
      ```sql
      CREATE UNIQUE INDEX ux_corporate_actions_open_per_day ON corporate_actions
        (instrument_id, type, ((effective_at AT TIME ZONE 'UTC')::date))
        WHERE status IN ('Draft','Submitted','Approved');
      ```
  - Outbox in `SaveChangesAsync`:
    - Ausgewertet werden alle getrackten `Entity` mit Events, nicht nur `Instrument`.
    - `Instrument` → wie bisher ein Snapshot. Er entsteht jetzt auch bei `InstrumentIsinChanged` und `InstrumentDelisted`.
    - `CorporateAction` → **eine** Outbox-Zeile pro Event: Topic `corporate-actions.v1`, Key `InstrumentId`, Envelope-`Type` = Event-Name.
    - Kommentar: Snapshots sind Zustand (der letzte gewinnt), Kapitalmaßnahmen-Events sind Fakten (jedes zählt).

- [ ] **Step 1: Failing Tests** (`[Collection("infra")]`, eigene Datenbank, Aufbau wie `InstrumentPersistenceTests`)
  - `Approved_action_writes_event_to_outbox_in_same_transaction`: Topic, Key, `Type == "CorporateActionApproved"`. Die Payload lässt sich mit `CorporateActionEvent.TryParse` lesen und hat `Kind == "Split"`, `SplitOld == 1`, `SplitNew == 4`.
  - `Submit_writes_no_outbox_row`
  - `Second_open_action_same_type_same_day_is_duplicate`: Das zweite Speichern wirft `DuplicateKeyException`. Mit anderem Typ oder einem Tag später klappt es.
  - `Concurrent_approvals_one_wins_other_conflicts`: zwei DbContexts, zwei Freigeber (`fred`, `frieda`). Erwartet wird `ConcurrencyConflictException` beim zweiten Speichern.
  - `Event_contract_rejects_unknown_type` (Unit-Test, `CorporateActionEvent.TryParse` mit `Type = "Other"` → `null`)

Run: `dotnet test tests/Portal.Tests --filter "CorporateActionPersistenceTests"`
Expected: Build-Fehler.

- [ ] **Step 2: Implementieren, Migration erzeugen**

```bash
dotnet tool run dotnet-ef migrations add CorporateActions -p src/Instruments/Portal.Instruments.Infrastructure -s src/Instruments/Portal.Instruments.Infrastructure
```

Die Migration bekommt am Ende von `Up` das Index-SQL von oben und in `Down` ein `DROP INDEX`. `Program.cs` legt vor `MigrateAsync` an: `EnsureTopicAsync(kafka, Topics.CorporateActions, config: Topics.LongRetention)`.

- [ ] **Step 3: Tests grün, Commit**

Run: `dotnet test tests/Portal.Tests --filter "FullyQualifiedName~Portal.Tests.Instruments"`
Expected: alle bestanden.

```bash
git add -A && git commit -m "feat(instruments): persist corporate actions and publish their events via outbox"
```

---

### Task 3: Anwendungsfälle, Abfragen und geschützte API

**Files:**
- Create:
  - `src/Instruments/Portal.Instruments.Application/CorporateActions/{CreateCorporateAction,ChangeCorporateAction,ListCorporateActions}.cs`
  - `src/Instruments/Portal.Instruments.Api/CorporateActionEndpoints.cs`
- Modify: `Application/DependencyInjection.cs`, `Api/Program.cs`, `Api/InstrumentEndpoints.cs`, `Api/Portal.Instruments.Api.csproj` (JwtBearer), `src/Instruments/Portal.Instruments.Api/appsettings.json`
- Test: `tests/Portal.Tests/Instruments/CorporateActionPersistenceTests.cs` (+Handler-Tests), `tests/Portal.Tests/Instruments/CorporateActionEndpointTests.cs`

**Interfaces:**
- Consumes: Task 1 und 2.
- Produces:
  - `sealed record CreateCorporateActionCommand(Guid InstrumentId, CorporateActionType Type, int? SplitOld, int? SplitNew, string? NewIsin, DateTimeOffset EffectiveAt, string User)`, dazu `CreateCorporateActionHandler.Handle(…) → Task<Result<Guid>>`.
    - Fehlt das Instrument → `InstrumentErrors.NotFound`.
    - `DuplicateKeyException` → `CorporateActionErrors.Duplicate`.
  - `enum CorporateActionStep { Submit, Withdraw, Discard, Approve, Reject, Cancel }`
  - `sealed record ChangeCorporateActionCommand(Guid Id, CorporateActionStep Step, string User)`, dazu `ChangeCorporateActionHandler(ICorporateActionRepository, IUnitOfWork, TimeProvider, TimeSpan leadTime).Handle(…) → Task<Result>`.
    - `ConcurrencyConflictException` → `CorporateActionErrors.ConcurrentUpdate`.
    - Setzt `Activity.Current?.SetTag("corporateActionId", id)`.
  - `sealed record CorporateActionDto(Guid Id, Guid InstrumentId, string InstrumentName, string Isin, string Type, int? SplitOld, int? SplitNew, string? NewIsin, DateTimeOffset EffectiveAt, string Status, string CreatedBy, string? DecidedBy)`
  - `ListCorporateActionsQuery(NpgsqlDataSource)` mit zwei Methoden:
    - `Worklist(CancellationToken)`: alle Maßnahmen, neueste zuerst, höchstens 200
    - `PublicForInstrument(Guid instrumentId, CancellationToken)`: nur `Approved`, `Effective` und `Cancelled`, nach `EffectiveAt` absteigend
  - `AddInstrumentApplication(TimeSpan leadTime)`: Die Lead-Time kommt aus `CorporateActions:LeadTime` (Standard 1 Minute).
  - API (`CorporateActionEndpoints.MapCorporateActionEndpoints(IEndpointRouteBuilder)`), die Rolle hängt sichtbar an jedem Endpunkt:
    - `POST /corporate-actions` → Rolle `pfleger`, `201 {id}`
    - `POST /corporate-actions/{id}/submit|withdraw|discard` → `pfleger`, `204`
    - `POST /corporate-actions/{id}/approve|reject|cancel` → `freigeber`, `204`
    - `GET /corporate-actions` → `pfleger` oder `freigeber`
    - `GET /instruments/{id}/corporate-actions` → öffentlich
    - `User` = Claim `preferred_username`.
  - `POST /instruments` und `PUT /instruments/{id}/name` verlangen jetzt `pfleger`. Damit endet die Runde-2-Ausnahme in `InstrumentEndpoints`.
  - `Program.cs` richtet die Anmeldung ein:
    - `AddAuthentication().AddJwtBearer` mit `Authority = <keycloak>/realms/portal`
    - `Audience = "portal-api"`, `RoleClaimType = "roles"`, `NameClaimType = "preferred_username"`
    - `RequireHttpsMetadata = false` nur in Development
    - Policies `"pfleger"` und `"freigeber"` über `RequireRole`
    - Die Keycloak-URL kommt aus `services__keycloak__http__0`, also aus der Aspire-Referenz.

- [ ] **Step 1: Failing Tests**
  - Handler-Tests in `CorporateActionPersistenceTests`, mit `FixedTime` (eine Testklasse wie in Runde 3, aber mit setzbarem `Now`):
    - `Create_and_full_approval_via_handlers` (Erfassen durch `pia`, Freigabe durch `fred`)
    - `Approve_by_creator_via_handler_returns_four_eyes_error`
    - `Unknown_action_returns_not_found`
    - `Duplicate_open_action_returns_duplicate_error`
    - `Public_list_hides_drafts_and_submitted`
  - `CorporateActionEndpointTests.Write_endpoints_require_expected_role`:
    - Baut `WebApplication.CreateBuilder().Build()`, ruft `MapCorporateActionEndpoints` und `MapInstrumentEndpoints` auf und liest dann alle `RouteEndpoint`s.
    - Erwartet wird pro Endpunkt die Policy über `IAuthorizeData.Policy`, und zwar für jede Kombination aus HTTP-Methode und Route aus der Liste oben.
    - Für `GET /instruments/{id}/corporate-actions` und `GET /instruments/{id}` darf kein `IAuthorizeData` existieren.

Run: `dotnet test tests/Portal.Tests --filter "CorporateActionPersistenceTests|CorporateActionEndpointTests"`
Expected: Build-Fehler.

- [ ] **Step 2: Implementieren** (wie unter Interfaces beschrieben; `InstrumentEndpoints` braucht für den Test `public` oder `InternalsVisibleTo`)

- [ ] **Step 3: Tests grün, Landkarte, Commit**

Run: `dotnet test tests/Portal.Tests --filter "FullyQualifiedName~Portal.Tests.Instruments"`
Expected: alle bestanden.

Landkarte Zeile 5: Neu dazu kommen `CorporateActionConfiguration.cs` (Revision) und der Test `Concurrent_approvals_one_wins_other_conflicts`. Der Status bleibt `teilweise`, bis das Experiment in Task 9 läuft.

```bash
git add -A && git commit -m "feat(instruments): corporate action use cases and role-protected endpoints"
```

---

### Task 4: Wirksamkeits-Job

**Files:**
- Create: `src/Instruments/Portal.Instruments.Infrastructure/CorporateActions/EffectivenessJob.cs`
- Modify: `Api/Program.cs`, `src/ServiceDefaults/Extensions.cs` (`AddSource("Portal.Instruments")`)
- Test: `tests/Portal.Tests/Instruments/EffectivenessJobTests.cs`

**Interfaces:**
- Consumes: `CorporateAction.BecomeEffective` (Task 1), Outbox-Mapping (Task 2).
- Produces: `sealed class EffectivenessJob(IServiceScopeFactory scopes, TimeProvider time, ILogger<EffectivenessJob> log) : BackgroundService` mit:
  - `public async Task<int> RunOnceAsync(CancellationToken ct)`
  - `public static readonly ActivitySource Source = new("Portal.Instruments")`

  `RunOnceAsync` macht pro Durchlauf (neuer Scope, eine Transaktion):
  ```sql
  SELECT * FROM corporate_actions WHERE status = 'Approved' AND effective_at <= @now
  ORDER BY effective_at LIMIT 10 FOR UPDATE SKIP LOCKED
  ```
  - Abfrage per `FromSql` auf `CorporateActions`, damit EF die Zeilen trackt.
  - Pro Maßnahme: Activity `corporate-action.effective` mit Tag `corporateActionId` starten, Instrument laden, `BecomeEffective` aufrufen. Ein Fehler wird geloggt und übersprungen.
  - Danach `SaveChangesAsync` und Commit. Die Outbox übernimmt `Activity.Current.Id` als `traceparent`, darum beginnt Trace 2 hier.
  - Rückgabe: Anzahl der wirksam gewordenen Maßnahmen.
  - `ExecuteAsync`: alle 5 s `RunOnceAsync`.
  - Kommentar: Sicher bei mehreren Instanzen über SKIP LOCKED (keine zwei Jobs fassen dieselbe Zeile an) und über den Zustandsübergang (eine wirksame Maßnahme ist nicht mehr `Approved`).
  - `Program.cs` registriert den Job `CorporateActions:JobInstances` mal (Standard 1). Mit 2 entsteht Experiment 17 ohne Code-Änderung.

- [ ] **Step 1: Failing Tests** (`FixedTime`, Freigabe bei `now`, Stichtag `now + 2 min`, dann `now += 3 min`)
  - `Due_action_becomes_effective_with_outbox_in_same_transaction`: Ergebnis 1, Status `Effective`, eine Outbox-Zeile `CorporateActionBecameEffective`.
  - `Not_yet_due_action_stays_approved`: Ergebnis 0.
  - `Isin_change_updates_instrument_and_writes_snapshot`: Das Instrument hat die neue ISIN, in der Outbox steht ein `InstrumentSnapshot` mit der neuen ISIN und `Revision + 1`.
  - `Delisting_sets_instrument_delisted`
  - `Two_jobs_in_parallel_make_each_action_effective_once`:
    - 20 fällige Maßnahmen auf 20 Instrumenten, zwei Jobs per `Task.WhenAll(RunOnceAsync × je 3)`.
    - Erwartet: Summe der Ergebnisse 20, 20 Outbox-Zeilen `CorporateActionBecameEffective`, alle 20 `Effective`.

Run: `dotnet test tests/Portal.Tests --filter EffectivenessJobTests`
Expected: Build-Fehler.

- [ ] **Step 2: Implementieren, Tests grün, Landkarte, Commit**

Run: `dotnet test tests/Portal.Tests --filter EffectivenessJobTests`
Expected: 5 bestanden.

Landkarte Zeile 17: `EffectivenessJob.cs`, Test `Two_jobs_in_parallel_make_each_action_effective_once`, Status `teilweise`, das Experiment folgt in Task 9.

```bash
git add -A && git commit -m "feat(instruments): effectiveness job safe for parallel instances"
```

---

### Task 5: Keycloak und BFF-Login

**Files:**
- Create:
  - `src/AppHost/keycloak/portal-realm.json`
  - `src/Bff/{MemoryTicketStore,Csrf,PdpEndpoint}.cs`
- Modify: `src/AppHost/AppHost.cs`, `src/AppHost/Portal.AppHost.csproj`, `src/Bff/Program.cs`, `src/Bff/appsettings.json`, `src/Bff/Portal.Bff.csproj`, `src/Frontend/server/api/[...path].ts`
- Test: `tests/Portal.Tests/Bff/CsrfTests.cs` (Testprojekt referenziert `src/Bff`)

**Interfaces:**
- Produces:
  - **AppHost:**
    - `var keycloak = builder.AddKeycloak("keycloak", 8080).WithRealmImport("./keycloak");`
    - Instrument und BFF bekommen `.WithReference(keycloak).WaitFor(keycloak)`.
    - Dev-Modus mit eingebauter H2-Datenbank. `// ponytail:` Kommentar: eigene Postgres-DB für Keycloak erst in Azure (Runde 5).
  - **Realm `portal`:**
    - Rollen `pfleger` und `freigeber`
    - Client `portal-bff`: vertraulich, Secret `portal-bff-dev-secret`, Standard-Flow ohne Direct Grants
    - Redirect `http://localhost:*/api/signin-oidc`, Post-Logout `http://localhost:*/*`
    - Protocol Mapper `audience` (Audience `portal-api` im Access Token) und `roles` (Realm-Rollen als flacher Claim `roles`, mehrwertig)
    - `accessTokenLifespan: 1800`
    - Nutzer `pia`, `fred` und `frieda` wie in Global Constraints
  - **BFF:**
    - Default-Scheme Cookie mit `SameSite=Lax`, `HttpOnly` und `SessionStore = new MemoryTicketStore()`. Kommentar `// ponytail: Sitzungen im Speicher – nach BFF-Neustart neu anmelden; Ausbau IDistributedCache`.
    - Challenge per OIDC:
      - Authority = Keycloak-Realm, `ClientId = portal-bff`, Secret aus `Bff:ClientSecret`, `ResponseType = code`
      - `SaveTokens = true` (landet im Session-Store, nicht im Cookie)
      - `CallbackPath = /api/signin-oidc`, `SignedOutCallbackPath = /api/signout-callback-oidc`
      - `MapInboundClaims = false`, `TokenValidationParameters.RoleClaimType = "roles"`, `NameClaimType = "preferred_username"`
    - Bei Pfaden unter `/api` liefert der Cookie-Handler 401 statt eines Redirects.
    - Weitere Middleware:
      - `UseForwardedHeaders(XForwardedHost | XForwardedProto)`, damit die Redirect-URI den Host des Browsers trägt.
      - `Csrf.IsAllowed(HttpRequest)` als Middleware vor `MapReverseProxy`. Nicht erlaubt → `400` „CSRF-Header fehlt“.
    - Endpunkte:
      - `GET /api/bff/login?returnUrl=`: Challenge. `returnUrl` muss mit `/` beginnen und darf nicht mit `//` beginnen, sonst gilt `/`.
      - `GET /api/bff/logout`: Abmelden bei Cookie und OIDC. `// ponytail: GET-Logout; erzwungenes Abmelden per Link ist harmlos`.
      - `GET /api/bff/user`: `{ isAuthenticated, name, roles[] }`
    - YARP:
      - Neue Route `corporate-actions`: `/api/corporate-actions/{**rest}` an den Cluster `instrument`, alle Methoden.
      - Die Route `instruments` bleibt GET-only.
      - Request-Transform: Bei angemeldetem Nutzer und dem Cluster `instrument` wird `Authorization: Bearer <access_token>` aus `GetTokenAsync("access_token")` gesetzt.
    - `PdpEndpoint`: `GET /api/pdp/{id:guid}` holt parallel `GET http://instrument/instruments/{id}` und `…/instruments/{id}/corporate-actions` (HttpClient mit Service Discovery). Ergebnis `{ instrument, corporateActions }`, `404` ohne Instrument.
  - `Csrf.IsAllowed(HttpRequest r)`: `true` bei GET, HEAD und OPTIONS oder wenn `X-CSRF: 1` gesetzt ist. Kommentar: Ein Cross-Site-Formular kann keine eigenen Header setzen, und `fetch` mit eigenem Header braucht einen CORS-Preflight, den das BFF nicht erlaubt.
  - Nuxt-Proxy: setzt `x-forwarded-host` (`getRequestHost(event)`) und `x-forwarded-proto` (`getRequestProtocol(event)`).

- [ ] **Step 1: Failing Tests** (`CsrfTests`)
  - `Safe_methods_are_allowed` (Theory GET, HEAD, OPTIONS)
  - `Unsafe_request_without_csrf_header_is_rejected` (POST, PUT, DELETE ohne Header → `false`)
  - `Unsafe_request_with_csrf_header_is_allowed`

Run: `dotnet test tests/Portal.Tests --filter CsrfTests`
Expected: Build-Fehler.

- [ ] **Step 2: Implementieren**

- [ ] **Step 3: Tests grün, live prüfen, Commit**

Run: `dotnet test tests/Portal.Tests --filter CsrfTests`, danach den AppHost starten.
Expected:
- Tests bestanden.
- Im Browser auf `/api/bff/login?returnUrl=/` landet man bei Keycloak (Login `pia/pia`) und dann wieder auf `/`. Danach liefert `/api/bff/user` `{"isAuthenticated":true,"name":"pia","roles":["pfleger"]}`.
- Im Browser gibt es nur das Cookie (`.AspNetCore.Cookies`, wenige hundert Bytes), kein Token in JS oder Local Storage.
- `curl -X POST http://<bff>/api/corporate-actions` ohne Cookie liefert 400 (CSRF), mit `X-CSRF: 1` liefert es 401.

```bash
git add -A && git commit -m "feat(bff): keycloak login with server-side session, token relay, csrf guard and pdp aggregation"
```

---

### Task 6: History – Split-Anpassungsfaktoren

**Files:**
- Create: `src/History/Portal.History/{AdjustmentStore,CorporateActionConsumer}.cs`
- Modify: `HistorySchema.cs`, `CandleQuery.cs`, `Program.cs`
- Test: `tests/Portal.Tests/History/AdjustmentTests.cs`

**Interfaces:**
- Consumes: `CorporateActionEvent.TryParse` (Task 2), `DeadLetter` (Runde 3).
- Produces:
  - DDL:
    ```sql
    CREATE TABLE IF NOT EXISTS adjustments (corporate_action_id uuid PRIMARY KEY, instrument_id uuid NOT NULL,
      valid_until timestamptz NOT NULL, factor numeric(18,8) NOT NULL);
    CREATE TABLE IF NOT EXISTS processed_messages (message_id uuid PRIMARY KEY, processed_at timestamptz NOT NULL);
    ```
  - `sealed class AdjustmentStore(NpgsqlDataSource db) { Task<bool> ApplyAsync(Envelope<CorporateActionEvent> e, CancellationToken ct); }`
    - Inbox und Wirkung laufen in einer Transaktion. Liefert `false`, wenn die `messageId` schon verarbeitet ist.
    - Nur `CorporateActionBecameEffective` mit `Kind == "Split"` legt einen Faktor an: `SplitOld / SplitNew`, `valid_until = EffectiveAt`.
    - Alle anderen Nachrichten werden nur als verarbeitet markiert.
  - `CorporateActionConsumer(AdjustmentStore, DeadLetter, ConsumerConfig, ILogger, string topic = Topics.CorporateActions) : BackgroundService`
    - Group `history-corporate-actions`, eigene Group, damit Kerzen- und Maßnahmen-Strom sich nicht gegenseitig rebalancen.
    - `AutoOffsetReset.Earliest`, manueller Commit, Poison und Retry über `DeadLetter` wie der `TickConsumer`.
    - Setzt `activity?.SetTag("corporateActionId", …)`.
  - `CandleQuery` wendet vor dem Rollup auf jede 1m-Kerze das Produkt aller Faktoren mit `bucket < valid_until` an: Preise × Faktor, Volumen ÷ Faktor. Die Rohkerzen bleiben unverändert. Kommentar: Spec-Entscheidung „Faktoren beim Lesen statt Umschreiben“.

    ```sql
    -- je Kerze: Produkt der späteren Faktoren (ln/exp = Produkt über Zeilen)
    LEFT JOIN LATERAL (SELECT coalesce(exp(sum(ln(a.factor))), 1) AS f FROM adjustments a
                       WHERE a.instrument_id = c.instrument_id AND c.bucket < a.valid_until) adj ON true
    ```
    Preise werden auf 4 Nachkommastellen gerundet.

- [ ] **Step 1: Failing Tests** (`[Collection("infra")]`)
  - `Split_factor_adjusts_older_candles_only`:
    - Kerze 10:00 mit 100, Kerze 10:10 mit 26.
    - Split 1:4 wirksam um 10:05 → 1m-Abfrage ergibt `close` 25 und 26. Volumen der 10:00-Kerze: 4-fach.
  - `Two_splits_multiply` (1:2 und 1:2 → Faktor 0,25)
  - `Same_message_twice_stores_one_factor` (zweimal `ApplyAsync`, das zweite liefert `false`, `count(*) FROM adjustments == 1`)
  - `Non_split_events_are_marked_processed_without_factor`
  - `Consumer_applies_split_from_topic` (über Kafka: eigenes Topic, Event produzieren, innerhalb von 30 s steht 1 Faktor und der Offset ist committet)

Run: `dotnet test tests/Portal.Tests --filter AdjustmentTests`
Expected: Build-Fehler.

- [ ] **Step 2: Implementieren, Tests grün, Commit**

Run: `dotnet test tests/Portal.Tests --filter "FullyQualifiedName~Portal.Tests.History"`
Expected: alle bestanden.

```bash
git add -A && git commit -m "feat(history): split adjustment factors applied on read"
```

---

### Task 7: Simulator reagiert auf Splits

**Files:**
- Create: `src/Simulator/PriceBook.cs`, `src/Simulator/CorporateActionConsumer.cs`
- Modify: `src/Simulator/SimulatorWorker.cs`, `src/Simulator/Program.cs`
- Test: `tests/Portal.Tests/Simulator/PriceBookTests.cs`

**Interfaces:**
- Produces:
  - `sealed class PriceBook { decimal Next(InstrumentSnapshot instrument, Random rng); void ApplySplit(Guid instrumentId, decimal factor); }`
    - Die Kurse liegen in einem `ConcurrentDictionary`. Der Startkurs kommt aus `StartPrices`, danach läuft `PriceWalk`.
    - `ApplySplit` multipliziert den aktuellen Kurs, gerundet auf 2 Nachkommastellen, und tut nichts, solange noch kein Kurs existiert.
  - `CorporateActionConsumer(PriceBook, ConsumerConfig, ILogger)`
    - Group `simulator-ca-{guid}`, Start bei `latest`. Kommentar: Broadcast, wie der Katalog.
    - Bei `CorporateActionBecameEffective` mit `Kind == "Split"` → `ApplySplit(InstrumentId, SplitOld / SplitNew)`.
    - Delisting braucht nichts Eigenes: Der Snapshot mit `Status = Delisted` entfernt das Instrument aus `InstrumentCatalog.Active`, damit stoppen die Ticks.
    - `// ponytail: nach Simulator-Neustart beginnt der Kurs wieder beim Startkurs (vor dem Split); Ausbau: letzten Kurs aus History holen`
  - Der Worker nutzt `PriceBook` statt des lokalen `prices`-Dictionary.

- [ ] **Step 1: Failing Tests** (`PriceBookTests`)
  - `Split_scales_current_price`: `Next` liefert p, danach `ApplySplit(id, 0.25m)` → der nächste `Next` liegt innerhalb ±1 % von `p * 0.25` (Random mit Seed).
  - `Split_before_first_price_is_ignored`

Run: `dotnet test tests/Portal.Tests --filter PriceBookTests`
Expected: Build-Fehler.

- [ ] **Step 2: Implementieren, Tests grün, Commit**

Run: `dotnet test tests/Portal.Tests --filter "FullyQualifiedName~Portal.Tests.Simulator"`
Expected: alle bestanden.

```bash
git add -A && git commit -m "feat(simulator): apply effective splits to the running price"
```

---

### Task 8: Frontend – Anmeldung, Erfassung, Freigabe, PDP

**Files:**
- Create: `src/Frontend/app/composables/useUser.ts`, `src/Frontend/app/pages/kapitalmassnahmen.vue`
- Modify: `src/Frontend/app/app.vue`, `src/Frontend/app/pages/instrumente/[id].vue`

**Interfaces:**
- Consumes: `/api/bff/user|login|logout`, `/api/corporate-actions…`, `/api/pdp/{id}`, `/api/search` (Task 3 und 5).
- Produces:
  - `useUser()` → `{ user: Ref<{ isAuthenticated, name, roles } | null>, hasRole(role) }`. Wird nur im Client geladen (`onMounted` + `$fetch`), weil das Cookie beim SSR nicht mitläuft.
  - `app.vue`: Kopfzeile mit Link „Instrumente“, „Kapitalmaßnahmen“ (nur angemeldet) und „Anmelden“ bzw. „Angemeldet als {name} · Abmelden“. Die Links zeigen auf `/api/bff/login?returnUrl=<aktueller Pfad>` und `/api/bff/logout`.
  - `/kapitalmassnahmen`:
    - **Formular „Neue Kapitalmaßnahme“** (nur `pfleger`), alle Felder mit `<label>`:
      - Instrument: Suchfeld mit Treffern aus `/api/search` als `<select>`
      - Art (Split, ISIN-Wechsel, Delisting)
      - Verhältnis `alt` / `neu` bzw. neue ISIN
      - „Wirksam ab“ als `<input type="datetime-local">`, vorbelegt mit jetzt + 3 Minuten
    - **Arbeitsliste** als Tabelle: Instrument, Art, Details, Wirksam ab (Ortszeit), Status (deutsch), erfasst von, entschieden von.
    - Buttons je Status und Rolle:
      - `pfleger`: Einreichen, Zurückziehen, Verwerfen
      - `freigeber`: Freigeben, Ablehnen, Stornieren
    - Alle schreibenden Aufrufe senden den Header `X-CSRF: 1`.
    - Fehler zeigen `problem.title`, etwa die Vier-Augen-Meldung, in einem `role="alert"`.
    - Nach jeder Aktion wird die Liste neu geladen, solange Maßnahmen `Approved` sind zusätzlich alle 10 s. Das zeigt den Weg zu „Wirksam“ ohne manuelles Neuladen.
  - PDP:
    - Lädt `/api/pdp/{id}` statt `/api/instruments/{id}`.
    - Abschnitt „Kapitalmaßnahmen“ mit „Freigegeben – wirksam ab …“, „Wirksam seit …“ und „Storniert“.
    - Solange eine Maßnahme `Approved` ist, alle 10 s neu laden.
    - Wechselt eine auf `Effective`, wird der Chart über `:key` neu aufgebaut und die angepasste Historie geladen (Landkarte 19).

- [ ] **Step 1: Implementieren** (keine Frontend-Testumgebung; die Prüfung läuft in Task 9 im Browser)
- [ ] **Step 2: Commit**

```bash
git add -A && git commit -m "feat(frontend): login, corporate action capture and approval, pdp shows corporate actions"
```

---

### Task 9: Ende-zu-Ende, Experimente, Landkarte

- [ ] **Step 1: Ablauf im Browser** (AppHost starten)
  - `pia` legt für SAP einen Split 1:4 an, wirksam in 3 Minuten, und reicht ihn ein.
  - `frieda` meldet sich an (hat beide Rollen) und versucht freizugeben → Vier-Augen-Meldung.
  - `fred` gibt frei → Status „Freigegeben“.
  - Die PDP zeigt „Freigegeben – wirksam ab …“.
  - Nach dem Stichtag:
    - Die Liste zeigt „Wirksam“.
    - Der Chart ist rückwirkend auf ein Viertel angepasst.
    - Der Ticker läuft im neuen Niveau weiter.
    - Kurz nach dem Stichtag ist ein Sprung möglich. Er ist bewusst akzeptiert, siehe Spec Abschnitt 6.
  - Ein ISIN-Wechsel macht die Suche nach der neuen ISIN möglich, die Ticks laufen unverändert weiter (Landkarte 8).
  - Ein Delisting lässt die Ticks stoppen, danach nimmt das Instrument keine neue Maßnahme mehr an.
- [ ] **Step 2: Experimente**
  - **5 Optimistic Concurrency:** Zwei Freigaben derselben Maßnahme gleichzeitig per `curl` (zwei Sessions `fred` und `frieda`, Cookies aus dem Browser) → ein `204` und ein `409 CorporateAction.ConcurrentUpdate`.
  - **17 Job doppelt:** `CorporateActions__JobInstances=2` am Instrument-Service, 5 Maßnahmen mit demselben Stichtag freigeben. Erwartet: 5 `BecameEffective` in `corporate-actions.v1`, nicht 10.
  - **18 Verteilte Folge:**
    - History im Dashboard stoppen.
    - Einen Split wirksam werden lassen → der Chart zeigt „nicht verfügbar“, der Ticker läuft im neuen Niveau.
    - History starten → der Chart ist angepasst.
  - **19 Eventual Consistency:** Die Abfolge „Freigegeben“ → „Wirksam“ → angepasster Chart ist ohne manuelles Neuladen sichtbar.
  - **21 BFF:**
    - Das Cookie enthält nur die Session (Größe, kein JWT darin).
    - `pia` versucht freizugeben → 403.
    - Eine Anfrage ohne `X-CSRF` → 400.
    - Nach einem BFF-Neustart muss man sich neu anmelden (Session-Store im Speicher).
  - **22 Tracing:** Im Aspire-Dashboard per `corporateActionId` beide Traces finden:
    - Freigabe → `corporate-actions.v1 publish`
    - Job → `publish` → History `consume`
- [ ] **Step 3: Landkarte abschließen und committen**

Zeilen 2, 5, 17, 18, 19, 21 und 22 bekommen Ort, Test, Beobachtung und den Status `drin`.
- **Zeile 18:** `src/History/Portal.History/CandleQuery.cs` (Faktoren) und `src/Simulator/CorporateActionConsumer.cs`, Tests `Split_factor_adjusts_older_candles_only` und `Split_scales_current_price`.
- **Zeile 19:** `src/Frontend/app/pages/kapitalmassnahmen.vue` und `[id].vue`, Test „–“.
- **Zeile 21:** `src/Bff/Program.cs` und `MemoryTicketStore.cs`, Test `CsrfTests`.

```bash
git add -A && git commit -m "docs: round 4 learning map"
```

---

## Nicht in dieser Runde

Azure/Bicep, Event Hubs und der Budget-Alarm kommen in Runde 5. Ebenfalls nicht dabei:

- Token-Refresh: Das Access Token lebt 30 Minuten, danach muss man sich neu anmelden.
- Eigene Keycloak-Datenbank
- Rebalance-sicherer Neuaufbau

Die aufgeschobenen Kleinigkeiten aus Runde 2 und 3 bleiben offen, es sei denn, eine Task berührt dieselbe Stelle ohnehin.
