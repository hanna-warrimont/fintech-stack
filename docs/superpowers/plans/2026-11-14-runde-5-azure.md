# Runde 5 – Azure mit Bicep und Event Hubs: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Das ganze Portal läuft zeitweise in Azure: handgeschriebenes Bicep, Event Hubs mit Kafka-Endpunkt statt Kafka, derselbe Client-Code, Budget-Alarm bei 20 €, Deploy und Abriss je mit einem Befehl. Die Unterschiede zwischen Kafka und Event Hubs sind dokumentiert.

**Architecture:**
- **Eine Ressourcengruppe für alles.** Der Abriss ist `az group delete`.
- **Container Apps Environment** (Consumption) mit Log Analytics und Aspire-Dashboard-Komponente. Darin laufen 8 Container Apps: instrument, search, history, quote, simulator, bff, frontend und keycloak.
- **Von außen erreichbar sind nur `frontend` und `keycloak`.** Der Browser muss sich bei Keycloak anmelden. Alle anderen Apps haben nur internen Ingress.
- **Event Hubs Standard** (1 TU) mit 5 Event Hubs: 3 Topics und 2 DLTs.
- **Postgres Flexible Server B1ms** mit den Datenbanken `instrumentdb`, `searchdb`, `historydb` und `keycloak`.
- **ACR Basic** für die Images.
- **Kafka-Zugang zentral:** Die Dienste bekommen ihn über eine neue Klasse `KafkaSettings`. Sie schaltet SASL nur ein, wenn ein Passwort konfiguriert ist, und überspringt das Anlegen von Topics, wenn `Kafka:ManageTopics=false` gilt.

**Tech Stack:**
- Bicep: Azure CLI mit eingebautem `az bicep`
- Container-Images:
  - .NET-Dienste mit `dotnet publish -t:PublishContainer` (ohne Dockerfile)
  - Frontend über ein Dockerfile mit `node:22-alpine`
  - Keycloak über ein Dockerfile mit `quay.io/keycloak/keycloak:26.6.4` (dieselbe Version wie lokal), mit eingebautem Realm-Import
- Ansonsten alles wie bisher.

**Spec:** `docs/superpowers/specs/2026-11-13-instrumenten-portal-design.md` (Abschnitte 3 „Azure“ und „Kostenrahmen“, 8 Zeilen 24 und 25, 10 Runde 5, 11 „Keycloak als Container App“, 12 „Event-Hubs-Doku prüfen“, „Größen der Azure-Ressourcen“)

## Global Constraints

- **Bicep von Hand**, nicht von Aspire oder azd erzeugt (Spec Abschnitt 3). Kein Terraform.
- **Kostenrahmen 20 € pro Monat:**
  - Event Hubs **Standard**, 1 TU, ohne Auto-Inflate
  - Postgres `Standard_B1ms` (Burstable), 32 GB
  - ACR Basic
  - Container Apps: Consumption, je 0,25 vCPU / 0,5 GiB; Keycloak 0,5 vCPU / 1 GiB; `minReplicas = maxReplicas = 1`
  - Log Analytics mit `dailyQuotaGb: 0.5`
- **Budget-Alarm:** `Microsoft.Consumption/budgets` auf Ebene der Ressourcengruppe, 20 €, monatlich, E-Mail als Deploy-Parameter. Benachrichtigt wird bei 80 % tatsächlich und 100 % prognostiziert. Ein Budget ist **kein Deckel**; das steht in der Doku.
- **Deploy nur zeitweise.** Abriss mit `infra/destroy.sh`. Ein Deploy braucht **die ausdrückliche Zustimmung der Nutzerin im Chat**, weil er Geld kostet und nach außen geht. Den Login (`az login`) macht die Nutzerin selbst.
- **Werkzeuge:** Die Azure CLI ist nicht installiert. Ohne sie lässt sich Bicep nicht kompilieren. Claude installiert nichts am System, ohne vorher zu fragen. Die Nutzerin installiert sie (`brew install azure-cli`) oder erlaubt es ausdrücklich.
- **Region:** Parameter `location`, Standard `germanywestcentral`.
- **Geheimnisse:**
  - Nur `@secure()`-Parameter und Container-App-Secrets, keine Geheimnisse in Outputs.
  - Folgende Werte erzeugt das Deploy-Skript per `openssl rand` und gibt sie am Ende einmal aus:
    - Postgres-Admin-Passwort
    - Client-Secret des BFF
    - Keycloak-Admin-Passwort
    - Passwörter der Testnutzer
  - **Öffentliche Testnutzer mit Passwort = Benutzername gibt es in Azure nicht.**
