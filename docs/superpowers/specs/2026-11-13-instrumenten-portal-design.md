# Instrumenten-Portal – Design

Stand: 2026-11-13 · Status: zur Freigabe (Builder-Check eingearbeitet)

## 1. Absicht und Warum

Hanna will **verteilte Systeme lernen**. Fundament ist Milan Jovanovićs Kurs *Pragmatic Clean Architecture* (.NET, DDD, CQRS, Outbox, Keycloak). Der Kurs endet bei einem Monolithen. Dieses Projekt geht weiter: mehrere Services, Kafka dazwischen, Azure als Ziel.

Das Vorgehen hat zwei Phasen:

1. **Phase 1, Referenz-App in mehreren Runden.** Claude baut die App. Nach jeder Runde prüft Hanna anhand der **Lernziel-Landkarte** (Abschnitt 8), ob alles drin ist, was sie lernen will. Danach wird ergänzt.
2. **Phase 2, Lernkurs aus der Referenz.** Den Kern bildet ein **Lückentext**: Kernstellen werden aus der laufenden Referenz herausgeschnitten und durch einen Platzhalter mit roten Tests ersetzt, Hanna implementiert sie selbst. Ergänzt wird das durch **Kaputtmach-Experimente**: Hanna sagt erst das Verhalten vorher, dann wird die Störung ausgelöst und das Ergebnis beobachtet. Phase 2 bekommt später ein eigenes Design. Dieses Dokument legt nur fest, was die Referenz dafür erfüllen muss (Abschnitt 9).

Fachlich ist es ein **Instrumenten-Portal**: Suche nach Finanzinstrumenten, eine Produktdetailseite (PDP) mit Live-Kursticker und History-Chart sowie die Pflege von **Kapitalmaßnahmen mit Vier-Augen-Freigabe**. Die Kapitalmaßnahmen sind der DDD-Schwerpunkt.

## 2. Ziele, Nicht-Ziele, Anti-Ziele

**Ziele**
- Jedes Konzept aus der Landkarte ist im Code sichtbar, an *einer* zusammenhängenden Stelle, und durch mindestens einen Test abgesichert.
- Lokal läuft das ganze System mit einem Befehl, ohne Cloud-Kosten.
- Eine Domänenentscheidung (Split wird wirksam) hat sichtbare verteilte Folgen (Chart wird rückwirkend angepasst).
- Störungen lassen sich gezielt auslösen: Duplikate, verspätete Ticks, Poison Messages, Service-Ausfall, Last.
- Die Azure-Landschaft ist komplett in handgeschriebenem Bicep beschrieben, deploybar und wieder abreißbar.

**Nicht-Ziele**
- Echte Marktdaten, echte Orders oder Handel, Depot.
- Produktionsreife für echte Nutzer: Hochverfügbarkeit, Multi-Region, DR.
- Kubernetes, OpenSearch/Elasticsearch, Schema Registry.
- Messaging-Frameworks (MassTransit, Wolverine, NServiceBus).
- CI/CD-Pipeline (kann in einer späteren Runde kommen).

**Anti-Ziele** (das Projekt wäre gescheitert, auch wenn alles läuft)
- Konzepte verschwinden in Frameworks oder generiertem Code und sind nicht als Lücke herausschneidbar.
- Ein Konzept ist über viele Dateien und Services verschmiert, sodass die Lücke nicht klar umrissen ist.
- Nur der Happy Path funktioniert. Fehlerverhalten ist weder vorführbar noch getestet.
- Azure-Ressourcen laufen unbemerkt weiter und kosten Geld.
- Die Referenz ist so groß, dass Hanna sie nicht mehr überblickt.

## 3. Rahmenbedingungen

