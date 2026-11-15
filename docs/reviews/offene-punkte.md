# Offene Punkte aus dem Review

Stand: 2026-11-15, nach Umsetzung von A1, A2, O1–O3, D1–D5, S1 (teilweise), S2, N1, N2, F1, R1, R2.
Quelle: [Architektur- und Code-Review](2026-11-15-architektur-und-code-review.md) plus Punkte, die bei der Umsetzung aufgefallen sind.

Jede Zeile hat einen Issue-Entwurf in [`issues/`](issues/). Angelegt am 2026-11-15 mit `scripts/create-review-issues.sh --apply` (Label `review-2026-11-15`); die Issues sind ab jetzt die Quelle, die Entwürfe nur noch Archiv.

| # | Issue | Titel | Priorität | Bereich | Typ | Review | Aufwand | Entwurf |
|---|---|---|---|---|---|---|---|---|
| 01 | [#2](https://github.com/hanna-warrimont/fintech-stack/issues/2) | CI als Pflichtprüfung für main | hoch | ci | enhancement | – | klein | [Entwurf](issues/01.md) |
| 02 | [#3](https://github.com/hanna-warrimont/fintech-stack/issues/3) | Node 24 überall, damit die package-lock.json stabil bleibt | hoch | frontend | bug | F6 | klein | [Entwurf](issues/02.md) |
| 03 | [#4](https://github.com/hanna-warrimont/fintech-stack/issues/4) | Azure erstmals deployen und Annahmen prüfen | mittel | infra | enhancement | 24/25 | mittel | [Entwurf](issues/03.md) |
| 04 | [#5](https://github.com/hanna-warrimont/fintech-stack/issues/5) | Lag-Experiment (Landkarte 23) nach D1 nachmessen | mittel | docs | documentation | – | klein | [Entwurf](issues/04.md) |
| 05 | [#6](https://github.com/hanna-warrimont/fintech-stack/issues/6) | Postgres nur privat erreichbar (Firewall 0.0.0.0 schließen) | mittel | sicherheit | enhancement | S1 | groß | [Entwurf](issues/05.md) |
| 06 | [#7](https://github.com/hanna-warrimont/fintech-stack/issues/7) | Outbox-Dispatcher: schneller senden, falls sich die Outbox staut | niedrig | backend | enhancement | D4 | mittel | [Entwurf](issues/06.md) |
| 07 | [#8](https://github.com/hanna-warrimont/fintech-stack/issues/8) | Störschalter des Simulators nur in Development | mittel | sicherheit | enhancement | S3 | klein | [Entwurf](issues/07.md) |
| 08 | [#9](https://github.com/hanna-warrimont/fintech-stack/issues/9) | Interner Verkehr in Azure verschlüsselt | mittel | sicherheit | enhancement | S4 | klein | [Entwurf](issues/08.md) |
| 09 | [#10](https://github.com/hanna-warrimont/fintech-stack/issues/10) | Security-Header im Frontend | niedrig | sicherheit | enhancement | S5 | klein | [Entwurf](issues/09.md) |
| 10 | [#11](https://github.com/hanna-warrimont/fintech-stack/issues/11) | deploy.sh: Geheimnisse nicht auf der Kommandozeile | niedrig | infra | enhancement | S6 | klein | [Entwurf](issues/10.md) |
| 11 | [#12](https://github.com/hanna-warrimont/fintech-stack/issues/12) | QuoteHub: Speicher wächst bei beliebigen Instrument-IDs | mittel | backend | bug | O5 | klein | [Entwurf](issues/11.md) |
| 12 | [#13](https://github.com/hanna-warrimont/fintech-stack/issues/13) | Unbehandelte Fehler als ProblemDetails statt leerem 500 | hoch | backend | bug | O4 | klein | [Entwurf](issues/12.md) |
| 13 | [#14](https://github.com/hanna-warrimont/fintech-stack/issues/14) | Seed bei mehreren Instrument-Instanzen | niedrig | backend | bug | D6 | klein | [Entwurf](issues/13.md) |
| 14 | [#15](https://github.com/hanna-warrimont/fintech-stack/issues/15) | Outbox-Zeit über TimeProvider | niedrig | backend | enhancement | A5 | klein | [Entwurf](issues/14.md) |
| 15 | [#16](https://github.com/hanna-warrimont/fintech-stack/issues/16) | Options-Pattern statt GetValue | niedrig | backend | enhancement | A4 | klein | [Entwurf](issues/15.md) |
| 16 | [#17](https://github.com/hanna-warrimont/fintech-stack/issues/17) | Eine Postgres-Version überall | niedrig | infra | enhancement | N4 | klein | [Entwurf](issues/16.md) |
| 17 | [#18](https://github.com/hanna-warrimont/fintech-stack/issues/18) | xUnit v3 und Testaufteilung prüfen | niedrig | tests | enhancement | N3 | mittel | [Entwurf](issues/17.md) |
| 18 | [#19](https://github.com/hanna-warrimont/fintech-stack/issues/19) | OpenAPI-Beschreibung für die APIs | niedrig | backend | enhancement | N5 | klein | [Entwurf](issues/18.md) |
| 19 | [#20](https://github.com/hanna-warrimont/fintech-stack/issues/20) | Detailseite: 404 als echter Status, andere Fehler unterscheiden | mittel | frontend | bug | F3 | klein | [Entwurf](issues/19.md) |
| 20 | [#21](https://github.com/hanna-warrimont/fintech-stack/issues/21) | useUser: einmal laden, kein Flackern beim SSR | mittel | frontend | bug | F4 | klein | [Entwurf](issues/20.md) |
| 21 | [#22](https://github.com/hanna-warrimont/fintech-stack/issues/22) | Instrumentsuche bei Kapitalmaßnahmen: Debounce und Reihenfolge | niedrig | frontend | bug | F5 | klein | [Entwurf](issues/21.md) |
| 22 | [#23](https://github.com/hanna-warrimont/fintech-stack/issues/23) | Proxy-Route über runtimeConfig | niedrig | frontend | enhancement | F2 | klein | [Entwurf](issues/22.md) |
| 23 | [#24](https://github.com/hanna-warrimont/fintech-stack/issues/24) | Landkarte: „zwei Aggregate in einer Transaktion“ als Abwägung | niedrig | docs | documentation | A3 | klein | [Entwurf](issues/23.md) |
| 24 | [#25](https://github.com/hanna-warrimont/fintech-stack/issues/25) | Split-Faktoren aus dem Instrument-Service nachladen | niedrig | backend | enhancement | D5 | mittel | [Entwurf](issues/24.md) |

**Labels**, die das Skript bei Bedarf anlegt: `prio: hoch`, `prio: mittel`, `prio: niedrig`, `bereich: backend|frontend|infra|sicherheit|tests|docs|ci`, `review-2026-11-15`. Dazu die vorhandenen GitHub-Labels `bug`, `enhancement`, `documentation`.

**Vorgeschlagene Reihenfolge:** #2 (nach dem Merge von PR #1) → #3 → #13 → #20 → #21 → #12 → #8, #9 → #4 → Rest nach Bedarf.