- **Event-Hubs-Fakten** (Microsoft Learn, Stand der Recherche 2026-11-14; in der Doku verlinken):
  - Standard: höchstens 7 Tage Aufbewahrung, 32 Partitionen pro Event Hub, 10 Event Hubs pro Namespace, 20 Consumer Groups pro Event Hub, 1000 Kafka-Consumer-Groups pro Namespace
  - Kompaktierung: bis 1 GB pro Partition
  - Durchsatz pro TU: 1 MB/s bzw. 1000 Events/s eingehend
  - Kafka-AdminClient für Topic-Verwaltung **nicht unterstützt**, Transaktionen und Exactly-once **nicht unterstützt**, Idempotent Producer **unterstützt**
  - librdkafka-Pflicht- und Empfehlungswerte:
    - `security.protocol=SASL_SSL`, `sasl.mechanism=PLAIN`, Benutzername `$ConnectionString`, Passwort = Connection-String, Port 9093
    - `socket.keepalive.enable=true`, `metadata.max.age.ms=180000`
    - Producer: `request.timeout.ms=60000`
    - Consumer: `session.timeout.ms=30000`
- **Spec Abschnitt 9:** Ein Konzept an einer Stelle. Bicep-Module bekommen die Landkarten-Zeile 24, die Doku die Zeile 25.

## Review Focus

1. **Dienst startet gegen Event Hubs ohne SASL oder mit falschem Port:** Er hängt still in Verbindungsfehlern. Erwartet wird: Ist `Kafka:SaslPassword` gesetzt, ist SASL immer aktiv. Test `Sasl_is_enabled_when_password_is_configured` (Task 1).
2. **Topic-Anlage per AdminClient gegen Event Hubs:** Der Dienst darf beim Start nicht abstürzen. Test `EnsureTopic_is_skipped_when_topics_are_managed_outside` (Task 1).
3. **Abriss lässt kostenpflichtige Ressourcen zurück:** Alles liegt in einer Ressourcengruppe (Lint-Regel und `az resource list` nach dem Abriss, Task 8). Das Budget wird mit der Gruppe gelöscht und steht in der Doku.
4. **Geheimnis landet in Outputs, Logs oder Klartext-Env:**
   - `az bicep lint` ohne Warnung `outputs-should-not-contain-secrets` und `secure-parameter-default`
   - Geheimnisse nur über `secretRef` (Task 3 und 5)
5. **Zu viel öffentlich erreichbar:** Nur `frontend` und `keycloak` sind extern. Postgres erlaubt nur Azure-Dienste. Prüfung über `az containerapp list --query "[].{n:name,ext:properties.configuration.ingress.external}"` (Task 8).

---

## File Structure

```
src/Messaging/KafkaSettings.cs                 Kafka-/Event-Hubs-Konfiguration an EINER Stelle (Landkarte 25)
src/{Instruments/…Api,Search/…,History/…,Simulator,Quote}/Program.cs   (KafkaSettings statt eigener Configs)
src/Bff/Program.cs                             (Bff:TrustAllProxies für Azure)
src/*/…csproj                                  (ContainerRepository, ContainerFamily)
src/Frontend/Dockerfile, .dockerignore
infra/
  main.bicep, main.bicepparam                  Einstieg, Parameter (Landkarte 24)
  modules/monitoring.bicep                     Log Analytics + Container Apps Environment + Aspire-Dashboard
  modules/registry.bicep                       ACR Basic
  modules/eventhubs.bicep                      Namespace Standard + 5 Event Hubs (Kafka-Endpunkt)
  modules/postgres.bicep                       Flexible Server B1ms + 4 Datenbanken + Firewall
  modules/app.bicep                            eine Container App (wiederverwendet für alle 8)
  modules/budget.bicep                         Budget 20 € mit E-Mail
  keycloak/Dockerfile, keycloak/portal-realm.json   Realm mit Platzhaltern (${env.…})
  deploy.sh, destroy.sh
docs/azure.md                                  Ablauf, Kosten, Abriss, Fehlerbilder
docs/kafka-vs-event-hubs.md                    Unterschiede (Landkarte 25)
tests/Portal.Tests/Messaging/KafkaSettingsTests.cs
```