- **.NET 10 (LTS)**, C#. Kurswissen (.NET 8) gilt weiter.
- **Ein Git-Repo, eine Solution** in `/Users/hanna/coding/ddd-teacher`.
- Jeder .NET-Service folgt intern dem Kurs-Aufbau: `Domain` → `Application` → `Infrastructure` → `Api`. Abhängigkeiten zeigen nach innen. Für Services ohne nennenswerte Domäne (Quote, Simulator) ist das eine **bewusste Ausnahme**: ein einziges Projekt, siehe Abschnitt 5.
- **Kafka-Client: `Confluent.Kafka` direkt.** Outbox, Inbox, Retry und DLT sind selbst geschrieben.
- **Lokal:** .NET Aspire AppHost startet Kafka (KRaft, ohne Zookeeper), Postgres, Keycloak und alle Services. Die Aspire-Telemetrie dient als Dashboard für Logs und Traces.
- **Azure:** Bicep von Hand, *nicht* aus Aspire oder azd generiert. Deployment nur zeitweise, mit dokumentiertem Abriss-Befehl. Kafka-Ersatz ist **Azure Event Hubs mit Kafka-Endpunkt**, sodass derselbe Client-Code läuft.
- **Kostenrahmen Azure: höchstens 20 € pro Monat.** Event Hubs läuft im **Standard-Tarif**. Features, die nur in höheren Tarifen existieren, werden nicht live gezeigt, sondern als Unterschied dokumentiert. Alle anderen Ressourcen bekommen die kleinste sinnvolle Größe. Im Bicep gibt es einen **Budget-Alarm** (`Microsoft.Consumption/budgets`, 20 €, E-Mail als Deploy-Parameter).
- Event-Hubs-Unterschiede zu Kafka werden dokumentiert, statt sie wegzuabstrahieren. Das gilt besonders für Compaction, Retention, Partitionsanzahl und Transaktionen je Tarif. Sie sind selbst Lernstoff.

## 4. Ansätze (Entscheidung)

| | Ansatz | Abwägung |
|---|---|---|
| **A ✅** | `Confluent.Kafka` direkt, Outbox und Inbox selbst | Jedes Konzept steht im Code und lässt sich als Lücke herausschneiden. Mehr Eigencode, der genau der Lernstoff ist. |
| B | MassTransit / Wolverine | Weniger Code, aber Outbox, Retries und Idempotenz verschwinden in der Library. MassTransit ab v9 kommerziell. Passt nicht zum Lückentext. |
| C | Erst modularer Monolith, später aufteilen | Saubere Grenzfindung, aber das eigentliche Lernziel (verteilt) kommt erst spät. |

**Gewählt: A.** Das ist der einzige Ansatz, bei dem der Lückentext in Phase 2 funktioniert.

Phase 2: Lückentext als Kern, ergänzt um Kaputtmach-Experimente. Verworfen wurden das Abtippen per `/lesson`-Diktat (passiv, bei dieser Größe zu lang) und der commitweise Nachbau (zu viel Boilerplate).

Cloud: lokal entwickeln, Bicep für Azure. Verworfen wurden Terraform lokal (kein Bicep) und Bicep ohne Deployment (kein Feedback).

## 5. Architektur

```
                         ┌────────────── Kafka ───────────────┐
Kurs-Simulator ──ticks.v1 (key InstrumentId)──┬──▶ Quote-Service ──SSE──┐
     ▲                                        └──▶ History-Service      │
     │ instruments.v1 / corporate-actions.v1       ▲   (Kerzen)         │
     │                                             │                    │
Instrument-Service ──Outbox──▶ instruments.v1 ─────┼──▶ Search-Service   │
 (DDD-Kern)                    corporate-actions.v1┘                    │
     ▲                                                                  ▼
     └──────────────── BFF (.NET, YARP, Cookie-Session) ◀──── Nuxt-Frontend
                              │
                          Keycloak
```

### Services

| Service | Aufgabe | Speicher | Aufbau |
|---|---|---|---|
| **Instrument** | Stammdaten und Kapitalmaßnahmen, DDD-Kern. Schreibt über die Outbox. | Postgres-DB `instrument` | 4 Schichten |
| **Kurs-Simulator** | Random Walk pro aktivem Instrument. Störschalter per HTTP. Reagiert auf Splits und Delistings. | keiner (Zustand im Speicher) | 1 Projekt |
| **Quote** | Letzter Kurs pro Instrument im Speicher, SSE-Stream | keiner | 1 Projekt |
| **History** | Ticks werden zu 1-Minuten-Kerzen. 1h/1d per SQL-Rollup. Split-Faktoren. Neuaufbau per Replay. | Postgres-DB `history` | 4 Schichten (dünne Domäne: Kerze, Anpassungsfaktor) |
| **Search** | Projektion von `instruments.v1`, Postgres-Volltextsuche (Name, ISIN, WKN) | Postgres-DB `search` | 3 Schichten (ohne Domain) |
| **BFF** | OIDC mit Keycloak, Cookie-Session, Rollen. Proxy über YARP. PDP-Aggregation. SSE-Weiterleitung. | keiner | 1 Projekt |
| **Frontend** | Nuxt 4: Suche, PDP (Ticker und Chart), Kapitalmaßnahmen erfassen, Liste und Freigabe | – | Nuxt |

