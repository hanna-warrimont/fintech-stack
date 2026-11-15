# Instrumenten-Portal

Referenz-App zum Lernen verteilter Systeme. Fachlich: Suche nach Finanzinstrumenten, eine Detailseite mit Live-Kurs und Chart, Kapitalmaßnahmen (Split, ISIN-Wechsel, Delisting) mit Vier-Augen-Freigabe. Technisch: .NET 10 mit Aspire, DDD im Instrument-Service, Kafka mit selbst geschriebener Outbox, Inbox und DLT, ein BFF mit Keycloak, Nuxt im Frontend, Bicep für Azure.

Jedes Konzept steht an einer Stelle im Code und ist durch einen Test abgesichert. Die Übersicht dazu ist die **[Lernziel-Landkarte](docs/lernziel-landkarte.md)**.

## Voraussetzungen

- .NET 10 SDK (Version in `global.json`)
- Docker (Kafka, Postgres, Keycloak und die Integrationstests laufen in Containern)
- Node 22 und npm (Frontend)

## Starten

```bash
cd src/Frontend && npm ci && cd ../..
scripts/run-apphost.sh
```

Das Skript baut und startet den Aspire-AppHost. Er bringt Kafka, Postgres, Keycloak, alle Dienste und das Frontend hoch.

- **Aspire-Dashboard:** http://localhost:15128. Dort stehen die URL des Frontends, Logs, Traces und der Zustand aller Dienste.
- **Anmelden** (nur für Kapitalmaßnahmen nötig, Suche und Detailseite sind öffentlich). Die Testnutzer gibt es nur lokal, Passwort jeweils gleich dem Namen:

  | Nutzer | Rolle | darf |
  |---|---|---|
  | `pia` | pfleger | Maßnahmen erfassen und einreichen |
  | `fred` | freigeber | freigeben, ablehnen, stornieren |
  | `frieda` | beide | alles, aber nie die eigene Maßnahme freigeben (Vier-Augen) |

- **Experimente** (Landkarte, Spalte „Experiment“): Umgebungsvariablen gehen ans Skript, z. B. `HistoryReplicas=2 scripts/run-apphost.sh`. Die Störschalter des Simulators (Duplikate, verspätete Ticks, Poison, Last) sind HTTP-Endpunkte unter `/chaos` am Simulator, die URL steht im Dashboard.

## Testen

```bash
dotnet test tests/Portal.Tests
```

Braucht Docker (Testcontainers für Kafka und Postgres). Ohne Docker laufen nur die Unit-Tests, die Integrationstests scheitern beim Start des Containers.

```bash
cd src/Frontend && npm run lint && npm run typecheck && npm test
```

Die CI ([.github/workflows/ci.yml](.github/workflows/ci.yml)) führt bei jedem PR beides aus, dazu den Bicep-Lint.

## Aufbau

| Ordner | Inhalt |
|---|---|
| `src/Instruments/` | Instrument-Service, DDD-Kern in vier Schichten (Domain, Application, Infrastructure, Api): Stammdaten, Kapitalmaßnahmen, Outbox, Wirksamkeits-Job |
| `src/Search/`, `src/History/` | Projektionen aus Kafka: Volltextsuche, 1-Minuten-Kerzen mit Split-Anpassung |
| `src/Quote/`, `src/Simulator/` | Live-Kurse per SSE, Kurs-Simulator mit Störschaltern |
| `src/Bff/` | Backend for Frontend: OIDC mit Keycloak, Cookie-Session, YARP |
| `src/Messaging/` | Gemeinsamer Kafka-Code: Topics, Verträge, DLT, Tracing, Aufräum-Job |
| `src/ServiceDefaults/`, `src/AppHost/` | Aspire: Telemetrie, Health Checks, lokales Gesamtsystem |
| `src/Frontend/` | Nuxt 4 |
| `tests/Portal.Tests/` | Alle Tests (Unit, Integration mit Testcontainers, Architektur) |
| `infra/` | Bicep für Azure, Deploy- und Abriss-Skript |

## Weiterlesen

- [Design-Spec](docs/superpowers/specs/2026-11-13-instrumenten-portal-design.md): Ziele, Architektur, Entscheidungen
- [Lernziel-Landkarte](docs/lernziel-landkarte.md): Konzept → Code → Test → Experiment
- [Azure](docs/azure.md): Deploy, Kosten, Abriss
- [Kafka vs. Event Hubs](docs/kafka-vs-event-hubs.md)
- [Architektur- und Code-Review](docs/reviews/2026-11-15-architektur-und-code-review.md)