---

### Task 1: Kafka-Konfiguration an einer Stelle, Event-Hubs-fähig

**Files:**
- Create: `src/Messaging/KafkaSettings.cs`
- Modify: die `Program.cs` von Instrument.Api, Search, History, Simulator und Quote; `OutboxDispatcher.ProducerConfig` entfällt
- Test: `tests/Portal.Tests/Messaging/KafkaSettingsTests.cs`

**Interfaces:**
- Produces: `public sealed class KafkaSettings(IConfiguration config)` mit:
  - `string BootstrapServers` (aus `ConnectionStrings:kafka`)
  - `ProducerConfig Producer()`: `Acks.All`, `EnableIdempotence = true`, plus SASL und Event-Hubs-Werte
  - `ConsumerConfig Consumer(string groupId, AutoOffsetReset reset)`: `EnableAutoCommit = false`
  - `Task EnsureTopicAsync(string topic, IReadOnlyDictionary<string,string>? config = null)`: ruft `Topics.EnsureTopicAsync(BootstrapServers, …)` auf. Bei `Kafka:ManageTopics == false` tut es nichts.
  - SASL wird nur aktiv, wenn `Kafka:SaslPassword` gesetzt ist. Dann gelten: `SecurityProtocol = SaslSsl`, `SaslMechanism = Plain`, `SaslUsername = "$ConnectionString"`, `SaslPassword`, `SocketKeepaliveEnable = true`, `MetadataMaxAgeMs = 180000`. Für Producer zusätzlich `RequestTimeoutMs = 60000`, für Consumer zusätzlich `SessionTimeoutMs = 30000`.
  - Die Testcontainers-Tests behalten `Topics.EnsureTopicAsync(bootstrap, …)` mit eigenen Configs. `KafkaSettings` ist nur die Verdrahtung für die Dienste.

- [ ] **Step 1: Failing Tests** (reine Unit-Tests mit `ConfigurationBuilder().AddInMemoryCollection(...)`)
  - `Plain_kafka_without_password`: `SecurityProtocol` ist `null`, und `BootstrapServers` kommt aus `ConnectionStrings:kafka`.
  - `Sasl_is_enabled_when_password_is_configured`: Producer und Consumer haben SASL_SSL, PLAIN, `$ConnectionString` und KeepAlive. Producer `RequestTimeoutMs == 60000`, Consumer `SessionTimeoutMs == 30000`.
  - `Producer_is_idempotent_with_acks_all`
  - `EnsureTopic_is_skipped_when_topics_are_managed_outside`: `ManageTopics=false` und Bootstrap `localhost:1` (nichts lauscht dort). Der Aufruf endet ohne Exception und in unter 1 s.

Run: `dotnet test tests/Portal.Tests --filter KafkaSettingsTests`
Expected: Build-Fehler.

- [ ] **Step 2: Implementieren und alle Dienste umstellen**

Jede Stelle, die bisher selbst `ProducerConfig`, `ConsumerConfig` oder `EnsureTopicAsync(kafka, …)` gebaut hat, nutzt jetzt eine `KafkaSettings`-Instanz. Die Group-IDs und Offset-Startpunkte bleiben **genau** wie bisher: `search`, `history`, `history-corporate-actions`, `simulator-…`, `simulator-ca-…`, `quote-…`. Kommentar an der Klasse: Hier und nur hier unterscheiden sich Kafka und Event Hubs für den Client-Code (Landkarte 25).

- [ ] **Step 3: Tests grün, Commit**

Run: `dotnet test tests/Portal.Tests`
Expected: alle bestanden.

```bash
git add -A && git commit -m "feat(messaging): central kafka settings with optional event hubs sasl"
```

---

### Task 2: Container-Images und Anpassungen für den Betrieb hinter Proxys

