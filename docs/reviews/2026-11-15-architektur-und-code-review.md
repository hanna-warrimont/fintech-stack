# Architektur- und Code-Review Instrumenten-Portal

Stand: 2026-11-15 · Basis: `93fbc82` · Umfang: alle Dateien unter `src/`, `tests/`, `infra/` und `docs/`

**Geprüft:** `dotnet build` ohne Warnungen und Fehler. 111 Tests ohne Container sind grün. Die rund 40 Testcontainers-Tests liefen nicht, weil Docker aus war. Frontend: kein `npm ci`, kein Typecheck, kein `npm audit`, weil `node_modules` fehlt.

Bewertet wird an zwei Maßstäben: am eigenen Ziel (Referenz-App zum Lernen, siehe `docs/superpowers/specs/…-design.md`) und an aktuellen Best Practices für .NET 10/Aspire, Nuxt 4 und System Design.

---

## 1. Gesamturteil

Die Codebasis ist für eine Lern-Referenz ungewöhnlich sauber. Jedes Konzept hat eine klar umrissene Stelle, ist kommentiert und meist getestet. Abkürzungen sind fast überall als `ponytail:` markiert und nennen ihren Ausbaupfad. Die verteilten Kernmechanismen (Outbox, Inbox, Seq-Dedupe, DLT, SKIP LOCKED, Trace-Propagation) sind fachlich korrekt umgesetzt. Es gibt keinen Fund der Klasse „Datenverlust im Normalbetrieb“.

Die Schwächen liegen woanders:
1. **Eine Schichtregel der eigenen Architektur ist verletzt** (Application kennt Postgres), und der Architekturtest fängt das nicht.
2. **Betriebsreife in Azure:** keine Health-Endpunkte, keine Probes, ein gemeinsamer DB-Superuser für alle Dienste.
3. **Querschnitt fehlt:** zentrale Paketverwaltung, Analyzer, Frontend-Tests und Lint, Aufräumen von Outbox und Inbox.

---

## 2. Was gut ist (behalten)

- **Domäne:** Value Objects (`Isin` mit Luhn-Prüfung, `Wkn`, `Currency`, `SplitRatio`), die nur gültig erzeugbar sind. Ein Zustandsautomat im Aggregat, Result-Pattern mit stabilem Fehlercode, Optimistic Concurrency über `Revision`. Ein partieller Ausdrucks-Index entscheidet den Wettlauf „eine offene Maßnahme pro Tag“ in der Datenbank.
- **Outbox:** Sie wird in `SaveChangesAsync` in derselben Transaktion geschrieben, und der Dispatcher liest per `SKIP LOCKED`. Der `traceparent` wird gespeichert und beim Senden fortgesetzt. Das ist sauberer als in vielen Produktivsystemen.
- **Consumer:** Commit erst nach Wirkung oder DLT. Die Inbox (`processed_messages`) ist zusätzlich durch einen `revision`-Guard abgesichert. `DeadLetter` unterscheidet bewusst zwischen vorübergehenden DB-Fehlern (nicht ins DLT, weil sie Ticks verlieren würden) und Poison (sofort ins DLT). Das ist ein sehr guter, selten gesehener Gedanke.
- **History:** Event-Time statt Verarbeitungszeit. Split-Faktoren werden beim Lesen angewendet, die Rohdaten bleiben unverändert. Der Rollup erfolgt per SQL, statt Tabellen zu synchronisieren.
- **Wirksamkeits-Job:** eine Transaktion pro Maßnahme, Idempotenz über den Zustandsübergang, Storno bei Nicht-Anwendbarkeit statt Endlosschleife.
- **BFF:** Tokens bleiben serverseitig (`ITicketStore`), CSRF über einen Custom Header plus SameSite=Lax, Open-Redirect-Schutz inklusive Steuerzeichen. Das Session-Cookie wird vor YARP entfernt, und der Instrument-Service prüft das Token selbst (Zero Trust zwischen BFF und API).
- **Quote:** `TypedResults.ServerSentEvents` (.NET 10), ein Bounded Channel pro Abonnent mit `DropOldest` und `CompleteAll` beim Shutdown.
- **Infra:** `@secure()`-Parameter, Bicep-Linter-Regeln als Fehler, Budget-Alarm, Managed Identity für ACR, ein Abriss über eine einzige Ressourcengruppe.
- **Tests:** Testcontainers statt Mocks für Kafka und Postgres, Architekturtests ohne Zusatz-Library, Zeit über `TimeProvider`.

