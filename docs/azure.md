# Azure: Deploy, Betrieb, Abriss

Das Portal läuft in Azure **nur zeitweise**: deployen, ausprobieren, abreißen. Alles liegt in **einer** Ressourcengruppe. Der Abriss löscht sie komplett, mit Datenbanken, Images und Budget.

## Voraussetzungen

- Azure CLI (`brew install azure-cli`). Bicep bringt sie mit (`az bicep`).
- Docker mit `buildx`, für das Frontend- und das Keycloak-Image.
- .NET 10 SDK, für die .NET-Images per `dotnet publish -t:PublishContainer`.
- `az login`. Das machst du selbst.

## Ablauf

```bash
infra/deploy.sh rg-instrumenten-portal du@example.com
```

1. Legt die Ressourcengruppe in `germanywestcentral` an.
2. Erzeugt beim ersten Mal die Geheimnisse und legt sie in `infra/.secrets.<rg>.env` ab (in `.gitignore`, nur für dich lesbar):
   - Postgres-Admin-Passwort und je ein Passwort für die Datenbank-Rollen `instrument`, `search`, `history` und `keycloak`
   - BFF-Client-Secret
   - Keycloak-Admin-Passwort
   - Passwörter der Testnutzer `pia`, `fred` und `frieda`
   Fehlen in einer älteren Datei Einträge, ergänzt das Skript sie.
3. **what-if** für die Infrastruktur, danach eine Rückfrage. Erst nach `ja` wird angelegt.
4. Startet den Container-Apps-Job `db-roles` und wartet auf sein Ende. Er legt mit `infra/postgres/roles.sql` pro Dienst eine eigene Postgres-Rolle an, die nur ihre eigene Datenbank nutzen darf. Bicep kann keine Rollen in Postgres anlegen, deshalb läuft `psql` in Azure (dort lässt die Firewall den Job durch).
5. Baut die 8 Images (Tag = Git-Kurz-Hash) und schiebt sie in die Container Registry.
6. Legt die 8 Container Apps an und gibt die URLs aus.

```bash
infra/destroy.sh rg-instrumenten-portal
```

Danach `az group exists` → `false`. **Ein Budget ist kein Deckel.** Es schickt nur eine E-Mail. Vor Kosten schützt allein der Abriss.

## Was wo läuft

| App | erreichbar | Größe | Aufgabe |
|---|---|---|---|
| frontend | **öffentlich** (HTTPS) | 0,25 vCPU / 0,5 GiB | Nuxt, reicht `/api` an das BFF weiter |
| keycloak | **öffentlich** (HTTPS) | 0,5 vCPU / 1 GiB | Login-Seite, Realm `portal` aus `infra/keycloak/portal-realm.json` |
| bff | intern | 0,25 / 0,5 | OIDC, Cookie-Session, Token-Weitergabe |
| instrument, search, history, quote, simulator | intern | je 0,25 / 0,5 | wie lokal |

Dazu kommen:
- **Event Hubs Standard** (1 TU) mit `ticks.v1`, `instruments.v1` (kompaktiert), `corporate-actions.v1` und den 2 DLTs
- **Postgres Flexible Server B1ms** mit `instrumentdb`, `searchdb`, `historydb` und `keycloak`. Jede Datenbank hat ihre eigene Login-Rolle, nur der Job `db-roles` nutzt den Admin. Noch nicht in Azure geprüft: wem die per Bicep angelegten Datenbanken gehören. `roles.sql` braucht Owner-Rechte über den Admin, der Test `DatabaseRolesTests` bildet genau das nach.
- **Container Registry Basic**
- **Log Analytics** (höchstens 0,5 GB pro Tag)
- **Aspire-Dashboard** als Komponente der Container-Apps-Umgebung

Die Apps finden sich im Environment über ihren Namen (`http://instrument` usw.), wie die Aspire-Service-Discovery lokal. Die Störschalter des Simulators sind nicht öffentlich erreichbar:

```bash
az containerapp exec -g <rg> -n bff --command "curl -X POST http://simulator/chaos/rate?ticksPerSecond=300"
```

## Kosten (Schätzung, Stand 2026-11-14, `germanywestcentral`, EUR)

Quelle: [Azure Retail Prices API](https://prices.azure.com/api/retail/prices), abgerufen am 2026-11-14. Bei Container Apps und Postgres lieferte die API für die Region keine bezahlte Stufe. Dort stehen die öffentlichen Listenpreise. **Vor dem ersten Deploy im [Preisrechner](https://azure.microsoft.com/pricing/calculator/) prüfen.**

| Posten | Preis | pro Stunde Deploy |
|---|---|---|
| Event Hubs Standard: Throughput Unit | 0,0269 €/h | 0,027 € |
| Event Hubs Standard: **Kafka-Endpunkt** (eigener Zähler!) | 0,0808 €/h | 0,081 € |
| Event Hubs Ingress | 0,0251 € je 1 Mio. Events | < 0,001 € (8 Ticks/s) |
| Container Registry Basic | 0,1496 €/Tag | 0,006 € |
| Postgres B1ms (Listenpreis, prüfen) | ca. 0,02 €/h + 32 GB Storage ca. 4 €/Monat | ca. 0,026 € |
| Container Apps: 2,25 vCPU + 4,5 GiB (Listenpreis ca. 0,000024 $/vCPU-s, 0,000003 $/GiB-s) | die ersten ca. 22 h pro Monat im Freikontingent (180.000 vCPU-s, 360.000 GiB-s) | 0 € im Freikontingent, danach ca. 0,22 € |
| Log Analytics | 2,68 €/GB (erste 5 GB/Monat frei) | ≈ 0 € |
| **Summe** | | **ca. 0,14 €/h im ACA-Freikontingent, ca. 0,36 €/h danach** |

**Faustregel:** 20 € reichen für etwa 50 bis 100 Stunden Deploy im Monat. Ein vergessener Abriss kostet etwa 100 bis 250 € im Monat. Genau dafür ist der Budget-Alarm da.

## Typische Fehlerbilder

| Symptom | Ursache | Abhilfe |
|---|---|---|
| Dienste hängen in „Broker transport failure“ | SASL fehlt oder falscher Port | `Kafka__SaslPassword` gesetzt? Bootstrap `<ns>.servicebus.windows.net:9093`? (`KafkaSettings`) |
| Dienst stürzt beim Start in `EnsureTopicAsync` ab | AdminClient gegen Event Hubs | `Kafka__ManageTopics=false` gesetzt? |
| Login: „Invalid parameter: redirect_uri“ | Frontend-URL im Realm stimmt nicht | `PORTAL_FRONTEND_URL` an der Keycloak-App = `https://frontend.<domain>`. Der Realm-Import wirkt nur beim **ersten** Start, danach im Admin-Konsolen-Client ändern. |
| Instrument-API antwortet 401 trotz Login | Issuer im Token ≠ Authority | `KC_HOSTNAME` und `services__keycloak__http__0` müssen dieselbe URL sein |
| Lastspitze: Lag wächst, Producer-Fehler „throttled“ | 1 TU = 1000 Events/s eingehend | Erwartet (siehe `docs/kafka-vs-event-hubs.md`). Rate wieder senken. |