**Files:**
- Modify:
  - `src/Instruments/Portal.Instruments.Api/*.csproj`, `src/Search/Portal.Search/*.csproj`, `src/History/Portal.History/*.csproj`, `src/Simulator/*.csproj`, `src/Quote/*.csproj`, `src/Bff/*.csproj`: `<ContainerRepository>portal/<name></ContainerRepository>`, `<ContainerRuntimeIdentifier>linux-x64</ContainerRuntimeIdentifier>`
  - `src/Bff/Program.cs`
- Create: `src/Frontend/Dockerfile`, `src/Frontend/.dockerignore`, `infra/keycloak/Dockerfile`, `infra/keycloak/portal-realm.json`

**Interfaces:**
- Produces:
  - **Image-Namen:** `portal/instrument`, `portal/search`, `portal/history`, `portal/simulator`, `portal/quote`, `portal/bff`, `portal/frontend`, `portal/keycloak`. Tag ist der Git-Kurz-Hash.
  - **BFF `Bff:TrustAllProxies` (bool, Standard `false`):** Bei `true` werden `KnownIPNetworks` und `KnownProxies` geleert. In Azure kommt der Nuxt-Proxy aus dem internen Netz der Umgebung, und das BFF hat nur internen Ingress. Kommentar: Lokal vertraut das BFF nur Loopback.
  - **Frontend-Dockerfile:** zweistufig. `node:22-alpine` mit `npm ci` und `npm run build`, dann `node .output/server/index.mjs`, Port 3000, ohne Dev-Abhängigkeiten im Laufzeit-Image.
  - **Keycloak:**
    - Dockerfile: `FROM quay.io/keycloak/keycloak:26.6.4`, `COPY portal-realm.json /opt/keycloak/data/import/`, `ENV KC_DB=postgres KC_HEALTH_ENABLED=true`, `RUN /opt/keycloak/bin/kc.sh build`, Start mit `start --optimized --import-realm`
    - Realm-Datei mit Platzhaltern `${env.PORTAL_FRONTEND_URL}`, `${env.PORTAL_BFF_SECRET}`, `${env.PORTAL_PIA_PASSWORD}`, `${env.PORTAL_FRED_PASSWORD}`, `${env.PORTAL_FRIEDA_PASSWORD}`
    - Exakte Redirect-URI `${env.PORTAL_FRONTEND_URL}/api/signin-oidc` und Post-Logout `${env.PORTAL_FRONTEND_URL}/*`. Damit ist der Runde-4-Minor zur Wildcard `http://localhost*` für Azure erledigt.
    - Vor der Implementierung in der Keycloak-Doku zu „Importing a realm“ prüfen, ob Umgebungsvariablen-Platzhalter in Import-Dateien so unterstützt werden. Wenn nicht: Realm-Datei per `envsubst` im Deploy-Skript rendern und das Ergebnis als Ruling ins Ledger.

- [ ] **Step 1: BFF-Test** (`BffProxyTrustTests`): Eine Methode `public static void ConfigureForwardedHeaders(ForwardedHeadersOptions o, bool trustAllProxies)` in `BffEndpoints` oder einer eigenen kleinen Klasse.
  - `Trusts_only_loopback_by_default`: Die Listen sind unverändert.
  - `Trusts_all_proxies_when_configured`: `KnownIPNetworks` und `KnownProxies` sind leer.

Run: `dotnet test tests/Portal.Tests --filter BffProxyTrustTests`
Expected: Build-Fehler, dann nach der Implementierung bestanden.

- [ ] **Step 2: Images lokal bauen** (keine Registry, nur der lokale Docker-Daemon; die Basis-Images kommen von mcr.microsoft.com, Docker Hub und quay.io)

```bash
dotnet publish src/Bff -t:PublishContainer -c Release
docker build -t portal/frontend:local src/Frontend
docker build -t portal/keycloak:local infra/keycloak
docker images 'portal/*'
```
Expected: 3 Images sind vorhanden, alle `linux/amd64` (`docker image inspect … --format '{{.Architecture}}'`). Die restlichen 5 .NET-Images baut erst das Deploy-Skript, weil sie gleich konfiguriert sind.

- [ ] **Step 3: Commit**

```bash
git add -A && git commit -m "build: container images for all services, keycloak with realm template, bff proxy trust switch"
```

---

