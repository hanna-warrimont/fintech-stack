# Runde 6 – Review-Fixes: Plan

**Ziel:** Die offenen Review-Issues (Label `review-2026-11-15`) in fünf Paketen abarbeiten. Ein erstes Azure-Deploy prüft die bisher ungeprüften Annahmen. Was nur „optional“ oder „erst bei Bedarf“ ist, wird bewusst geparkt statt gebaut.

**Quellen:** [Review](../../reviews/2026-11-15-architektur-und-code-review.md) · [Offene Punkte](../../reviews/offene-punkte.md) · [Retro](../../reviews/2026-11-17-retro.md)

**Stand (2026-11-17):** #2 (Branch-Schutz mit 3 Pflicht-Checks) und #3 (Node 24) sind erledigt. #13 liegt als PR #27 vor.

## Regeln für alle Pakete (aus der Retro)

- **Definition of Done:** Docker an, `dotnet test` komplett grün (auch Testcontainers), Frontend mit `lint`, `typecheck` und `test`. Die CI ist Pflicht, erzwingt das also für `main`.
- **Ein PR pro Paket.** Bei Paket 3 ist ein PR pro Issue erlaubt, weil die Issues in Bicep, Skript und Frontend liegen.
- **Jeder Fix mit genau dem Test aus dem „Fertig, wenn“ des Issues.** Bicep und Doku sind ausgenommen, dafür reichen Lint bzw. what-if.
- **Das Deploy (Paket 4) kostet Geld und geht nach außen.** Es braucht im Chat deine ausdrückliche Zustimmung. `az login` machst du selbst.

## Paket 0 – Offenes abschließen

| Issue | Was | Fertig, wenn |
|---|---|---|
| #13 | PR #27 (ProblemDetails über `UseExceptionHandler` in ServiceDefaults) prüfen und mergen | PR gemergt, Issue geschlossen |
| – | PR #28 (Retro und dieser Plan) mergen | gemergt |

## Paket 1 – Backend-Robustheit (klein, unabhängig)

| Issue | Was | Test |
|---|---|---|
| #12 | `QuoteHub.Subscribe`: leeren äußeren Eintrag im `finally` entfernen; im BFF `AddRateLimiter` mit `ConcurrencyLimiter` für `/api/quotes` | Viele Abos mit zufälligen IDs hinterlassen nach dem Schließen keine Einträge |
| #14 | `SeedData`: Bei `DuplicateIsin` weitermachen statt zu werfen | Zwei parallele Seeds enden mit genau 4 Instrumenten |
| #15 | `TimeProvider` in `InstrumentDbContext` | Die Outbox-Zeit lässt sich über `FixedTime` steuern |

## Paket 2 – Frontend (klein, unabhängig)

| Issue | Was | Test |
|---|---|---|
| #20 | Detailseite: Bei 404 `createError({ statusCode: 404 })`, bei 502/504 eine eigene Meldung | `mountSuspended` und `registerEndpoint` mit 404 und 502 |
| #21 | `useUser`: einmal `useFetch('/api/bff/user', { key: 'user' })`, Kommentar korrigieren | Ein Request pro Seitenaufruf, kein „Anmelden“-Flackern |
| #22 | Kapitalmaßnahmen-Suche: `useFetch` mit entprellter, reaktiver Query (wie auf der Startseite) | Schnelles Tippen zeigt immer die Treffer zum letzten Begriff |
| #23 | Proxy-Route: `runtimeConfig.bffUrl` statt `process.env` | Kein `process.env` mehr in der Route, Typecheck grün |

## Paket 3 – Azure härten, vor dem Deploy

Diese Punkte ändern, was deployt wird. Paket 4 soll sie gleich mitprüfen.

| Issue | Was | Prüfung |
|---|---|---|
| #8 | `/chaos` nur in Development mappen; `docs/azure.md` anpassen | Test: in Production 404 |
| #9 | `peerTrafficConfiguration.encryption.enabled: true` an der Container-Apps-Umgebung | Bicep lint, in Paket 4 erreichbar über `http://<name>` |
| #10 | Security-Header über `routeRules` (CSP, `frame-ancestors`, HSTS, `Referrer-Policy`) | Header kommen an, Chart und SSE funktionieren lokal |
| #11 | `deploy.sh` nutzt `--parameters main.bicepparam`, keine Geheimnisse mehr als `-p` | Shellcheck, dann Paket 4 |
| #17 | Eine Postgres-Hauptversion überall (AppHost, Testcontainers, Bicep, Job-Image) | Alle vier Stellen gleich, Tests grün |

## Paket 4 – Erstes Azure-Deploy (braucht deine Zustimmung)

| Issue | Was | Fertig, wenn |
|---|---|---|
| #4 | what-if, Deploy und die vier Annahmen prüfen: `db-roles`-Job, Startup-Probe reicht für Migrationen, `sub`-Claim vom Azure-Keycloak, Search und History gegen Event Hubs. Danach `destroy.sh` | Landkarte 24/25 auf „drin“ oder Abweichungen dokumentiert |
| #5 | Lag-Experiment (300 Ticks/s) lokal nachmessen, optional zusätzlich gegen Event Hubs, solange das Deploy steht | Landkarte 23 mit alten und neuen Zahlen |

Abweichungen aus dem Deploy werden zu neuen Issues und nicht in diesem Paket gefixt.

## Paket 5 – Geparkt (erst bei Auslöser)

| Issue | Auslöser zum Anfangen |
|---|---|
| #6 Postgres privat (VNet, groß) | Wenn Azure länger als zeitweise laufen soll. Erfordert die Neuanlage des Servers |
| #7 Outbox schneller senden | Wenn sich die Outbox messbar staut |
| #25 Split-Faktoren nachladen | Wenn Azure dauerhaft betrieben wird (gleicher Auslöser wie #6) |
| #16 Options-Pattern | Beim nächsten neuen Konfigurationswert |
| #18 xUnit v3 / Testaufteilung | Vor Phase 2 (Lückentext) |
| #19 OpenAPI | Wenn ein Client generiert oder die API dokumentiert werden soll |
| #24 Landkarte „zwei Aggregate in einer Transaktion“ | Nur Doku, kann jederzeit nebenbei gemacht werden |

## Reihenfolge

1. **Paket 0**
2. **Pakete 1, 2 und 3 parallel**, weil sie sich keine Dateien teilen. Einzige Berührung: #10 und #23 ändern beide `nuxt.config.ts`, also nacheinander.
3. **Paket 4** sobald Paket 3 gemergt ist. Die Retro empfiehlt, früh zu deployen. Deshalb kommt Paket 4 nicht hinter die Pakete 1 und 2.
4. **Paket 5** nur bei Auslöser. Die Issues bleiben offen und bekommen ein Label `geparkt`.