Lokal laufen ein Postgres-Server mit getrennten Datenbanken pro Service und ein Keycloak mit eigener Datenbank. Kein Service greift auf die Datenbank eines anderen zu.

### Kafka-Topics

| Topic | Key | Inhalt | Retention | Konsumenten |
|---|---|---|---|---|
| `instruments.v1` | InstrumentId | **Event-carried State Transfer**: kompletter Instrument-Snapshot nach jeder Änderung | compacted | Search, Simulator, BFF (optional) |
| `corporate-actions.v1` | InstrumentId | Fachliche Events: `CorporateActionApproved`, `…Rejected`, `…Cancelled`, `…BecameEffective` | delete, lang | History, Simulator |
| `ticks.v1` | InstrumentId | `{ instrumentId, seq, price, volume, eventTime }` | delete (lokal z. B. 7 Tage) | Quote, History |
| `<topic>.<group>.dlt` | wie Quelle | Nachrichten, die nach den Retries noch fehlschlagen, mit Fehler-Headern | delete | – (Inspektion) |

- **Key ist die interne `InstrumentId` (GUID), nicht die ISIN.** Grund: Eine ISIN kann sich durch eine Kapitalmaßnahme ändern. Mit der ISIN als Key würde die Reihenfolge pro Instrument zerbrechen. Das ist ein Landkarten-Konzept.
- Events sind JSON mit einem Umschlag `{ messageId, type, version, occurredAt, payload }`, und `traceparent` steht als Kafka-Header. Eine Schema Registry gibt es nicht. Versionierung läuft über den Topic-Namen und das Feld `version`.
- Lokal hat jedes Topic 6 Partitionen.

### Consumer Groups

- **History und Search: Arbeitsteilung.** Alle Instanzen teilen sich eine Group, jede besitzt einen Teil der Partitionen.
- **Quote: Broadcast.** Jede Instanz braucht *alle* Ticks, weil an jeder Instanz SSE-Clients hängen. Deshalb bekommt jede Instanz ihre eigene Group-ID, und der Start ist bei `latest`. Der Gegensatz zwischen beiden Modellen ist bewusst Lernstoff.

## 6. Domäne (Instrument-Service)

### Instrument (Aggregat)
- `InstrumentId`, `Isin` (Value Object, Prüfziffer nach ISO 6166 wird validiert), `Wkn?`, `Name`, `Typ` (Aktie, Anleihe, Fonds, ETF), `Währung` (Value Object, ISO 4217), `Status` (Aktiv, Delistet).
- Delistete Instrumente nehmen keine neuen Kapitalmaßnahmen an.
- Jede Änderung erzeugt einen Snapshot-Event nach `instruments.v1` (über die Outbox).

### Kapitalmaßnahme (Aggregat)
- Arten: **Split** (Verhältnis, z. B. 1:4, und Reverse-Split), **ISIN-Wechsel** (neue ISIN), **Delisting**.
- Zustände:
  ```
  Entwurf ──einreichen──▶ Eingereicht ──freigeben──▶ Freigegeben ──Stichtag──▶ Wirksam
     │                        │  └──ablehnen──▶ Abgelehnt
     └──verwerfen             └──zurückziehen──▶ Entwurf
  Freigegeben ──stornieren (vor Stichtag)──▶ Storniert
  ```
- Regeln:
  - **Vier-Augen-Prinzip:** Wer erfasst hat, darf nicht freigeben.
  - **Stichtag ist ein Zeitpunkt** (`WirksamAb`, `DateTimeOffset`), kein Datum. Er muss bei der Freigabe mindestens einen Vorlauf in der Zukunft liegen. Der Vorlauf ist konfigurierbar, lokal 1 Minute. So lassen sich Splits live beobachten. Fachlich ist das eine Vereinfachung, denn echte Kapitalmaßnahmen haben einen Ex-Tag (ein Datum). Die Zeit kommt über `TimeProvider` und ist testbar.
  - Pro Instrument und Stichtag ist höchstens eine offene Maßnahme gleicher Art erlaubt.
  - Für delistete Instruments ist keine Maßnahme möglich.
  - Gleichzeitige Freigabe durch zwei Freigeber wird über **Optimistic Concurrency** abgewiesen (Kurswissen).