### Task 3: Bicep-Grundgerüst (Monitoring, Environment, Registry, Budget)

**Files:**
- Create: `infra/main.bicep`, `infra/main.bicepparam`, `infra/modules/{monitoring,registry,budget}.bicep`, `infra/bicepconfig.json`

**Interfaces:**
- Produces:
  - **`main.bicep`, `targetScope = 'resourceGroup'`. Parameter:**
    - `location` (Standard `resourceGroup().location`)
    - `prefix` (Standard `'portal'`), `budgetEmail` (string)
    - `budgetStartDate` (Standard `'${utcNow('yyyy-MM')}-01'`)
    - `deployApps` (bool, Standard `false`), `imageTag` (string)
    - `@secure()`: `postgresAdminPassword`, `bffClientSecret`, `keycloakAdminPassword`, `piaPassword`, `fredPassword`, `friedaPassword`
  - **Ressourcennamen:**
    - Allgemein: `'${prefix}-<art>-${uniqueString(resourceGroup().id)}'`, gekürzt, wo Azure Grenzen setzt
    - ACR ohne Bindestriche: `'${prefix}acr${uniqueString(...)}'`
  - `monitoring.bicep`:
    - Log Analytics: `PerGB2018`, `retentionInDays: 30`, `workspaceCapping.dailyQuotaGb: 0.5`
    - `Microsoft.App/managedEnvironments`: Consumption, `appLogsConfiguration` → Log Analytics
    - `Microsoft.App/managedEnvironments/dotNetComponents` mit `componentType: 'AspireDashboard'`; die API-Version aus der Bicep-Referenz prüfen
    - Outputs: `environmentId`, `defaultDomain`
  - `registry.bicep`: ACR Basic, `adminUserEnabled: false`. Die Apps ziehen per **system-assigned Managed Identity** mit Rolle `AcrPull`, die Rollenzuweisung steht in `app.bicep`. Output: `loginServer`.
  - `budget.bicep`:
    - `Microsoft.Consumption/budgets`, `amount: 20`, `timeGrain: 'Monthly'`, `category: 'Cost'`
    - `notifications`: `Actual_GreaterThan_80` und `Forecasted_GreaterThan_100`, jeweils an `contactEmails: [budgetEmail]`
    - Kommentar: Ein Budget alarmiert nur, es stoppt nichts.
  - `bicepconfig.json`: Linter mit `outputs-should-not-contain-secrets`, `secure-parameter-default` und `no-hardcoded-location` auf `error`.

- [ ] **Step 1: Kompilieren und linten**

Run: `az bicep build --file infra/main.bicep --stdout > /dev/null && az bicep lint --file infra/main.bicep`
Expected: keine Fehler, keine Warnungen. Ohne Azure CLI ist dieser Schritt blockiert, siehe Global Constraints.

- [ ] **Step 2: Landkarte Zeile 24 auf `teilweise`, Commit**

```bash
git add -A && git commit -m "infra: bicep skeleton with monitoring, aspire dashboard, registry and 20 euro budget"
```

---

### Task 4: Bicep – Event Hubs und Postgres

**Files:**
- Create: `infra/modules/eventhubs.bicep`, `infra/modules/postgres.bicep`
- Modify: `infra/main.bicep`

**Interfaces:**
- Produces:
  - `eventhubs.bicep`:
    - Namespace `sku: { name: 'Standard', tier: 'Standard', capacity: 1 }`, `kafkaEnabled` ist bei Standard automatisch an, `isAutoInflateEnabled: false`, `minimumTlsVersion: '1.2'`
    - Event Hubs (API `2024-01-01`), alle mit `partitionCount: 6`:

      | Event Hub | `retentionDescription` |
      |---|---|
      | `ticks.v1` | `Delete`, 24 h |
      | `instruments.v1` | `Compact`, `tombstoneRetentionTimeInHours: 24` |
      | `corporate-actions.v1` | `Delete`, 168 h (Maximum Standard) |
      | `ticks.v1.history.dlt` | `Delete`, 168 h |
      | `corporate-actions.v1.history-corporate-actions.dlt` | `Delete`, 168 h |

    - Autorisierungsregel `portal-apps` mit `Send` und `Listen` (nicht `Manage`)
    - Outputs: `bootstrapServers` (`'${ns.name}.servicebus.windows.net:9093'`) und `namespaceName`. Der Connection-String wird **nicht** ausgegeben, `main.bicep` holt ihn per `listKeys()` direkt als Secret.
  - `postgres.bicep`:
    - `Microsoft.DBforPostgreSQL/flexibleServers`, Version `17`, `Standard_B1ms` (Burstable), Storage 32 GB, Backup 7 Tage, kein HA
    - Passwort-Login `portaladmin`
    - Firewall-Regel `AllowAzureServices` (`0.0.0.0`–`0.0.0.0`). Kommentar: Für eine öffentliche Demo-Umgebung bewusst einfach; die Ausbaustufe wäre VNet-Integration.
    - Datenbanken `instrumentdb`, `searchdb`, `historydb`, `keycloak`
    - Output: `fqdn`

