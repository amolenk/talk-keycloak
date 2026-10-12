# Local identity stack verification probe

Prerequisite: [Verify the selected local identity stack and configuration-preservation contract](https://github.com/amolenk/talk-keycloak/issues/13).

This branch is a throwaway verification asset. Its minimal API does not implement City Picks or browser UI. `contract.py` temporarily mutates client settings and creates/deletes a disposable registration account; run against this probe realm only. `lifecycle.py` starts/stops this exact AppHost and tests an intentional initializer failure. All fixed `demo` credentials are the selected local-demo defaults.

## Prepare

Install .NET SDK **10.0.401** with framework **10.0.12**, Docker and Python 3. Work from this directory. `global.json`, explicit package references and package lock files fix the .NET dependency baseline.

```bash
dotnet tool install Aspire.Cli --version 13.6.1 --tool-path .tools
dotnet tool install dotnet-ef --version 10.0.12 --tool-path .tools
export PATH="$PWD/.tools:$PATH"
aspire certs trust --non-interactive
dotnet restore AppHost/AppHost.csproj --locked-mode
dotnet build AppHost/AppHost.csproj --no-restore
docker pull quay.io/keycloak/keycloak:26.8.0
docker pull adorsys/keycloak-config-cli:6.5.1-26.5.5
docker pull postgres:17.11-bookworm
```

Verify pulled digests against `RESULTS.md`; stop on a mismatch. Tags in this small AppHost match the verified versions, while the evidence locks their observed index/ARM64 manifest digests. Other CPU architectures require their own observed manifest digest before claiming a native-platform pin.

On the Linux sandbox only, startup required the following environment alongside the downloaded SDK/tool paths:

```bash
export SSL_CERT_DIR="$HOME/.aspnet/dev-certs/trust:/usr/lib/ssl/certs"
export NO_PROXY="${NO_PROXY:-localhost,127.0.0.1,::1},[::1]"
export no_proxy="${no_proxy:-localhost,127.0.0.1,::1},[::1]"
```

macOS uses OS certificate trust. For Python installations that do not consume the macOS trust store, export only the public ASP.NET developer certificate to `.local/dev-cert.pem` and set `PROBE_CA_FILE` to its absolute path; no validation bypass is used. One supported export command is `dotnet dev-certs https --export-path .local/dev-cert.pem --format PEM --no-password` after creating `.local`. Do not publish the accompanying private key. Chrome must independently trust both origins.

## Run

Choose a fresh, uniquely named probe volume for first-start evidence; keep that name through all lifecycle checks. No script deletes a volume.

```bash
export PROBE_VOLUME=wayfinder-stack-probe-presenter
aspire start --non-interactive --apphost "$PWD/AppHost/AppHost.csproj"
aspire wait probe --apphost "$PWD/AppHost/AppHost.csproj" --timeout 120
python3 check.py
python3 contract.py
python3 lifecycle.py
aspire stop --non-interactive --apphost "$PWD/AppHost/AppHost.csproj"
```

The probe listens at `https://localhost:7100`; Keycloak at `https://localhost:8443`. A single binary runs as both the one-shot `initializer` and the minimal API. `WaitForCompletion` requires initializer exit 0; the API health check verifies the seed marker and discovery issuer. No manual TLS assets are needed: Aspire supplies the trusted development certificate to both resources.

`lifecycle.py` runs config-cli against `http://keycloak.dev.internal:8080` on the observed Aspire container network. This internal transport is distinct from the public HTTPS issuer. It sets `IMPORT_MANAGED_CLIENTS=no-delete`, `IMPORT_MANAGED_ROLES=no-delete`, and `IMPORT_MANAGED_USERS=no-delete`, checks apply/reapply and bad-password failure, verifies container recreation and retained snapshots, tests initializer exit 23, and exercises synchronous EF tooling seeding.

For the checksum/drift experiment, run `python3 drift.py`, then apply the unchanged file with the explicit Docker command below. An unchanged-file apply skips drift; adding `-e IMPORT_CACHE_ENABLED=false` explicitly forces reconciliation. Find the current Keycloak network with `docker inspect <keycloak-container>`:

```bash
docker run --rm --network <observed-aspire-network> \
  -e KEYCLOAK_URL=http://keycloak.dev.internal:8080 \
  -e KEYCLOAK_USER=admin -e KEYCLOAK_PASSWORD=demo \
  -e IMPORT_FILES_LOCATIONS=/config/presentation.json \
  -e IMPORT_MANAGED_CLIENTS=no-delete -e IMPORT_MANAGED_ROLES=no-delete \
  -e IMPORT_MANAGED_USERS=no-delete \
  -e LOGGING_LEVEL_DE_ADORSYS_KEYCLOAK_CONFIG=DEBUG \
  -v "$PWD/assets:/config:ro" adorsys/keycloak-config-cli:6.5.1-26.5.5
```

## Presenter-Mac confirmation checklist

Record results in the prerequisite ticket. This remains required live evidence.

1. Record Mac model/CPU architecture, macOS version, Chrome version, Docker client/server version, `dotnet --info` and `aspire --version`. On ARM64 verify the manifest digests below; on x64 record the actual native manifests.
2. Prepare online using the exact versions above. Record certificate-trust results and any `aspire doctor` failures. Start on a fresh named probe volume; confirm initializer Finished/exit 0 and probe Healthy. Open both `/health` and Keycloak discovery in Chrome without certificate interstitials. Run the three scripts and record their PASS output.
3. Check the Keycloak login page is reachable in Chrome at the agreed origin. The scripts verify protocol redirects, not Blazor UX or actual browser logout UI.
4. Stop the exact AppHost; wait for its session containers to disappear while its named volume remains. Quit/restart Docker Desktop with prepared caches retained. Disable Wi-Fi and other internet connectivity, keeping localhost/Docker networking available.
5. With the machine genuinely offline, cold-start the AppHost, wait for probe Healthy, run `check.py` and `lifecycle.py` (includes explicit config-cli apply/reapply), and open both HTTPS origins in Chrome. Confirm no image download, NuGet restore, remote asset, trust or issuer failure. This deliberately measures the prepared probe, not future theme/fonts/images that do not exist yet.
6. Report observed failures verbatim (first DCP/TLS error, failing command and exit status) rather than substituting versions. Restore connectivity afterwards. Actual Touch ID/passkey interaction belongs to [Verify the selected passkey journey on the presenter MacBook](https://github.com/amolenk/talk-keycloak/issues/12).