- **Wirksamwerden:** Ein Hintergrundjob sucht fällige freigegebene Maßnahmen und setzt sie auf *Wirksam*. Dabei wendet er sie auf das Instrument an (ISIN-Wechsel ändert die ISIN, Delisting den Status) und schreibt in derselben Transaktion `CorporateActionBecameEffective` sowie bei Bedarf den neuen Instrument-Snapshot in die Outbox. Der Job muss bei mehreren Instanzen und Neustarts sicher sein: Er arbeitet idempotent über den Zustandsübergang und `FOR UPDATE SKIP LOCKED`.
- Fehler laufen über das Result-Pattern mit einem Fehlerkatalog (Kurswissen). Lesende Abfragen gehen über Dapper (CQRS, Kurswissen).

### Verteilte Folgen eines wirksamen Splits
- **History** speichert einen Anpassungsfaktor `(instrumentId, gültigBis, faktor)`, idempotent über die `messageId`. Lesende Abfragen wenden die Faktoren auf ältere Kerzen an, die Rohkerzen bleiben unverändert. Der Chart ist danach rückwirkend angepasst.
- **Simulator** teilt seinen aktuellen Kurs durch das Verhältnis, sodass neue Ticks im neuen Niveau kommen. Bei einem Delisting stoppt er die Ticks.
- **Eventual Consistency ist sichtbar:** Zwischen dem Wirksamwerden und der Chart-Anpassung liegt eine kurze Lücke. Ist History gerade aus, holt der Service die Anpassung nach dem Neustart nach.
- **Bewusst akzeptierter Sprung:** Ticks zwischen dem Stichtag und dem Moment, in dem der Simulator `BecameEffective` konsumiert, kommen noch im alten Kursniveau, liegen aber schon nach `gültigBis`. Im Chart entsteht deshalb kurz ein Sprung. Er wird nicht wegkonstruiert (der Simulator plant also nicht selbst), sondern in der Landkarte als sichtbare Eventual Consistency beschrieben.

## 7. Querschnitt

- **Transactional Outbox** (Instrument): Outbox-Zeilen entstehen in derselben EF-Transaktion. Ein Dispatcher als `BackgroundService` liest per `FOR UPDATE SKIP LOCKED`, sendet mit dem idempotenten Producer (`enable.idempotence=true`) und markiert die Zeilen als gesendet. Zusage: at-least-once.
- **Inbox / idempotenter Consumer** (History, Search): Die verarbeitete `messageId` wird in derselben DB-Transaktion gespeichert wie die Wirkung. Der Offset wird erst danach manuell committet.
- **Tick-Deduplizierung** (History): Der Simulator vergibt eine monotone `seq` pro Instrument. History merkt sich die letzte `seq` pro Instrument und verwirft alles mit `seq` ≤ letzter Wert. Wegen der hohen Rate werden Ticks nicht über die Inbox dedupliziert. Der Unterschied ist Lernstoff.
- **Verspätete Ticks:** Ein Tick mit neuer `seq` und alter `eventTime` aktualisiert die Kerze der alten Minute (Event-Time statt Verarbeitungszeit).
- **Retry und DLT:** Pro Nachricht gibt es N Versuche im Prozess mit Backoff, danach geht sie ins DLT mit Fehler-Headern, und der Offset wird committet. Eine Poison Message blockiert die Partition nicht.
- **Neuaufbau einer Projektion:** Search löscht die eigene DB und liest `instruments.v1` von vorn (kompaktiert). History baut die Kerzen aus `ticks.v1` innerhalb der Retention neu auf. Beides wird über ein Admin-Kommando oder einen Startparameter ausgelöst.
- **Distributed Tracing:** OpenTelemetry. `traceparent` wird beim Produzieren in den Header geschrieben und beim Konsumieren fortgesetzt. Der Weg vom Freigabe-Klick zur Chart-Anpassung ergibt **zwei Traces**: Freigabe → `Approved`, und später Job → `BecameEffective` → History. Beide tragen das Attribut `corporateActionId` und lassen sich im Aspire-Dashboard darüber finden. Ein im Aggregat gespeicherter `traceparent` ist nicht nötig.
- **Störschalter am Simulator** (HTTP, nur lokal): Duplikate senden, verspätete Ticks, Lastspitze (Ticks pro Sekunde einstellbar), Poison Message, Pause.
- **SSE:** Quote liefert `GET /quotes/{id}/stream`, das BFF leitet weiter, das Frontend nutzt `EventSource`. Beim Verbinden wird sofort der letzte bekannte Kurs gesendet. Nach einem Neustart von Quote ist der Speicher leer, bis der nächste Tick kommt. Das ist bewusst so (`ponytail:`-Kommentar), der Ausbaupfad wäre ein kompaktiertes `quotes.latest`.