- [ ] **Step 1: Kompilieren und linten** (wie Task 3)
- [ ] **Step 2: Commit**

```bash
git add -A && git commit -m "infra: event hubs standard with kafka endpoint and postgres flexible server"
```

---

### Task 5: Bicep – Container Apps

**Files:**
- Create: `infra/modules/app.bicep`
- Modify: `infra/main.bicep` (Apps nur bei `deployApps`)

**Interfaces:**
- Produces: `app.bicep` mit diesen Parametern:
  - `name`, `environmentId`, `registryServer`, `image`
  - `external` (bool), `targetPort` (int)
  - `cpu` (string, z. B. `'0.25'`), `memory` (z. B. `'0.5Gi'`)
  - `env` (Array aus `{name, value}` oder `{name, secretRef}`), `secrets` (`@secure()` Objekt-Array)

  Die App bekommt eine system-assigned Identity, `AcrPull` auf der Registry, `minReplicas: 1` und `maxReplicas: 1` (Kommentar: Kafka-Consumer laufen dauerhaft; Skalierung auf 0 hier nicht sinnvoll). Ingress hat `transport: 'auto'`, `allowInsecure: false`. Output: `fqdn`, nur bei externem Ingress.

  **Verdrahtung in `main.bicep`** (die Namen der Apps entsprechen den Aspire-Ressourcennamen, damit `http://instrument` im Environment auflöst):

  | App | extern | Port | wichtige Env |
  |---|---|---|---|
  | instrument | nein | 8080 | `ConnectionStrings__instrumentdb` (secret), `ConnectionStrings__kafka`, `Kafka__SaslPassword` (secret), `Kafka__ManageTopics=false`, `services__keycloak__http__0=https://keycloak.<domain>` |
  | search | nein | 8080 | `ConnectionStrings__searchdb` (secret), Kafka wie oben |
  | history | nein | 8080 | `ConnectionStrings__historydb` (secret), Kafka |
  | quote | nein | 8080 | Kafka |
  | simulator | nein | 8080 | Kafka |
  | bff | nein | 8080 | `services__{instrument,search,quote,history}__http__0=http://<name>`, `services__keycloak__http__0`, `Bff__ClientSecret` (secret), `Bff__TrustAllProxies=true` |
  | frontend | **ja** | 3000 | `NUXT_BFF_URL=http://bff` |
  | keycloak | **ja** | 8080 | `KC_DB_URL`, `KC_DB_USERNAME`, `KC_DB_PASSWORD` (secret), `KC_HOSTNAME=https://keycloak.<domain>`, `KC_PROXY_HEADERS=xforwarded`, `KC_HTTP_ENABLED=true`, `KC_BOOTSTRAP_ADMIN_USERNAME=admin`, `KC_BOOTSTRAP_ADMIN_PASSWORD` (secret), `PORTAL_FRONTEND_URL=https://frontend.<domain>`, `PORTAL_BFF_SECRET`, die drei Testnutzer-Passwörter (alle secret); 0,5 vCPU / 1 GiB |

  Postgres-Connection-Strings haben die Form `Host=<fqdn>;Database=<db>;Username=portaladmin;Password=<secret>;Ssl Mode=Require`. Sie werden in `main.bicep` als Secret-Wert gebaut.

  Der Port 8080 der .NET-Images ist der Standard von `PublishContainer` (`ASPNETCORE_HTTP_PORTS=8080`).

