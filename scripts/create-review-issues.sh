#!/usr/bin/env bash
# Legt für jeden Entwurf in docs/reviews/issues/*.md ein GitHub-Issue an (Übersicht: docs/reviews/offene-punkte.md).
# Format eines Entwurfs: Zeile "title: …", Zeile "labels: a,b,c", Zeile "---", danach der Text (Markdown).
#
#   scripts/create-review-issues.sh          Probelauf: zeigt nur, was angelegt würde
#   scripts/create-review-issues.sh --apply  legt fehlende Labels und Issues wirklich an
#
# Ein Issue, dessen Titel es schon gibt (offen oder geschlossen), wird übersprungen – zweimal ausführen schadet nicht.
set -euo pipefail
cd "$(dirname "$0")/.."

apply=false
[[ "${1:-}" == "--apply" ]] && apply=true

# Neue Labels mit Farbe; bug, enhancement und documentation gibt es in jedem GitHub-Repo schon.
labels=(
  "prio: hoch|b60205" "prio: mittel|fbca04" "prio: niedrig|c2e0c6"
  "bereich: backend|1d76db" "bereich: frontend|5319e7" "bereich: infra|0e8a16"
  "bereich: sicherheit|d93f0b" "bereich: tests|bfdadc" "bereich: docs|0075ca" "bereich: ci|006b75"
  "review-2026-11-15|ededed"
)

if $apply; then
  for entry in "${labels[@]}"; do
    gh label create "${entry%%|*}" --color "${entry##*|}" --force >/dev/null
  done
  existing=$(gh issue list --state all --limit 500 --json title --jq '.[].title')
else
  existing=""
  echo "Probelauf – nichts wird angelegt. Mit --apply ausführen, um die Issues zu erstellen."
fi

body=$(mktemp)
trap 'rm -f "$body"' EXIT

for draft in docs/reviews/issues/*.md; do
  title=$(sed -n 's/^title: //p' "$draft" | head -1)
  labels_csv=$(sed -n 's/^labels: //p' "$draft" | head -1)
  [[ -n "$title" && -n "$labels_csv" ]] || { echo "Ungültiger Entwurf: $draft" >&2; exit 1; }
  sed '1,/^---$/d' "$draft" > "$body"

  if grep -Fxq -- "$title" <<<"$existing"; then
    echo "übersprungen (gibt es schon): $title"
  elif $apply; then
    args=()
    IFS=',' read -ra parts <<<"$labels_csv"
    for label in "${parts[@]}"; do args+=(--label "$label"); done
    echo "angelegt: $(gh issue create --title "$title" --body-file "$body" "${args[@]}")"
  else
    echo "würde anlegen: $title  [$labels_csv]"
  fi
done