## 8. Lernziel-Landkarte (Prüfinstrument für Phase 1)

Die Landkarte liegt als `docs/lernziel-landkarte.md` im Repo und wird nach jeder Runde aktualisiert. Jede Zeile nennt Konzept, Ort im Code, Test und Experiment. Das ist der Startbestand:

| # | Konzept | Ort | Experiment (Phase 2) |
|---|---|---|---|
| 1 | Value Object mit Validierung (ISIN, Währung) | Instrument.Domain | – |
| 2 | Aggregat mit Zustandsautomat und Vier-Augen-Regel | Instrument.Domain | – |
| 3 | Result-Pattern, Fehlerkatalog | Instrument.Domain | – |
| 4 | Domain Event → Integration Event | Instrument.Application/Infrastructure | – |
| 5 | Optimistic Concurrency | Instrument.Infrastructure | Zwei Freigaben gleichzeitig |
| 6 | Transactional Outbox und Dispatcher | Instrument.Infrastructure | Kafka stoppen, freigeben, Kafka starten |
| 7 | Idempotenter Producer | Outbox-Dispatcher | – |
| 8 | Key-Wahl und Partitionierung (InstrumentId statt ISIN) | Producer | ISIN-Wechsel mitten im Tick-Strom. Erwartung: Am Tick-Strom passiert nichts, und genau das ist die Lektion. Ein Schalter „Key nach ISIN“ ist nicht vorgesehen. |
| 9 | Consumer Groups: Arbeitsteilung vs. Broadcast | History vs. Quote | Zweite Instanz von History bzw. Quote starten |
| 10 | Manueller Offset-Commit, at-least-once | Consumer-Basis | Consumer nach der Verarbeitung und vor dem Commit abschießen |
| 11 | Inbox / idempotenter Consumer | History, Search | Duplikate einschalten |
| 12 | Sequenz-Deduplizierung | History | Duplikate einschalten |
| 13 | Event-Time und verspätete Ticks | History | Verspätete Ticks einschalten |
| 14 | Retry, DLT, Poison Message | Consumer-Basis | Poison Message senden |
| 15 | Event-carried State Transfer, kompaktiertes Topic | instruments.v1, Search | – |
| 16 | Projektion neu aufbauen (Replay) | Search, History | Search-DB löschen, neu aufbauen |
| 17 | Zeitgesteuerte Wirksamkeit, sicher bei mehreren Instanzen | Instrument-Job | Job doppelt laufen lassen |
| 18 | Verteilte Folge einer Domänenentscheidung (Split) | History, Simulator | History aus, Split wirksam, History an |
| 19 | Eventual Consistency in der UI | Frontend / BFF | Freigabe beobachten |
| 20 | SSE-Push, Fan-out | Quote, BFF | 3 Browser-Tabs, Quote neu starten |
| 21 | BFF: Cookie-Session, Tokens serverseitig, Rollen, Aggregation | BFF | – |
| 22 | Distributed Tracing über Kafka | alle | Beide Traces einer Kapitalmaßnahme über `corporateActionId` finden |
| 23 | Last und Consumer-Lag | History | Lastspitze, Lag beobachten |
| 24 | Bicep: Module, Parameter, Container Apps, Event Hubs, Postgres | `infra/` | what-if, Deploy, Abriss |
| 25 | Kafka vs. Event Hubs: Unterschiede | `docs/` + Bicep | Gleichen Code gegen Event Hubs laufen lassen |

## 9. Anforderungen aus Phase 2 an die Referenz