---

## 3. Befunde

Schwere: **H** = beheben, bevor das Projekt als Referenz dient · **M** = sollte · **N** = kann

### Architektur (Clean Architecture / DDD)

| # | S | Befund | Ort | Empfehlung |
|---|---|---|---|---|
| A1 | ✅ | **Erledigt:** Die Queries nutzen jetzt `System.Data.Common.DbDataSource`, Npgsql ist aus Application entfernt, der Architekturtest verbietet `Npgsql` und `Confluent`. Ursprünglicher Befund (H): **Application hängt an Npgsql und Dapper.** `ListCorporateActionsQuery` und `GetInstrumentQuery` nutzen `NpgsqlDataSource` direkt, und das csproj referenziert `Npgsql`. Das widerspricht der eigenen Regel („Application soll nichts über Postgres wissen“, `IUnitOfWork.cs:11`) und dem Kursaufbau. `ArchitectureTests` verbietet für Application nur Infrastructure, Api und EF und lässt den Verstoß deshalb durch. | `Portal.Instruments.Application/CorporateActions/ListCorporateActions.cs:12`, `Instruments/GetInstrument.cs:9`, `Portal.Instruments.Application.csproj:10`, `tests/…/ArchitectureTests.cs:25` | Wie im Kurs: `ISqlConnectionFactory` (gibt `DbConnection` zurück) in Application, Implementierung in Infrastructure. Dapper darf in Application bleiben (Kurs-Variante), Npgsql nicht. Den Test um `"Npgsql"` erweitern. |
| A2 | M | **Vier-Augen-Prinzip über `preferred_username`** statt über den unveränderlichen `sub`. Ein Admin kann in Keycloak umbenennen, und ein gelöschter Nutzername kann neu vergeben werden. | `CorporateActionEndpoints.cs:46`, `CorporateAction.cs:74` | `CreatedById` (sub) für die Regel speichern, den Namen nur zur Anzeige. |
| A3 | N | `BecomeEffective` ändert zwei Aggregate (CorporateAction und Instrument) in einer Transaktion. Das ist eine bewusste Entscheidung in der Spec und vertretbar (gleicher Bounded Context, gleiche DB), widerspricht aber der Daumenregel „ein Aggregat pro Transaktion“. | `CorporateAction.cs:93` | Als Lernpunkt in die Landkarte aufnehmen: die Alternative wären Domain Event und ein zweiter Schritt (eventual). |
| A4 | N | `LeadTime`, `JobInstances` und das Simulator-Tempo werden per `GetValue` und Lambda-Factory verdrahtet statt über das Options-Pattern. Bei 3 Werten ist das ok, `IOptions<T>` mit `ValidateOnStart` wäre die Standardform. | `Portal.Instruments.Api/Program.cs:19`, `Application/DependencyInjection.cs:16` | Optional. |
| A5 | N | `InstrumentDbContext` stempelt `OccurredAt` mit `DateTimeOffset.UtcNow`, der Rest des Codes nutzt `TimeProvider`. | `InstrumentDbContext.cs:30` | `TimeProvider` injizieren, das macht die Outbox-Zeit testbar. |

### Verteilte Systeme und Messaging

