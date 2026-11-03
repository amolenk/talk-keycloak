#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
aspire stop --non-interactive --apphost "$repo_root/src/CityPicks.AppHost/CityPicks.AppHost.csproj"
