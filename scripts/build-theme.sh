#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
docker build --target jar --output "type=local,dest=$repo_root/infra/keycloak/theme/dist_keycloak" \
  "$repo_root/infra/keycloak/theme"