| # | S | Befund | Ort | Empfehlung |
|---|---|---|---|---|
| D1 | M | **Synchroner Commit pro Tick.** `consumer.Commit(result)` ist ein Broker-Roundtrip pro Nachricht und der eigentliche Durchsatzdeckel im Lag-Experiment. | `History/Portal.History/TickConsumer.cs:60` | Confluent-Muster: `EnableAutoOffsetStore=false`, `EnableAutoCommit=true`, nach der Verarbeitung `consumer.StoreOffset(result)`. Das bleibt at-least-once. Den manuellen Commit als Lernstoff behält Search. |
| D2 | M | **Search hat kein DLT.** Ungültige Nachrichten werden nur geloggt und committet. Der Kommentar „kein DLT bis Runde 3“ ist veraltet, Runde 3 ist durch. | `Search/Portal.Search/SnapshotConsumer.cs:55` | `DeadLetter.SendAsync` wie in History. Das DLT-Topic in `eventhubs.bicep` ergänzen. |
| D3 | M | **Outbox und Inbox wachsen unbegrenzt.** Gesendete `outbox_messages` und `processed_messages` (Search, History) werden nie gelöscht. | `OutboxDispatcher.cs:71`, `InstrumentProjection.cs:19`, `AdjustmentStore.cs:21` | Aufräum-Job: Outbox `processed_at < now() - 7 days`. Inbox nur, solange das Fenster größer ist als die maximale Redelivery-Spanne (Topic-Retention). |
| D4 | M | **Outbox-Dispatcher:** Die DB-Transaktion und die Zeilensperren bleiben offen, während bis zu 50 Nachrichten **nacheinander** an Kafka gehen. Gegen Event Hubs (Request-Timeout 60 s) kann das lange dauern. Zudem `ORDER BY occurred_at` ohne Tie-Breaker, die Zeit kommt von der App-Uhr. | `OutboxDispatcher.cs:51-74` | Reihenfolge über eine `bigint GENERATED ALWAYS AS IDENTITY`-Spalte. Senden mit `Produce` plus Delivery-Handler und einem `Flush` pro Batch, das hält die Reihenfolge dank Idempotenz und spart Roundtrips. Der Single-Dispatcher-Hinweis ist gut dokumentiert. |
| D5 | M | **„Keine Migrationen, weil neu aufbaubar“ gilt in Azure nicht für `adjustments`.** `corporate-actions.v1` hält dort nur 168 h (Standard-Tarif). Nach einer Woche ist ein Split nicht mehr aus dem Topic rekonstruierbar, und ein Schema-Neuaufbau verliert ihn. | `History/Portal.History/HistorySchema.cs:7`, `infra/modules/eventhubs.bicep:21` | In `docs/kafka-vs-event-hubs.md` als Folge vermerken. Die Faktoren als Quelle der Wahrheit behandeln (sichern, migrieren) oder beim Start bei Instrument nachfragen. |
| D6 | N | Ein Start-Seed bei mehreren Instrument-Instanzen läuft in einen Wettlauf: Die zweite scheitert am Unique-Index und beendet den Prozess. | `SeedData.cs:22` | Bei `DuplicateIsin` still weitermachen. |

### Resilienz, Betrieb, Observability