- [ ] **Step 1: Kompilieren und linten** (wie Task 3, mit `deployApps=true` im Parameterfile-Beispiel)
- [ ] **Step 2: Commit**

```bash
git add -A && git commit -m "infra: container apps for all services with internal ingress except frontend and keycloak"
```

---

### Task 6: Deploy- und Abriss-Skript, Betriebsdoku

**Files:**
- Create: `infra/deploy.sh`, `infra/destroy.sh`, `docs/azure.md`

**Interfaces:**
- Produces:
  - **`infra/deploy.sh <resource-group> <budget-email>`** (`set -euo pipefail`):
    1. Prüft `az account show` und bricht mit dem Hinweis „erst az login“ ab.
    2. `az group create` (Region `germanywestcentral`).
    3. Erzeugt die Geheimnisse per `openssl rand -base64 24` bzw. ein Postgres-taugliches Passwort und schreibt sie in `infra/.secrets.<rg>.env` (Rechte 600, in `.gitignore`). Bei einem zweiten Deploy wird die Datei wiederverwendet.
    4. `az deployment group what-if` mit `deployApps=false` → **Bestätigung per `read -p`**.
    5. `az deployment group create` mit `deployApps=false`.
    6. `az acr login`. Dann die 6 .NET-Dienste per `dotnet publish -t:PublishContainer -p:ContainerRegistry=<acr> -p:ContainerImageTag=<tag>` bauen und `docker buildx build --platform linux/amd64 --push` für Frontend und Keycloak.
    7. `az deployment group create` mit `deployApps=true imageTag=<tag>`.
    8. Gibt die URLs von Frontend, Keycloak und Aspire-Dashboard aus, dazu die Testnutzer-Passwörter.
  - **`infra/destroy.sh <resource-group>`:** `az group delete --yes`, danach `az group exists` (Erwartung `false`). Kommentar: Budget, Images und Daten verschwinden mit der Gruppe.
  - **`docs/azure.md`:**
    - Voraussetzungen (Azure CLI, Docker, `az login`)
    - Ablauf mit beiden Skripten
    - Was wo läuft (Tabelle aus Task 5)
    - Kostenschätzung pro Stunde und pro Monat, mit Quelle Azure-Preisseiten und Abrufdatum; vor dem Schreiben per WebFetch prüfen:
      - Event Hubs Standard TU/h
      - Postgres B1ms/h und Storage
      - Container Apps vCPU-s, GiB-s und Freikontingent
      - ACR Basic pro Tag
      - Log-Analytics-Ingest
    - Aussage, wie viele Stunden im Monat ins Budget passen
    - Hinweis: Budget ≠ Deckel
    - Typische Fehlerbilder: Keycloak-Hostname, Event-Hubs-Auth, Throttling bei Lastspitze

- [ ] **Step 1:** `bash -n infra/deploy.sh infra/destroy.sh` (Syntax) und `shellcheck`, falls installiert, sonst überspringen und im Ledger vermerken.
- [ ] **Step 2: Commit**

```bash
git add -A && git commit -m "infra: deploy and teardown scripts, azure operations guide"
```

---

### Task 7: Kafka vs. Event Hubs – Doku und Landkarte

**Files:**
- Create: `docs/kafka-vs-event-hubs.md`
- Modify: `docs/lernziel-landkarte.md`

