#!/usr/bin/env bash
# Instrumenten-Portal nach Azure – zeitweise. Abriss: infra/destroy.sh <resource-group>
# Nutzung: infra/deploy.sh <resource-group> <budget-email>
set -euo pipefail

RG=${1:?Ressourcengruppe fehlt}
BUDGET_EMAIL=${2:?E-Mail für den Budget-Alarm fehlt}
LOCATION=germanywestcentral
ROOT=$(cd "$(dirname "$0")/.." && pwd)
SECRETS="$ROOT/infra/.secrets.$RG.env"
TAG=$(git -C "$ROOT" rev-parse --short HEAD)

az account show --query name -o tsv >/dev/null || { echo "Erst anmelden: az login" >&2; exit 1; }

# Geheimnisse einmal erzeugen und für weitere Deploys wiederverwenden (Datei ist in .gitignore, nur für dich lesbar).
# Fehlende Einträge (z. B. nach einem Update dieses Skripts) werden ergänzt, vorhandene bleiben.
umask 077
pw() { openssl rand -base64 24 | tr -d '/+=' ; }
touch "$SECRETS"
for name in POSTGRES_ADMIN_PASSWORD INSTRUMENT_DB_PASSWORD SEARCH_DB_PASSWORD HISTORY_DB_PASSWORD KEYCLOAK_DB_PASSWORD \
            BFF_CLIENT_SECRET KEYCLOAK_ADMIN_PASSWORD PIA_PASSWORD FRED_PASSWORD FRIEDA_PASSWORD; do
  grep -q "^$name=" "$SECRETS" && continue
  # Azure verlangt fürs Admin-Passwort 3 von 4 Zeichenklassen – das Präfix garantiert sie.
  [[ $name == POSTGRES_ADMIN_PASSWORD ]] && value="Pg1-$(pw)" || value=$(pw)
  echo "$name=$value" >> "$SECRETS"
done
set -a; source "$SECRETS"; set +a

deploy() { # $1 = deployApps (true/false), weitere Argumente gehen an az durch
  local apps=$1; shift
  az deployment group "$@" -g "$RG" -f "$ROOT/infra/main.bicep" -p \
    budgetEmail="$BUDGET_EMAIL" deployApps="$apps" imageTag="$TAG" \
    postgresAdminPassword="$POSTGRES_ADMIN_PASSWORD" instrumentDbPassword="$INSTRUMENT_DB_PASSWORD" \
    searchDbPassword="$SEARCH_DB_PASSWORD" historyDbPassword="$HISTORY_DB_PASSWORD" \
    keycloakDbPassword="$KEYCLOAK_DB_PASSWORD" bffClientSecret="$BFF_CLIENT_SECRET" \
    keycloakAdminPassword="$KEYCLOAK_ADMIN_PASSWORD" piaPassword="$PIA_PASSWORD" \
    fredPassword="$FRED_PASSWORD" friedaPassword="$FRIEDA_PASSWORD"
}

az group create -n "$RG" -l "$LOCATION" -o none

echo "== 1/4 Infrastruktur: what-if =="
deploy false what-if
read -r -p "Infrastruktur so anlegen? Das kostet Geld, bis destroy.sh läuft. [ja/nein] " answer
[[ "$answer" == "ja" ]] || { echo "Abgebrochen."; exit 1; }
deploy false create -o none
ACR=$(az deployment group show -g "$RG" -n main --query properties.outputs.registryName.value -o tsv)
REGISTRY=$(az deployment group show -g "$RG" -n main --query properties.outputs.registryServer.value -o tsv)

echo "== 2/4 Datenbank-Rollen pro Dienst (Job db-roles, infra/postgres/roles.sql) =="
EXECUTION=$(az containerapp job start -g "$RG" -n db-roles --query name -o tsv)
while :; do
  STATUS=$(az containerapp job execution show -g "$RG" -n db-roles --job-execution-name "$EXECUTION" --query properties.status -o tsv)
  case "$STATUS" in
    Succeeded) break ;;
    Failed|Stopped|Degraded) echo "Job db-roles: $STATUS. Logs im Portal unter Container Apps Jobs → db-roles → Ausführungsverlauf." >&2; exit 1 ;;
  esac
  sleep 5
done

echo "== 3/4 Images bauen und nach $REGISTRY schieben (Tag $TAG) =="
az acr login -n "$ACR"
for project in Instruments/Portal.Instruments.Api Search/Portal.Search History/Portal.History Simulator Quote Bff; do
  dotnet publish "$ROOT/src/$project" -t:PublishContainer -c Release \
    -p:ContainerRegistry="$REGISTRY" -p:ContainerImageTag="$TAG"
done
docker buildx build --platform linux/amd64 -t "$REGISTRY/portal/frontend:$TAG" --push "$ROOT/src/Frontend"
docker buildx build --platform linux/amd64 -t "$REGISTRY/portal/keycloak:$TAG" --push "$ROOT/infra/keycloak"

echo "== 4/4 Apps =="
deploy true create -o none

FRONTEND=$(az deployment group show -g "$RG" -n main --query properties.outputs.frontendUrl.value -o tsv)
KEYCLOAK=$(az deployment group show -g "$RG" -n main --query properties.outputs.keycloakUrl.value -o tsv)
cat <<DONE

Fertig.
  Portal:    $FRONTEND
  Keycloak:  $KEYCLOAK  (Admin: admin, Passwort in $SECRETS)
  Dashboard: az containerapp env dotnet-component show -g $RG --environment <env> -n aspire-dashboard
  Testnutzer: pia / fred / frieda – Passwörter in $SECRETS

Abriss nicht vergessen: infra/destroy.sh $RG
DONE