| # | S | Befund | Ort | Empfehlung |
|---|---|---|---|---|
| O1 | H | **In Azure gibt es keine Health-Endpunkte und keine Probes.** `ASPNETCORE_ENVIRONMENT` ist nicht gesetzt, also gilt Production, und `MapDefaultEndpoints` mappt `/health` und `/alive` nur in Development. `app.bicep` definiert keine Probes. Außerdem gibt es keine Dependency-Checks: `/health` meldet „healthy“, auch wenn Postgres oder Kafka weg sind. | `ServiceDefaults/Extensions.cs:113`, `infra/modules/app.bicep:38-55` | Health-Endpunkte immer mappen (sie sind nur intern erreichbar) und in `app.bicep` Liveness auf `/alive` und Readiness auf `/health` setzen. DB-Checks über `AddNpgSql` und Kafka-Checks ergänzen (siehe O2). |
| O2 | M | **Aspire-Client-Integrationen fehlen.** `NpgsqlDataSource.Create(GetConnectionString(…)!)` wird von Hand gebaut, sodass Health Checks, Metriken und Config-Validierung fehlen. Das `!` verschiebt einen Konfigurationsfehler auf die erste Abfrage. | `History/Program.cs:11`, `Search/Program.cs:9`, `Instruments.Api/Program.cs:20` | `builder.AddNpgsqlDataSource("historydb")` und `AddNpgsqlDbContext<…>` (`Aspire.Npgsql*`). Kafka bewusst ohne `Aspire.Confluent.Kafka`, weil das Lernziel den Client sichtbar halten soll. Dann aber einen eigenen kleinen Health Check schreiben. |
| O3 | M | **PDP-Aggregation im BFF:** Der `HttpClient` „instrument“ läuft über `AddStandardResilienceHandler` (3 Retries, Gesamt-Timeout 30 s), während YARP für denselben Dienst auf 5 s Fast-Fail getrimmt ist. Fällt der Instrument-Service aus, hängt die PDP bis zu 30 s. Bei 404 bleibt der `actions`-Task unbeobachtet, und `EnsureSuccessStatusCode` wird zu einem nackten 500. | `Bff/BffEndpoints.cs:30-43` | Timeouts für diesen Client angleichen (`AddStandardResilienceHandler(o => o.TotalRequestTimeout…)`). Beide Tasks per `Task.WhenAll` abwarten. Ein Fehler wird zu `Results.Problem(statusCode: 502)`. |
| O4 | N | Es gibt kein `UseExceptionHandler()`. Mit `AddProblemDetails` liefern unbehandelte Fehler in Production ein leeres 500. | `Instruments.Api/Program.cs:16`, BFF | `app.UseExceptionHandler()` (das nutzt dann ProblemDetails). |
| O5 | N | `QuoteHub` räumt den äußeren Dictionary-Eintrag nie weg. Jede SSE-Anfrage mit einer beliebigen GUID über den öffentlichen Endpunkt legt einen dauerhaften Eintrag an, und die Zahl gleichzeitiger Streams ist nicht begrenzt. | `Quote/QuoteHub.cs:54`, `:64` | Im `finally` den leeren Eintrag entfernen oder nur bekannte Instrumente zulassen. Im BFF `AddRateLimiter` (ConcurrencyLimiter) für `/api/quotes`. |

### Security (vor allem Azure)

| # | S | Befund | Ort | Empfehlung |
|---|---|---|---|---|
| S1 | H | **Ein DB-Superuser für alles.** Alle Dienste und Keycloak verbinden sich als `portaladmin`. Die Regel „Kein Service greift auf die DB eines anderen zu“ gilt dadurch nur per Konvention. Dazu kommt die Firewall-Regel `0.0.0.0` (alle Azure-Kunden, nicht nur die eigene Subscription). | `infra/main.bicep:102`, `infra/modules/postgres.bicep:23` | Pro Dienst eine eigene Rolle mit Rechten nur auf die eigene DB (z. B. per Init-Skript im Deploy, oder Entra-Auth mit der Managed Identity). Der Private-Endpoint-Ausbau ist bereits als `ponytail:` notiert. |
| S2 | M | **Keycloak öffentlich ohne Härtung:** `bruteForceProtected` aus, keine `passwordPolicy`, PKCE nicht serverseitig erzwungen (`pkce.code.challenge.method`). Dazu zwei Realm-Dateien, die auseinanderlaufen können. | `infra/keycloak/portal-realm.json`, `src/AppHost/keycloak/portal-realm.json` | Brute-Force-Schutz an und PKCE `S256` erzwingen (ASP.NET sendet PKCE ohnehin). Eine Realm-Datei mit Platzhaltern für beide Umgebungen. |
| S3 | N | Die Störschalter des Simulators sind auch in Azure gemappt (interner Ingress, aber für jede App im Environment erreichbar). | `Simulator/Program.cs:32` | `if (app.Environment.IsDevelopment())` oder Config-Flag. |
| S4 | N | Intern Klartext-HTTP (`allowInsecure`), keine Peer-Verschlüsselung. Event Hubs nutzt einen gemeinsamen SAS-Connection-String. | `infra/modules/app.bicep:33`, `infra/main.bicep:100` | `peerTrafficConfiguration.encryption.enabled: true` am Environment, das ist günstig. Event Hubs mit Entra (OAUTHBEARER und Managed Identity) als späterer Lernschritt. |
| S5 | N | Das Frontend setzt keine Security-Header (CSP, `X-Frame-Options`, HSTS). | `src/Frontend/nuxt.config.ts` | `routeRules: { '/**': { headers: { … } } }` oder das Modul `nuxt-security`. |
| S6 | N | `deploy.sh` übergibt die Geheimnisse als `-p key=value` auf der Kommandozeile (lokal in `ps` sichtbar), und `main.bicepparam` liegt ungenutzt daneben. | `infra/deploy.sh:32` | `--parameters main.bicepparam` nutzen, dann liest `readEnvironmentVariable` die Werte aus der Umgebung. |