**Interfaces:**
- Produces: Doku mit je einer Zeile „Kafka lokal | Event Hubs Standard | Folge für das Portal | Stelle im Code/Bicep“. Diese Themen müssen vorkommen:
  - Verbindung und Authentifizierung (SASL PLAIN, `$ConnectionString`, Port 9093) → `KafkaSettings`
  - Topic-Verwaltung (AdminClient nicht unterstützt) → `Kafka:ManageTopics=false`, Topics in `eventhubs.bicep`
  - Aufbewahrung (lokal 7 Tage bzw. unbegrenzt für `corporate-actions.v1`, Standard höchstens 7 Tage)
    - Folge für History-Neuaufbau und Split-Faktoren: Nach 7 Tagen kann ein neuer History-Stand Faktoren verlieren.
    - Ausbaupfad: Premium (90 Tage) oder Faktoren aus dem Instrument-Service nachladen
  - Kompaktierung (Standard: ja, 1 GB pro Partition) → `instruments.v1`
  - Partitionen (32 bei Standard, nachträglich nicht änderbar) und Event Hubs pro Namespace (10, wir brauchen 5)
  - Durchsatz (1 TU = 1000 Events/s eingehend) → Lastspitze mit `rate=300` bei 4 Instrumenten ergibt 1200/s, Throttling erwartet
  - Idempotent Producer: ja; Transaktionen und Exactly-once: nein (das Portal nutzt keine – Outbox und Inbox statt Transaktionen)
  - Kompression (nur gzip) und Verbindungsabbrüche nach 240 s Leerlauf → KeepAlive und `metadata.max.age.ms`
  - Consumer Groups (Kafka-Groups werden automatisch angelegt, höchstens 1000 pro Namespace; Quote und Simulator erzeugen bei jedem Start neue Groups)
  - Quellen: die Microsoft-Learn-Seiten zu Quotas und Kafka-Client-Konfiguration, mit Abrufdatum

  Landkarte:
  - Zeile 24: `infra/main.bicep` + `infra/modules/*.bicep`, Test „`az bicep lint`“, Status `teilweise`
  - Zeile 25: `docs/kafka-vs-event-hubs.md` + `src/Messaging/KafkaSettings.cs`, Test `KafkaSettingsTests`, Status `teilweise`

- [ ] **Step 1: Commit**

```bash
git add -A && git commit -m "docs: kafka vs event hubs differences; learning map rows 24 and 25"
```

---

### Task 8: Abnahme – what-if, Deploy, Rauchtest, Abriss (nur mit Zustimmung)

- [ ] **Step 1:** `az bicep lint` und `what-if` (Lesezugriff, kostenlos). Voraussetzung ist, dass die Nutzerin `az login` gemacht hat.
- [ ] **Step 2: Zustimmung einholen** („Deploy nach `rg-…` in germanywestcentral, geschätzt X €/h, Abriss direkt nach dem Test?“). **Ohne ein klares Ja endet die Runde hier.** Die Landkarten-Zeilen 24 und 25 bleiben dann `teilweise`.
- [ ] **Step 3: Deploy** mit `infra/deploy.sh`, dann Rauchtest im Browser:
  - Suche, PDP mit Ticker und Chart
  - Login als Pia mit generiertem Passwort, Split erfassen; Freigabe durch Fred; wirksam → Chart angepasst
  - Aspire-Dashboard zeigt die Traces
  - Lastspitze `rate=300` → Throttling bzw. Lag beobachten. Der Simulator hat keinen öffentlichen Ingress, der Störschalter geht per `az containerapp exec -n simulator --command "curl -X POST localhost:8080/chaos/rate?ticksPerSecond=300"`. Gibt es im Image kein `curl`, wird das als Ruling ins Ledger notiert und der Schalter über die Replik-Konsole von bff ausgelöst (`http://simulator/chaos/...`).
  - Öffentlich sind nur Frontend und Keycloak (Review Focus 5).
- [ ] **Step 4: Abriss** mit `infra/destroy.sh`, danach `az group exists` → `false`. Die Kosten der Sitzung laut Cost Management in `docs/azure.md` nachtragen, sobald sie sichtbar sind; das kann bis zu 24 h dauern.
- [ ] **Step 5: Landkarte**
  - Zeile 24 bekommt als Beobachtung what-if, Deploy und Abriss, Zeile 25 „gleicher Code gegen Event Hubs“. Status `drin`.
  - Commit:

```bash
git add -A && git commit -m "docs: round 5 learning map after azure deployment"
```

---

## Nicht in dieser Runde

- CI/CD (Spec: Nicht-Ziel)
- VNet und Private Endpoints
- Key Vault (Container-App-Secrets reichen für eine zeitweise Demo)
- Token-Refresh
- Der Kafka-Fehlerschalter „Key nach ISIN“ (Spec: bewusst keiner)

Die aufgeschobenen Kleinigkeiten aus Runde 2 bis 4 bleiben offen. Ausnahme ist die Keycloak-Wildcard: Für Azure ist sie durch exakte URIs ersetzt, lokal bleibt sie.
