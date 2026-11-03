#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"
aspire certs trust --non-interactive
dotnet restore tests/CityPicks.IntegrationTests/CityPicks.IntegrationTests.csproj --locked-mode
dotnet build tests/CityPicks.IntegrationTests/CityPicks.IntegrationTests.csproj --no-restore
./scripts/build-theme.sh
bash ./scripts/build-email-provider.sh
docker pull postgres:17.11-bookworm
docker pull quay.io/keycloak/keycloak:26.8.0
docker pull adorsys/keycloak-config-cli:6.5.1-26.5.5
docker pull axllent/mailpit:v1.31.6@sha256:23fe39e81bde596a918000b3135c0f85dc8bb4c9ea6be274b051143f3e300387

case "$(uname -s):$(uname -m)" in
  Linux:aarch64|Linux:arm64) playwright_rid='linux-arm64' ;;
  Linux:x86_64) playwright_rid='linux-x64' ;;
  Darwin:arm64) playwright_rid='osx-arm64' ;;
  Darwin:x86_64) playwright_rid='osx-x64' ;;
  *) printf 'Playwright browser preparation is not configured for this host.\n' >&2; exit 1 ;;
esac

playwright_dir="$repo_root/tests/CityPicks.IntegrationTests/bin/Debug/net10.0/.playwright"
if [[ "$(uname -s)" == "Linux" ]]; then
  "$playwright_dir/node/$playwright_rid/node" "$playwright_dir/package/cli.js" install-deps chromium
fi
"$playwright_dir/node/$playwright_rid/node" "$playwright_dir/package/cli.js" install chromium