### .NET Best Practices (Querschnitt)

| # | S | Befund | Empfehlung |
|---|---|---|---|
| N1 | M | **Keine `Directory.Build.props`, kein Central Package Management, keine `global.json`.** `TargetFramework`, `Nullable` und `ImplicitUsings` stehen in 13 csproj. Dapper und Npgsql sind mehrfach versioniert, `Microsoft.Extensions.*` gemischt 10.0.0 und 10.0.15. | `Directory.Packages.props` (`ManagePackageVersionsCentrally`), `Directory.Build.props` mit `TreatWarningsAsErrors`, `AnalysisLevel=latest-recommended`, `global.json` mit `rollForward: latestFeature`. |
| N2 | M | **`Currency` holt die gültigen Codes aus ICU (`CultureInfo.GetCultures`).** Mit `InvariantGlobalization` oder einem Image ohne ICU (Alpine, chiseled) ist jede Währung ungültig, und der Seed bricht den Start ab. Dazu fehlen Codes ohne Land (z. B. `XAU`). | `Domain/Instruments/Currency.cs:9` | Eine kleine feste Liste der benötigten Codes oder ein Test, der im Container-Image prüft, dass `EUR` gültig ist. |
| N3 | N | Ein Testprojekt für alle Dienste. Das ist ok, koppelt aber den Build aller Services. xUnit v2, aktuell ist v3. | Bei Phase 2 (Lückentext) prüfen, ob ein Testprojekt pro Dienst die Lücken besser isoliert. |
| N4 | N | Postgres-Versionen laufen auseinander: Tests 17, Azure 16, Aspire Standard. | Eine Version festlegen (`AddPostgres(...).WithImageTag`, Testcontainers-Image, Bicep). |
| N5 | N | Keine OpenAPI-Beschreibung (`AddOpenApi` ist seit .NET 9 eingebaut). | Optional, hilft beim Ausprobieren der Endpunkte. |

### Nuxt Best Practices

| # | S | Befund | Ort | Empfehlung |
|---|---|---|---|---|
| F1 | M | **Keine Tests, kein Lint, kein Typecheck** für das Frontend. | `src/Frontend/package.json` | `@nuxt/eslint`, `nuxi typecheck` (`vue-tsc`) als Skript. Mindestens ein `@nuxt/test-utils`- oder Playwright-Test für den Ticker und die Freigabe. |
| F2 | N | Die Proxy-Route liest `process.env.NUXT_BFF_URL` statt `useRuntimeConfig(event).bffUrl`. Das funktioniert, ist aber nicht der dokumentierte Weg (`runtimeConfig` wird von `NUXT_*` überschrieben, ist typisiert und Preset-neutral). | `server/api/[...path].ts:8` | `runtimeConfig: { bffUrl: '' }` in `nuxt.config.ts`. |
| F3 | N | Ein unbekanntes Instrument rendert per SSR mit **Status 200** und „nicht gefunden“, und jeder Fehler (auch 504) wird als „nicht gefunden“ angezeigt. | `pages/instrumente/[id].vue:26` | Bei 404 `throw createError({ statusCode: 404 })`, sonst eine eigene Fehlermeldung. |
| F4 | N | `useUser` ruft `/api/bff/user` pro Aufrufer in `onMounted` ab (App und Seite = 2 Requests) und rendert per SSR immer „abgemeldet“ (Flackern). Der Kommentar „Session-Cookie ginge nicht mit“ stimmt so nicht: `useFetch` reicht beim SSR die Cookies weiter. | `composables/useUser.ts:5-11` | `useFetch('/api/bff/user', { key: 'user' })` einmal in `app.vue` oder einem Plugin. |
| F5 | N | Die Instrumentsuche in der Kapitalmaßnahmen-Seite hat kein Debounce, und alte Antworten können neuere überschreiben. | `pages/kapitalmassnahmen.vue:33` | Wie in `index.vue` `useFetch` mit reaktiver Query nutzen, dann bricht Nuxt veraltete Requests selbst ab. |
| F6 | N | Das Docker-Image nutzt `node:22`. Aktuelles LTS ist Node 24. | `src/Frontend/Dockerfile` | `node:24-alpine`. |

