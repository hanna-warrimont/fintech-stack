#!/bin/sh
# Startet den AppHost im http-Profil ohne Aspire-CLI (die CLI würde Dev-Zertifikate im Keychain trusten wollen).
# Umgebungsvariablen reichen durch – so laufen die Experimente der Lernziel-Landkarte, z. B.:
#   HistoryReplicas=2 scripts/run-apphost.sh          (Nr. 9: zwei History-Instanzen teilen sich die Partitionen)
#   InstrumentJobInstances=2 scripts/run-apphost.sh   (Nr. 17: Wirksamkeits-Job zweimal, SKIP LOCKED)
set -e
cd "$(dirname "$0")/.."
dotnet build src/AppHost --nologo -v quiet
exec env DOTNET_ENVIRONMENT=Development ASPNETCORE_ENVIRONMENT=Development \
  ASPNETCORE_URLS=http://localhost:15128 ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL=http://localhost:19083 \
  ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL=http://localhost:20171 ASPIRE_ALLOW_UNSECURED_TRANSPORT=true \
  src/AppHost/bin/Debug/net10.0/Portal.AppHost
