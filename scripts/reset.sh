#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
"$repo_root/scripts/stop.sh"
volume="${CityPicks__Volume:-city-picks-postgres}"
# Aspire can return before Docker has finished removing session containers.
for ((attempt=0; attempt<30; attempt++)); do
  if [[ -z "$(docker ps --all --filter "volume=$volume" --format '{{.ID}}')" ]]; then
    docker volume rm "$volume"
    exit 0
  fi
  sleep 1
done
printf 'Containers are still attached to %s; storage was not reset.\n' "$volume" >&2
exit 1
