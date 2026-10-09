# Local identity baseline: .NET 10, Aspire and Keycloak

Research for [issue #11](https://github.com/amolenk/talk-keycloak/issues/11), part of [wayfinder map #1](https://github.com/amolenk/talk-keycloak/issues/1).

## Findings and evidence boundary

Observed on **2026-10-09** using release APIs, NuGet indexes, tagged source and official documentation. Versions below come from those endpoints, not a projection from the calendar. This is a compatibility investigation, not an executed Mac demo or a final design decision.

The architecture in map #1 has a documented path: standalone Blazor WASM uses generic OIDC Authorization Code + PKCE; an ASP.NET Core API validates Keycloak access tokens with JWT bearer authentication; Aspire starts and coordinates the local resources. **Stable Aspire does not imply a stable Keycloak integration package.** Both official Keycloak packages are still preview. Also, the latest config-cli release has no tagged test evidence for the newest stable Keycloak. These are explicit decision/verification boundaries, rather than grounds for claiming the whole combination is certified. [S1–S6, S10–S13]

### Release and compatibility matrix

| Component / combination | Actual available release or evidence | Consequence |
| --- | --- | --- |
| .NET 10 | Official channel metadata: SDK **10.0.401**, runtime and ASP.NET Core **10.0.12**, released 2026-09-08; active LTS, EOL 2028-11-14. Metadata lists macOS arm64 and x64 SDK artifacts. [S1] | Target `net10.0`; pin SDK and matching framework packages. |
| Aspire core | GitHub latest stable **13.6.1**, published 2026-10-07; NuGet `Aspire.Hosting` index corroborates **13.6.1**. Current prerequisites require .NET 10 SDK for C# AppHosts. [S2, S3] | Compatible documented baseline for the required .NET 10 AppHost. |
| Official Aspire Keycloak hosting and API integration | Both **13.6.1-preview.1.26506.6**; neither package index contains a non-preview release. Hosting depends on Aspire.Hosting >=13.6.1; authentication has an explicit `net10.0` asset and JWT/OIDC dependencies >=10.0.12. [S4, S5] | Available, official, framework-compatible **preview** path. Do not label it GA. |
| Keycloak server | GitHub latest non-prerelease **26.8.0**, published 2026-10-01; official downloads agree. [S6] | Current stable candidate; pin full tag and preferably OCI digest. Protocol compatibility is supported by OIDC/JWT docs, but this exact end-to-end combination was not run here. |
| Aspire's bundled Keycloak image default | Tagged Aspire 13.6.1 source defaults to **`quay.io/keycloak/keycloak:26.6`**, not 26.8.0. [S7] | Explicit `.WithImageTag("26.8.0")` is required to select the current server; `26.6` is not a full patch pin. Image override is not proof of runtime compatibility. |
| keycloak-config-cli | Latest stable **6.5.1**, published 2026-05-22. Tagged CI and release JAR assets include server **26.5.5** (Admin Client **26.0.8**); matrix also covers older servers. Project promises latest four Keycloak releases **if possible**, with breaking-change exceptions. [S10, S11] | **6.5.1 + 26.5.5 is an evidenced release/test target; 6.5.1 + 26.8.0 remains unverified.** “26.x” alone is insufficient evidence. |
| PostgreSQL option | Keycloak database guide lists **18.x, 17.x, 16.x, 15.x, 14.x** supported and 18 tested for the current guide. [S9] | A supported major can be selected, but exact server image patch/digest must be resolved when choosing this option; none was invented here. |

Two viable integration options remain for human choice:

- **Official preview convenience packages:** use the pinned Keycloak hosting package and optionally `AddKeycloakJwtBearer` in the API. This supplies resource defaults and health checks. WASM still uses its own `AddOidcAuthentication`. [S4, S5, S7, S12]
- **Stable-package-only application path:** use Aspire's generic container resource and stable ASP.NET Core JWT bearer / WASM authentication packages, explicitly configuring Keycloak startup, health checks, authority and persistence. Generic containers are an Aspire resource mechanism; this loses the Keycloak-specific convenience API. It avoids requiring preview *packages*, without asserting a vendor-certified combination. [S3, S12, S13, S24]

Using Keycloak 26.5.5 for a separately explained config-cli walkthrough has tagged compatibility evidence, but choosing an older demo server or two server versions is a design tradeoff. If the complete demo uses 26.8.0, test the config-cli scenarios below before pinning that pairing. Runtime-build/edge config-cli images require additional downloads or custom preparation and are not a stable release substitute. [S10, S11]

## Public WASM and bearer API integration

1. **Browser client:** register a distinct public OIDC client (Client authentication OFF), Standard Flow ON, require PKCE **S256**. Implicit Flow, Direct Access Grants and service accounts are unnecessary for this browser architecture. No client secret can be kept in WASM or `wwwroot` configuration. Keycloak documents these capabilities and exact case-sensitive redirect matching. Register the actual app's login callback and valid post-logout callback; keep presenter ports stable. [S14, S12]
2. **Blazor:** use `Microsoft.AspNetCore.Components.WebAssembly.Authentication` and `AddOidcAuthentication`, real browser-reachable realm authority (`<base>/realms/<realm>`), public client ID and response type `code`. Use `RemoteAuthenticatorView` callback routes and `AuthorizationMessageHandler` limited to the actual API URL and intended scopes. Request any optional API client scope explicitly; `openid profile` alone is not an API audience configuration. [S12, S17]
3. **API:** use stable `AddJwtBearer` or preview `AddKeycloakJwtBearer`. Validate signature, expiration, exact issuer and API audience. The API returns **401** for absent/invalid credentials and **403** for valid identity without authorization; it does not redirect API requests to login. Keycloak's ASP.NET convenience integration configures a discovery-capable backchannel and `https+http://<resource>/realms/<realm>` authority, but does **not** add role flattening or an audience value. Its configure callback can override Authority. [S8, S13]
4. **Audience:** represent the API separately from the WASM client. Keycloak does not automatically make the frontend client an access-token audience. Its Audience Resolve mapper adds services based on included client roles; with realm roles, a dedicated hardcoded API Audience mapper is a straightforward documented option. Default versus optional client scope determines whether the mapper always applies or the browser must request the scope. Configure API `Audience` to match the issued `aud`; `azp` (requesting client) is not a replacement. [S15, S13]
5. **Roles:** default realm roles appear in nested `realm_access.roles`, client roles in `resource_access.<client>.roles`, and by default in access tokens rather than ID tokens. ASP.NET `RoleClaimType` is a claim name, not a JSON-path evaluator. Options: a Keycloak mapper emitting a flat multivalued `roles` claim and API `RoleClaimType="roles"`; or an idempotent API claims transformation extracting the selected nested roles **after token validation**. Do not accidentally treat roles of unrelated clients as API roles. Choose `MapInboundClaims=false` if preserving original claim names; set NameClaimType deliberately. [S16, S18]
6. **UI identity:** the generic WASM authentication service returns the OIDC user's `profile`, so API access-token roles do not automatically become browser `IsInRole` claims. Emit suitable ID-token/UserInfo role claims plus configure WASM role mapping/custom account factory, or have the API return the relevant identity/authorization view. UI hiding is presentation only; API checks remain authoritative. A profile inspector should distinguish ID/user-profile claims from access-token claims, and avoid printing reusable raw tokens in a projected demo. [S16, S19, S17, S13]

**Documentation discrepancy:** the Learn generic WASM article's introduction currently attributes the authentication library to MSAL. Its own `AddOidcAuthentication` instructions are generic OIDC, and the tagged .NET 10.0.12 implementation imports **`oidc-client`**, not MSAL. Use the generic OIDC path for Keycloak; do not infer that Entra-specific `AddMsalAuthentication` is the supported Keycloak client. [S12, S19]

## Mac topology, issuer and HTTPS/CORS constraints

A low-complexity topology option is native Mac AppHost/API/WASM dev server and containerized Keycloak (plus an optional database/config-cli job). Chrome and the native API can both reach the same published `localhost` Keycloak URL. Docker Desktop runs Linux containers in a VM; host access uses published ports, not container IPs or Docker-internal service names. A config-cli container must use a container-network Keycloak address or `host.docker.internal:<published-port>`; its own `localhost` is not the Mac. [S20]

| Caller | Address requirement |
| --- | --- |
| Chrome / WASM | Authority, discovery authorization/token/UserInfo/logout URLs and API base URL must be reachable from the **host browser**. Neither `http://keycloak:8080` nor `https+http://keycloak` is a browser endpoint. |
| Native API | Can use the same public loopback authority; Aspire-discovered backchannel is optional. Verify discovery `issuer` and token `iss` exactly agree. |
| Containerized API / config-cli | Use shared-network DNS or Docker Desktop's host gateway for transport. If API metadata uses an internal URL, preserve the canonical public issuer validation and ensure the advertised JWKS URL is reachable too. Changing metadata transport is not permission to disable issuer validation. |

The table is an integration inference from Docker networking, Keycloak hostname and ASP.NET JWT behavior. Keycloak **hostname v2** supports a full fixed frontend URL and optionally dynamic private backchannel resolution (`hostname-backchannel-dynamic=true`, requiring a full hostname URL). `KC_HOSTNAME` influences advertised URLs, **not listening ports**. A native API avoids the extra split-horizon DNS/certificate setup required when all application processes are containerized. Inspect the discovery document from every caller; manually setting only a metadata URL may leave an unreachable advertised `jwks_uri`. [S20, S21, S13]

- **TLS option:** browser-facing app/API/Keycloak can use locally trusted HTTPS. Aspire's tagged Keycloak resource has certificate configuration hooks and switches its primary endpoint to HTTPS when a TLS certificate is supplied. Keycloak needs explicit certificate provisioning; a native ASP.NET development certificate is not automatically a Keycloak server certificate. The .NET certificate can be exported for other tools, and .NET 10 adds SANs including localhost and host.docker.internal; serving the right certificate and trusting it in a container are still separate concerns. Trust must work in Chrome, native .NET, and any container/Java client that needs the HTTPS endpoint. Certificate plumbing on the presenter's Mac remains a runtime verification. [S7, S25]
- **Loopback HTTP option:** Keycloak `start-dev` supports HTTP; local JWT metadata can opt into `RequireHttpsMetadata=false`. This does not relax signature/issuer/audience checks. W3C secure-context rules treat loopback and qualifying localhost names as potentially trustworthy, so local HTTP is not categorically incompatible with WebAuthn. An arbitrary HTTP hostname mapped in `/etc/hosts` does not inherit that exception. Chrome policy, client library behavior, mixed-content restrictions and Keycloak realm SSL policy still need rehearsal for the exact topology. HTTPS is the more portable option beyond loopback. [S7, S21, S22, S8]
- **Two CORS configurations:** Keycloak client's Web Origins governs browser calls to identity endpoints; ASP.NET API needs its own CORS policy if API and WASM origins differ. Allow the exact scheme/host/port and required methods/Authorization header. Keycloak's allowed-origin token feature is specific to its adapters; ASP.NET does not automatically consume it. A same-origin static host/API deployment removes the API cross-origin requirement while retaining bearer authentication. [S14, S23]
- **Preflight:** direct the browser to the intended HTTPS API endpoint; HTTP-to-HTTPS redirects can break CORS preflight. ASP.NET places CORS after routing and before authorization. An HttpClient smoke test cannot substitute for a real browser CORS test. [S23]
- **Browser configuration and renewal:** Aspire project environment/reference injection does not by itself rewrite a downloaded WASM `wwwroot/appsettings.json`. Supply public authority/API URLs through prepared static configuration or an explicit browser configuration endpoint; never embed admin credentials there. The generic OIDC source attempts silent sign-in/token renewal and falls back to interaction when it fails. Rehearse expiry/logout and Chrome cookie/iframe behavior rather than promising silent renewal always works. [S12, S19, S24]
- **Passkey baseline implication:** keep the Keycloak host/RP ID stable across stages; Keycloak's RP ID defaults to its base URL host, and WebAuthn success depends on browser, platform and authenticator. Restoring a realm/user by recreation can leave local credentials referring to missing server identities. Actual Touch ID/offline passkey behavior is not proven by server support, and belongs in a focused Mac rehearsal. [S26]

### Architecture-specific image evidence

Read-only registry inspections succeeded during research:

| Registry artifact | Observed index digest | Native platforms relevant to Mac |
| --- | --- | --- |
| `quay.io/keycloak/keycloak:26.8.0` | `sha256:b0f60d489d51c5d113390bdf5461d4c06e6051be026c05549f2e1e10ec352bcc` | `linux/arm64`, `linux/amd64` |
| `docker.io/adorsys/keycloak-config-cli:6.5.1-26.5.5` | `sha256:0955d98c8a341898b7aa177477edf8a1e90569ae50bbe7598141c1270b773274` | `linux/arm64`, `linux/amd64` |

Primary evidence is the registry OCI indexes, inspected with `docker buildx imagetools inspect <artifact>`; reproduce these commands before pinning/pulling. The config-cli tag names its build/test server target, not a requirement that the runtime server must have exactly that version. These checks establish image availability and architecture, **not execution or 26.8 compatibility**. [S6, S11, S27]

## Lifetime, storage and realm/configuration semantics

**Container reuse, durable data and configuration application are separate mechanisms.** Aspire `Session` containers are created for an AppHost run and disposed on shutdown. `Persistent` attempts reuse by container name and survives AppHost shutdown, but an Aspire-created container with changed resource configuration may be replaced. A persistent container is therefore not a durable-data guarantee. Data volumes survive container recreation; a process/container restart does not imply a clean realm. [S28, S4]

| Local storage option | What survives / limitations |
| --- | --- |
| Keycloak default `dev-file` without external storage | Data is development-only and tied to the container filesystem; a session-container replacement loses it. Useful for clean disposable tests. [S9, S28] |
| `WithDataVolume()` / `WithDataBindMount(...)` | Mounts `/opt/keycloak/data`; preserves development database across replacements. Named volume is documented by the official integration. Existing admin credentials are in stored state; bootstrap environment changes do not reset an existing admin. Mac bind mount sharing/permissions are an additional local setup concern. [S4, S7, S20] |
| External PostgreSQL | Configure `KC_DB=postgres`, JDBC URL/host/database and credentials; persist **PostgreSQL's data**, not merely Keycloak's data directory. Keycloak ships the driver. Aspire docs describe the Community Toolkit `WithPostgres` helper; explicit supported Keycloak environment options avoid making that community dependency mandatory. Adds a container and readiness dependency. [S9, S4] |

### Import versus config-cli

| Mechanism | First start | Subsequent starts / changes | Reset boundary |
| --- | --- | --- | --- |
| Keycloak startup `--import-realm` | Imports JSON in `/opt/keycloak/data/import`; `<realm>-realm.json` naming required; full startup waits for import. | **Skips an existing realm.** Editing JSON and restarting does not update an existing realm. | Fresh storage or explicit realm deletion/recreation is required for clean re-seeding. [S29] |
| Aspire `WithRealmImport` | In 13.6.1 copies files with container-file injection; `AddKeycloak` already adds `--import-realm`, and uses `start-dev` locally. | Has the same skip-existing semantics. The older boolean overload is obsolete and uses a bind mount instead. | Local-run file injection is not supported by this helper for `aspire publish/deploy`; unnecessary for this local-only demo. [S7, S4] |
| `kc.sh import` command | Offline import command launches, imports, exits. | Defaults to `--override true`; different from startup import. All nodes must be stopped before override to avoid inconsistent caches. | Replacement operation; export/import is not a full-state snapshot of sessions/events/revoked tokens. [S29] |
| config-cli Admin API apply | Creates/configures the online realm from minimal JSON/YAML; does not require server restart. | Intended idempotent repeated application and updates, subject to checksum skipping and resource-management settings below. | Can change/delete managed objects; it is not intrinsically non-destructive or a complete presenter-state reset. [S10, S30–S32] |

config-cli boundaries that affect a live walkthrough:

- **Idempotence is not continuous drift reconciliation.** Tagged checksum source skips an unchanged recorded import checksum; a manual Admin Console edit is not necessarily restored by running the same file again. Provider source computes checksum after variable substitution, so changed edition/endpoint values can trigger an update. Cache keys/checksum behavior must be understood when moving between stages. [S31, S32]
- **Deletion is scoped by ownership and resource type.** Default `import.remote-state.enabled=true` records managed resources in realm attributes and purges previously managed objects that disappear from the desired configuration. With remote state disabled, removal may include all absent existing entities of the selected managed types. Omitted collections and explicitly empty collections differ. Per-type `import.managed.<entity>=no-delete` prevents deletion for that type. Tagged defaults use `full` for clients, roles, groups, flows and others, but **`no-delete` for client scopes and scope mappings**; do not generalize that everything is full-managed. [S30, S33]
- **User/credential preservation needs evidence.** User role/group merge options default false, so declarative membership updates may remove memberships not present in input. Deleting/recreating user-federation resources can delete their users/offline tokens. A wholesale realm replacement can invalidate the demo's accumulated identity/passkey state. Minimal managed configuration plus explicit preservation options is a candidate, not a guarantee. [S10, S30, S33]
- **No transactional/no-delete blanket guarantee found.** The tool applies via multiple Admin API operations; its documentation does not establish an atomic rollback contract for a failed apply. Test changed configurations and recovery on disposable state before using them on presenter state. The MANAGED guide's broad “delete and recreate” wording should not be read as every object being recreated on every unchanged run, given the checksum behavior. [S10, S30, S31]
- **Startup sequencing:** wait for Keycloak readiness, run config-cli as a one-shot process/job, and only then declare dependent authenticated resources ready (e.g. Aspire completion dependency). Wait-for-server-health alone does not establish that a later Admin API initializer succeeded. config-cli has an availability check (default false, 120s timeout); enable/use explicit readiness plus check job exit status. [S7, S10, S24]

## Offline preparation and isolated real-Keycloak smoke tests

“Offline” here means internet-disconnected **local services**, not an offline PWA or Keycloak offline-token grant. OIDC discovery, keys, login and API calls still require the local servers. From the documented startup/download behavior, a viable preparation plan is to pin and cache SDK/CLI/NuGet packages, any required WASM workloads, chosen container images, test-helper images, browser binaries, and all built branding/assets. Build themes beforehand; avoid CDN fonts, remote configuration URLs, external identity providers and required email/SMS flows. Run the **exact start/reset/test paths after disabling internet**, including a cold Docker/AppHost start, rather than relying on a warm browser cache. This is an operational inference; offline execution on the presenter's Mac was not performed here. [S1, S3, S10, S20, S34]

Two documented testing paths are available: Aspire.Hosting.Testing for whole-AppHost closed-box tests; or Testcontainers plus WebApplicationFactory for a focused API/real-container fixture. Aspire tests randomize proxied resource ports and disable the dashboard by default; disposal cleans up app resources. Persistent containers and named volumes must be explicitly excluded from a disposable test profile. Testcontainers supports real Docker dependencies and any containerized service; no dedicated .NET Keycloak module is assumed here. [S34, S35]

Proposed small smoke suite (future acceptance evidence, not tests run in this research):

1. Start a **new session container and fresh realm/storage** with the exact pinned image and test-only credentials, and obtain allocated endpoints. Wait for health **and realm discovery** / successful initializer completion. Assert discovery `issuer`, token `iss` and API accepted issuer agree.
2. Verify anonymous browsing, protected endpoint 401 without token, 401 for wrong issuer/audience/expired or invalid signature, traveler denial (403) and curator success. Use real Keycloak-issued tokens with deliberate test-client/audience configuration.
3. Use an isolated browser context for at least one public **Authorization Code + S256** login, API call and logout. This covers callbacks, browser CORS and claims that a machine-token test misses. A test-only confidential service-account client can cover some bearer plumbing, but cannot establish delegated user/PKCE/passkey behavior. Direct-grant-only tests would also miss the required browser flow. [S12–S16, S23]
4. Verify startup import on fresh storage; restart with retained state and changed JSON to demonstrate skip-existing; exercise config-cli create/update/reapply plus one `full` deletion and `no-delete` preservation case. Manually introduce drift and verify the actual checksum behavior. For 26.8.0 this suite is specifically the missing config-cli compatibility evidence.
5. Give test runs unique realm/container/storage names and dynamically construct URLs, valid redirects and origins. Fixed presenter hostname/ports or hard-coded `KC_HOSTNAME` conflict with Aspire's randomized test endpoints; either inject resolved values or serialize a fixture with reserved stable ports. Avoid any test that resets the presenter's volume or realm. [S21, S28, S34]

Docker must be installed/running, images cached for the Mac CPU architecture, and enough VM memory/disk available. Expect Keycloak boot/import to require a readiness timeout rather than a sleep; no universal startup duration or Mac resource minimum was measured. If Testcontainers is selected, cache its resource-reaper/helper images as well and verify cleanup offline. Headless automated browser authentication does not prove physical Touch ID or offline synced-passkey availability; keep that a presenter-device test. [S20, S26, S35]

## Questions carried forward

- Accept official **preview** Keycloak packages or configure generic containers/stable auth packages? Choose this explicitly despite stable Aspire core.
- Pin stable Keycloak **26.8.0** and verify config-cli **6.5.1** against the required configuration subset, or select the evidenced **26.5.5** pairing for the walkthrough? No 26.8 config-cli support claim is justified yet.
- Choose dev-file volume versus external PostgreSQL, container lifetime, and separate reset/preservation boundaries for each presenter stage. Does reset retain users/passkeys and application favourites?
- Choose canonical loopback HTTP versus locally trusted HTTPS, native versus containerized API, precise fixed presenter URLs, and flat versus transformed role claims. These choices drive test endpoint configuration.
- Confirm Mac architecture/macOS/Chrome/Docker versions only as needed; rehearse cold offline startup, certificates, state transitions and Touch ID on that machine. No application execution or physical Mac verification was performed in this report.

## Primary sources

Source links marked “current” may move; release versions and tagged source preserve the observed evidence. Registry digests above identify the actual inspected manifests.

- **S1:** [.NET 10 release metadata](https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/10.0/releases.json).
- **S2:** [Aspire latest release API](https://api.github.com/repos/microsoft/aspire/releases/latest), [13.6.1 release](https://github.com/microsoft/aspire/releases/tag/v13.6.1), [NuGet Aspire.Hosting index](https://api.nuget.org/v3-flatcontainer/aspire.hosting/index.json).
- **S3:** [Aspire prerequisites](https://aspire.dev/get-started/prerequisites/) (current).
- **S4:** [Aspire Keycloak integration](https://aspire.dev/integrations/security/keycloak/) (current), [hosting package index](https://api.nuget.org/v3-flatcontainer/aspire.hosting.keycloak/index.json), [13.6.1 preview package/dependencies](https://www.nuget.org/packages/Aspire.Hosting.Keycloak/13.6.1-preview.1.26506.6).
- **S5:** [Authentication package index](https://api.nuget.org/v3-flatcontainer/aspire.keycloak.authentication/index.json), [13.6.1 preview package/dependencies](https://www.nuget.org/packages/Aspire.Keycloak.Authentication/13.6.1-preview.1.26506.6).
- **S6:** [Keycloak latest release API](https://api.github.com/repos/keycloak/keycloak/releases/latest), [26.8.0 release](https://github.com/keycloak/keycloak/releases/tag/26.8.0), [official downloads](https://www.keycloak.org/downloads).
- **S7:** Aspire 13.6.1 [image defaults](https://github.com/microsoft/aspire/blob/v13.6.1/src/Aspire.Hosting.Keycloak/KeycloakContainerImageTags.cs), [resource builder implementation](https://github.com/microsoft/aspire/blob/v13.6.1/src/Aspire.Hosting.Keycloak/KeycloakResourceBuilderExtensions.cs).
- **S8:** [Aspire 13.6.1 authentication implementation](https://github.com/microsoft/aspire/blob/v13.6.1/src/Components/Aspire.Keycloak.Authentication/AspireKeycloakExtensions.cs).
- **S9:** [Keycloak database guide](https://www.keycloak.org/server/db) (current).
- **S10:** [config-cli latest release API](https://api.github.com/repos/adorsys/keycloak-config-cli/releases/latest), [6.5.1 release](https://github.com/adorsys/keycloak-config-cli/releases/tag/v6.5.1), [tagged README](https://github.com/adorsys/keycloak-config-cli/blob/v6.5.1/README.md).
- **S11:** [config-cli 6.5.1 CI matrix and image publication](https://github.com/adorsys/keycloak-config-cli/blob/v6.5.1/.github/workflows/ci.yaml), [release asset API](https://api.github.com/repos/adorsys/keycloak-config-cli/releases/tags/v6.5.1).
- **S12:** [.NET 10 standalone WASM generic authentication](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/webassembly/standalone-with-authentication-library?view=aspnetcore-10.0).
- **S13:** [.NET 10 JWT bearer authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-10.0).
- **S14:** [Keycloak 26.8.0 OIDC basic client settings](https://github.com/keycloak/keycloak/blob/26.8.0/docs/documentation/server_admin/topics/clients/oidc/con-basic-settings.adoc).
- **S15:** [Keycloak 26.8.0 audience support](https://github.com/keycloak/keycloak/blob/26.8.0/docs/documentation/server_admin/topics/clients/oidc/con-audience.adoc).
- **S16:** [Keycloak 26.8.0 token role mappings](https://github.com/keycloak/keycloak/blob/26.8.0/docs/documentation/server_admin/topics/clients/oidc/con-token-role-mappings.adoc).
- **S17:** [.NET 10 WASM additional security scenarios](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/webassembly/additional-scenarios?view=aspnetcore-10.0).
- **S18:** [.NET 10 claims mapping and transformation](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/claims?view=aspnetcore-10.0).
- **S19:** [.NET 10.0.12 WASM OIDC authentication source](https://github.com/dotnet/aspnetcore/blob/v10.0.12/src/Components/WebAssembly/WebAssembly.Authentication/src/Interop/AuthenticationService.ts).
- **S20:** [Docker Desktop network overview](https://docs.docker.com/desktop/features/networking/), [host/container networking how-tos](https://docs.docker.com/desktop/networking/).
- **S21:** [Keycloak hostname v2](https://www.keycloak.org/server/hostname) (current).
- **S22:** [W3C Secure Contexts: potentially trustworthy origins](https://www.w3.org/TR/secure-contexts/#is-origin-trustworthy).
- **S23:** [.NET 10 CORS configuration, preflight and HTTPS redirect constraints](https://learn.microsoft.com/en-us/aspnet/core/security/cors?view=aspnetcore-10.0).
- **S24:** [Aspire resource configuration and dependencies](https://aspire.dev/get-started/resources/) (current).
- **S25:** [.NET development certificate command](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-dev-certs).
- **S26:** [Keycloak 26.8.0 WebAuthn/RP policy and platform constraints](https://github.com/keycloak/keycloak/blob/26.8.0/docs/documentation/server_admin/topics/authentication/webauthn.adoc).
- **S27:** First-party registry artifacts: [Keycloak Quay repository](https://quay.io/repository/keycloak/keycloak), [adorsys published tags](https://hub.docker.com/r/adorsys/keycloak-config-cli/tags?name=6.5.1-26.5.5); [Docker imagetools inspect command](https://docs.docker.com/reference/cli/docker/buildx/imagetools/inspect/).
- **S28:** [Aspire 13.6.1 container lifetime contract](https://github.com/microsoft/aspire/blob/v13.6.1/src/Aspire.Hosting/ApplicationModel/ContainerLifetimeAnnotation.cs).
- **S29:** [Keycloak realm import/export](https://www.keycloak.org/server/importExport) (current).
- **S30:** [config-cli 6.5.1 resource management](https://github.com/adorsys/keycloak-config-cli/blob/v6.5.1/docs/MANAGED.md).
- **S31:** [config-cli 6.5.1 checksum decision logic](https://github.com/adorsys/keycloak-config-cli/blob/v6.5.1/src/main/java/de/adorsys/keycloak/config/service/checksum/ChecksumService.java).
- **S32:** [config-cli 6.5.1 substitution and checksum generation](https://github.com/adorsys/keycloak-config-cli/blob/v6.5.1/src/main/java/de/adorsys/keycloak/config/provider/KeycloakImportProvider.java).
- **S33:** [config-cli 6.5.1 actual default properties](https://github.com/adorsys/keycloak-config-cli/blob/v6.5.1/src/main/resources/application.properties) (takes precedence where README defaults differ).
- **S34:** [Aspire testing overview and random port/lifecycle behavior](https://aspire.dev/testing/overview/) (current).
- **S35:** [Testcontainers .NET first-party guide](https://testcontainers.com/guides/getting-started-with-testcontainers-for-dotnet/).
