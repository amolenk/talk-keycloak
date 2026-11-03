#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"
aspire start --non-interactive --apphost "$repo_root/src/CityPicks.AppHost/CityPicks.AppHost.csproj"
aspire wait city-picks --timeout 180 --apphost "$repo_root/src/CityPicks.AppHost/CityPicks.AppHost.csproj"
