#!/usr/bin/env bash
# Puts the demo back in its opening state for a rehearsal or the talk itself.
#
#   ./scripts/demo-reset.sh              stop, wipe data, restore opening values, build
#   ./scripts/demo-reset.sh --keep-data  same, but keep users, passkeys, favourites and edits
#   ./scripts/demo-reset.sh --start      also start the stack afterwards (to verify)
#   ./scripts/demo-reset.sh --no-build   skip the AppHost and test builds
#
# The opening values are set explicitly. Nothing is restored from git, so
# uncommitted work elsewhere in the repo is left alone.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

keep_data=false
start=false
build=true
for arg in "$@"; do
  case "$arg" in
    --keep-data) keep_data=true ;;
    --start) start=true ;;
    --no-build) build=false ;;
    -h|--help) sed -n '2,10p' "$0"; exit 0 ;;
    *) printf 'Unknown option: %s\n' "$arg" >&2; exit 2 ;;
  esac
done

realm_yaml="infra/keycloak/config/city-picks.yaml"
policy_file="src/CityPicks.Api/Services/PlaceEndpoints.cs"
volume="${CityPicks__Volume:-city-picks-postgres}"
warnings=()

step() { printf '\n\033[1m== %s\033[0m\n' "$1"; }
ok() { printf '   ok  %s\n' "$1"; }
warn() { printf '   !!  %s\n' "$1"; warnings+=("$1"); }

# Rewrites a file through sed without relying on GNU or BSD flavours of `sed -i`.
rewrite() {
  local file="$1"; shift
  local tmp
  tmp="$(mktemp)"
  sed "$@" "$file" > "$tmp"
  cat "$tmp" > "$file"
  rm -f "$tmp"
}

expect_line() {
  local file="$1" pattern="$2" label="$3"
  if grep -Eq "$pattern" "$file"; then ok "$label"; else
    printf '   FAILED  %s (pattern not found in %s)\n' "$label" "$file" >&2; exit 1
  fi
}

step "Stop the stack"
if "$repo_root/scripts/stop.sh" >/dev/null 2>&1; then ok "stopped"; else ok "nothing running"; fi

if [[ "$keep_data" == false ]]; then
  step "Wipe users, passkeys, favourites and curator edits"
  if docker volume inspect "$volume" >/dev/null 2>&1; then
    # Aspire can return before Docker has finished removing session containers.
    for ((attempt = 0; attempt < 30; attempt++)); do
      [[ -z "$(docker ps --all --filter "volume=$volume" --format '{{.ID}}')" ]] && break
      sleep 1
    done
    if [[ -n "$(docker ps --all --filter "volume=$volume" --format '{{.ID}}')" ]]; then
      printf 'Containers are still attached to %s; storage was not reset.\n' "$volume" >&2
      exit 1
    fi
    docker volume rm "$volume" >/dev/null
    ok "removed volume $volume"
  else
    ok "volume $volume does not exist"
  fi
fi

step "Restore opening values"
rewrite "$realm_yaml" \
  -e 's/^loginTheme: .*/loginTheme: keycloak/' \
  -e 's/^emailTheme: .*/emailTheme: keycloak/' \
  -e 's/^displayName: .*/displayName: City Picks/' \
  -e 's/^resetPasswordAllowed: .*/resetPasswordAllowed: false/' \
  -e 's/^webAuthnPolicyPasswordlessPasskeysEnabled: .*/webAuthnPolicyPasswordlessPasskeysEnabled: false/' \
  -e '/providerId: webauthn-register-passwordless/{n;s/enabled: .*/enabled: false/;}'
expect_line "$realm_yaml" '^loginTheme: keycloak$' "loginTheme: keycloak"
expect_line "$realm_yaml" '^emailTheme: keycloak$' "emailTheme: keycloak"
expect_line "$realm_yaml" '^displayName: City Picks$' "displayName: City Picks"
expect_line "$realm_yaml" '^resetPasswordAllowed: false$' "password reset disabled"
expect_line "$realm_yaml" '^webAuthnPolicyPasswordlessPasskeysEnabled: false$' "passkeys disabled"
if grep -A1 'providerId: webauthn-register-passwordless' "$realm_yaml" | grep -Eq 'enabled: false'; then
  ok "passkey registration action disabled"
else
  printf '   FAILED  passkey registration action is not disabled in %s\n' "$realm_yaml" >&2; exit 1
fi

rewrite "$policy_file" -e 's/ManagementPolicy = "[A-Za-z]*";/ManagementPolicy = "CanManagePlaces";/'
expect_line "$policy_file" 'ManagementPolicy = "CanManagePlaces";' "ManagementPolicy = CanManagePlaces"

step "Check prerequisites"
if [[ -f infra/keycloak/theme/dist_keycloak/city-picks.jar ]]; then ok "theme JAR built"; else
  warn "theme JAR missing: run ./scripts/build-theme.sh"
fi
for image in postgres:17.11-bookworm quay.io/keycloak/keycloak:26.8.0 \
  adorsys/keycloak-config-cli:6.5.1-26.5.5 \
  axllent/mailpit:v1.31.6@sha256:23fe39e81bde596a918000b3135c0f85dc8bb4c9ea6be274b051143f3e300387; do
  if docker image inspect "$image" >/dev/null 2>&1; then ok "image $image"; else
    warn "image $image not pulled: run ./scripts/prepare.sh while online"
  fi
done
if [[ -d infra/keycloak/theme/node_modules ]]; then ok "theme node_modules (Storybook)"; else
  warn "Storybook needs: cd infra/keycloak/theme && npm install"
fi

if [[ "$build" == true ]]; then
  step "Build (so the cold start in demo 1 doesn't compile from scratch)"
  dotnet build src/CityPicks.AppHost/CityPicks.AppHost.csproj --nologo -v quiet >/dev/null
  ok "AppHost built"
  dotnet build tests/CityPicks.IntegrationTests/CityPicks.IntegrationTests.csproj --nologo -v quiet >/dev/null
  ok "integration tests built"
fi

step "Git"
printf '   branch: %s\n' "$(git branch --show-current)"
other_changes="$(git status --porcelain --untracked-files=no -- . ":!$realm_yaml" ":!$policy_file")"
if [[ -n "$other_changes" ]]; then
  warn "uncommitted changes outside the demo files: slide 34's 'git diff --stat' will show them"
  printf '%s\n' "$other_changes" | sed 's/^/        /' | head -15
else
  ok "no uncommitted changes outside the demo files"
fi
demo_changes="$(git status --porcelain --untracked-files=no -- "$realm_yaml" "$policy_file")"
if [[ -n "$demo_changes" ]]; then
  warn "demo files differ from HEAD after reset: commit the opening state so slide 34 starts clean"
  printf '%s\n' "$demo_changes" | sed 's/^/        /'
fi

if [[ "$start" == true ]]; then
  step "Start"
  "$repo_root/scripts/start.sh"
fi

step "Manual"
printf '   - Delete old localhost passkeys in Chrome (chrome://settings/passkeys or Google Password Manager)\n'
printf '   - Close all Keycloak and City Picks tabs, so there is no lingering session\n'
printf '   - Walk through presentation/DEMO-CHECKLIST.md\n'

if ((${#warnings[@]} > 0)); then
  printf '\n\033[1m%d warning(s).\033[0m Fix them before going on stage.\n' "${#warnings[@]}"
else
  printf '\n\033[1mReady.\033[0m\n'
fi