### Doku und Reproduzierbarkeit

| # | S | Befund | Empfehlung |
|---|---|---|---|
| R1 | M | Die Landkarte verweist mehrfach auf `.superpowers/run-apphost.sh` (Experimente 9 und 17), aber `.superpowers/` steht in `.gitignore`. Die Experimente sind aus dem Repo heraus nicht reproduzierbar. | Das Skript nach `scripts/` verschieben und einchecken, oder die Aufrufe dokumentieren (`HistoryReplicas=2 aspire run`). |
| R2 | N | Es gibt keine `README.md`, also keinen Einstieg mit „wie starte ich das“, Voraussetzungen (Docker, .NET 10, Node) und Testlauf. | Eine kurze README mit Links auf Spec, Landkarte und `docs/azure.md`. |

---

## 4. System Design – Einordnung

- **Schnitt der Services:** Für das Lernziel passend, weil jeder Dienst ein eigenes Muster zeigt. Für ein echtes Produkt dieser Größe wäre es zu fein geschnitten. Ein modularer Monolith (Instrument + Search + History) mit Kafka nur für Ticks wäre günstiger. Die Spec nennt das selbst als verworfene Variante C, die Entscheidung ist also bewusst und dokumentiert.
- **Skalierungsgrenzen** sind alle benannt: ein Outbox-Dispatcher, eine BFF-Instanz (In-Memory-Session), Neuaufbau nur mit einer Instanz, Quote-Speicher flüchtig. Der nächste sinnvolle Ausbau mit dem größten Lerneffekt wäre die **Leader-Wahl per `pg_try_advisory_lock`** für den Dispatcher (D4/Kommentar), danach **`IDistributedCache` fürs BFF**.
- **Konsistenzmodell:** Event-carried State Transfer mit `revision` und kompaktiertem Topic ist richtig gewählt. Die Lücke zwischen „Kompaktierung behält den letzten nach Offset“ und „Revision“ ist erkannt und dokumentiert.
- **Event-Versionierung:** Topic-Name plus `version`-Feld, strenges JSON (`RespectRequiredConstructorParameters`). Für das Lernziel gut. Ein Contract-Test (Producer-JSON gegen Consumer-Parser) würde Brüche zwischen Diensten früh fangen.
- **Azure-Kosten und -Abriss:** vorbildlich (Budget, `destroy.sh`, Log-Analytics-Cap, kein Auto-Inflate).

---

## 5. Empfohlene Reihenfolge

1. **A1** Application von Npgsql lösen und den Architekturtest schärfen (klein, betrifft direkt den Lückentext).
2. **O1 + O2** Health Checks mit DB-Checks und Probes in `app.bicep`.
3. **S1** eigene DB-Rolle pro Dienst.
4. **D2 + D3** DLT für Search, Aufräum-Job für Outbox und Inbox.
5. **N1** `Directory.Build.props` und `Directory.Packages.props`.
6. **F1** Lint, Typecheck und ein Frontend-Test.
7. **R1** das Experiment-Skript einchecken.
