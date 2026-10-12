# Observed verification evidence

Observed on Linux ARM64 in the Docker Sandbox, 2026-10-11 through 2026-10-12. Final portable sources on this branch were rebuilt and tested against a newly created `wayfinder-stack-probe-published-20261012` volume. Presenter-Mac trust and prepared cold-offline operation remain unverified; this prerequisite stays open.

## Exact versions and digests

- SDK 10.0.401; Microsoft.NETCore.App/Microsoft.AspNetCore.App 10.0.12.
- Aspire CLI/AppHost SDK/hosting/PostgreSQL hosting 13.6.1; Keycloak hosting 13.6.1-preview.1.26506.6.
- EF Core/design/tool/JWT bearer 10.0.12; Npgsql EF provider/Npgsql 10.0.3. Package lock files capture the complete resolved graphs; locked-mode restore succeeds.
- Docker client/server 29.8.1, linux/arm64.

| Image | Index digest | Native ARM64 manifest digest |
| --- | --- | --- |
| quay.io/keycloak/keycloak:26.8.0 | sha256:27b3230fbabb8c9ff3c66c864cf09dcd8a41aca82e5100679d109ef0eeb05741 | sha256:cae4f0fe0315f8c259a2e6deba8df93a5f1f297f65aa09198660add1056a8632 |
| adorsys/keycloak-config-cli:6.5.1-26.5.5 | sha256:0955d98c8a341898b7aa177477edf8a1e90569ae50bbe7598141c1270b773274 | sha256:e40682ac702ad154ea8c147105e7a2bf567d2fdae772d5e4ce1624d2640f75e0 |
| postgres:17.11-bookworm | sha256:3645570cccdfa447589da9f57dd740faa29b30938e861289a5574b6ca6b03826 | sha256:a85953d6f830fd55a12929df3b7a4fa94dc96d652de62d2d714e0867ccb3670d |

No pinned version was substituted. AppHost build succeeds with ASPIRE010 (CLI bundle disabled). The separately installed exact CLI successfully orchestrates this probe.

## Observed passing checks

- Fresh PostgreSQL volume initializes separate `keycloak` and `citypicks` databases/users. First Keycloak import works. Native API and Keycloak share the trusted Aspire development certificate at the approved public origins; no TLS-validation bypass.
- One-shot initializer applies EF migrations and async seeding, with one transactional completion marker and sample. API startup waits for initializer success. A 15-second initializer delay finishes before API start. HTTP health verifies the marker and matching discovery issuer.
- Intentional initializer exit **23** produces API **FailedToStart**; `aspire wait probe --timeout 10` fails and no API listener exists. Restart with failure disabled recovers.
- Real Authorization Code/S256 PKCE login through cookie-backed HTML form submission produces matching discovery/token issuer `https://localhost:8443/realms/city-picks` and audience `city-picks-api`.
- Traveler identity/traveler/curator statuses **200/200/403**; curator **200/200/200**; observer **200/403/403**. Anonymous and signature-tampered requests **401**.
- `fullScopeAllowed=false` plus explicit traveler/curator scope mappings excludes an unrelated realm role assigned to curator. Observer retains the API audience without application roles. Curator has no composites.
- Actual self-registration form succeeds without email/required-action barriers. New account receives only traveler in the flat `roles` claim and API statuses **200/200/403**; disposable registration accounts are removed after testing.
- RP-initiated logout using ID-token hint returns the exact logout callback with state. Subsequent cookie-backed `prompt=none` yields `login_required`.
- A genuine Keycloak-signed token with audience `account` gets **401** on every protected endpoint. A genuine expired token gets **401** (temporary client lifespan 2 seconds; probe validation clock skew 5 seconds). Client settings are restored afterward.
- Config-cli 6.5.1 against Keycloak 26.8.0 applies the presentation-only file over private-network HTTP. Reapply exits 0 with `import checksum same`. With explicit no-delete settings for clients/roles/users, omitted resources retain exact IDs; user subjects and credential IDs/types remain stable and password login continues working.
- Wrong administrator password makes explicit apply exit **1**, without changing the realm/identity/credential snapshot. Applying with the correct credential recovers. No partial-mutation/rollback guarantee is inferred from this authentication failure.
- AppHost stop removes its exact session containers; named volume survives. **Removal can finish shortly after CLI stop returns**: lifecycle checks wait up to 15 seconds for removal before asserting it.
- Next start produces new PostgreSQL/Keycloak container IDs. Keycloak logs `Realm 'city-picks' already exists. Import skipped`. Exact realm/identity/credential snapshots, applied display name and `retained-curator-edit` survive. One seed marker remains.
- Synchronous `dotnet-ef database update --no-build` exercises `UseSeeding`, preserving the edit after async initialization.

## Earlier checksum/drift evidence

The original presentation file (different whitespace from the published file) recorded checksum `5e9afddfab674247c40cd799ac16f0a37ce159b820142c1227174030958a1900`. Manual display-name drift survives unchanged-file reapply. Explicit `IMPORT_CACHE_ENABLED=false` restores declared presentation. These were observed in the earlier direct-Docker experiment; the final Aspire run independently verifies normal apply/reapply checksum skip. Do not treat the old checksum as a fixed value for these reformatted published bytes, or checksum skip as drift reconciliation.

## Failure history and remaining limits

- Earlier Aspire DCP startup timed out in `KubernetesService.ExecuteWithRetry`/`CreateDcpObjectsAsync`. Doctor exposed proxy CONNECT 502 for bracketed IPv6 loopback. Adding `[::1]` to proxy bypass, trusting certificates with Linux `SSL_CERT_DIR`, and using exact CLI 13.6.1 allowed successful orchestration with proxies retained.
- Doctor's developer-certificate DCP check passed; its independent ephemeral-certificate check still failed with an SSL connection error. Linux certutil/browser trust was unavailable. Doctor is not fully green.
- Earlier manual short-validity TLS certificate was rejected by .NET as `NotTimeValid`; the mechanism was not explained. The final published probe removes that mechanism and uses Aspire-provisioned trusted development certificates for both resources.
- Evidence is protocol/API/initializer/configuration evidence, not the finished Blazor UX, theme, images, or physical passkeys. Actual presenter-Mac Chrome trust and genuine prepared cold-offline operation require the live checklist in `README.md`.
- This minimal health check is sufficient for probe marker/issuer evidence; final application readiness must additionally validate its selected city content and assets.

## Reproduction

See `README.md` for exact preparation/run commands and the presenter checklist. The final-source run used `dotnet restore`, `dotnet build --no-restore`, `aspire start`, `aspire wait probe`, `python3 check.py`, `python3 contract.py`, `PROBE_VOLUME=wayfinder-stack-probe-published-20261012 python3 lifecycle.py`, `aspire stop`, and `dotnet restore --locked-mode`. All completed successfully after fixing a probe-only missing Python import. The AppHost is stopped at session end; named probe volumes are retained.