- **Ein Konzept, eine Stelle.** Jede Landkarten-Zeile zeigt auf eine klar umrissene Klasse, Methode oder Datei, die man herausschneiden kann, ohne den Rest umzubauen.
- **Tests pro Konzept**, die rot werden, wenn die Stelle durch `throw new NotImplementedException()` ersetzt wird:
  - Domäne: Unit-Tests.
  - Messaging und Persistenz: Integrationstests mit Testcontainers (Kafka, Postgres).
  - Architektur-Tests für die Schichtregeln (wie im Kurs).
- **Experimente sind ohne Code-Änderung auslösbar**, also über Störschalter, Aspire-Dashboard (Stop/Start) oder ein Admin-Kommando.
- **Git-Historie mit mindestens einem Commit pro Runde.** Sie dient als Rücksprungpunkt.

## 10. Runden für Phase 1

Nach jeder Runde: Landkarte aktualisieren, Hanna prüft, dann wird ergänzt oder es geht weiter.

1. **Walking Skeleton:** Aspire mit Kafka, Simulator mit festen Seed-Instrumenten, `ticks.v1`, Quote, BFF ohne Login, Nuxt-PDP mit Live-Ticker. *Landkarte 8, 9, 20, 22.*
2. **Stammdaten:** Instrument-Service mit Domäne, Outbox, `instruments.v1`. Search-Service mit Suche im Frontend. Der Simulator holt seine Instrumente aus dem Topic. *1, 3, 4, 6, 7, 10, 11, 15, 16.*
3. **History:** History-Service, Kerzen, Chart auf der PDP, Sequenz-Deduplizierung, verspätete Ticks, Retry/DLT, Neuaufbau, Störschalter. *12, 13, 14, 16, 23.*
4. **Kapitalmaßnahmen:** Aggregat, Vier-Augen-Prinzip, Keycloak mit Rollen `pfleger` und `freigeber`, BFF-Login, Erfassungs- und Freigabe-UI, Wirksamkeits-Job, Split-Folgen. *2, 5, 17, 18, 19, 21.*
5. **Azure:** Bicep (Container Apps Environment, Log Analytics, Container Registry, Event Hubs Namespace, Postgres Flexible Server, Keycloak als Container App, Budget-Alarm 20 €), Deploy- und Abriss-Skript, Doku der Unterschiede zu Event Hubs. *24, 25.*

## 11. Entscheidungen von Claude (bitte prüfen)

- InstrumentId statt ISIN als Kafka-Key.
- Den zunächst skizzierten Notifications-Service gibt es nicht mehr, weil er keinen Lernzweck hatte, der nicht schon abgedeckt ist.
- Suche und PDP sind öffentlich. Ein Login ist nur für die Pflege der Kapitalmaßnahmen nötig.
- Quote hält den letzten Kurs nur im Speicher.
- Split-Anpassung über Faktoren beim Lesen statt durch Umschreiben der Rohkerzen.
- JSON-Events ohne Schema Registry.
- Suche mit Postgres-Volltext statt OpenSearch.
- Chart-Bibliothek im Frontend: TradingView **lightweight-charts** (Apache-2.0).
- Keycloak läuft in Azure als Container App, nicht über Entra ID.
- Quote und Simulator bestehen aus einem einzigen Projekt statt vier Schichten.
- Zwei verknüpfte Traces statt eines einzigen durchgehenden Trace (Abschnitt 7).
- Der kurze Chart-Sprung beim Split wird akzeptiert und dokumentiert (Abschnitt 6).
- Das ISIN-Wechsel-Experiment zeigt, dass nichts kaputtgeht. Es gibt keinen Schalter, um den Fehler vorzuführen.
- Stichtag als Zeitpunkt mit Mindestvorlauf statt als Datum (angenommen, weil die Frage offen blieb).

## 12. Dem Builder überlassen

Projekt- und Ordnernamen, Ports, Anzahl der Retries und Backoff-Werte, Tick-Rate des Simulators, Seed-Instrumente, Kerzen-Tabellenschema, Gestaltung der Frontend-Seiten (schlicht und funktional), Prüfung der aktuellen Event-Hubs-Doku (welche Kafka-Features es im Standard-Tarif gibt) und konkrete Größen der übrigen Azure-Ressourcen innerhalb des Budgets, Testcontainers-Setup.
