#!/usr/bin/env bash
# Reißt ALLES ab: Die ganze Umgebung (inkl. Budget, Images, Datenbanken) liegt in dieser einen Ressourcengruppe.
# Nutzung: infra/destroy.sh <resource-group>
set -euo pipefail
RG=${1:?Ressourcengruppe fehlt}

az group delete -n "$RG" --yes
echo "Ressourcengruppe noch vorhanden: $(az group exists -n "$RG")  (erwartet: false)"
