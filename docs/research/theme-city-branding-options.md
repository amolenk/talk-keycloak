# Keycloakify theme and city-branding configuration options

Research for [Establish Keycloakify theme and city-branding configuration options](https://github.com/amolenk/talk-keycloak/issues/2), under [Find the route to an implementation-ready City Picks demo specification](https://github.com/amolenk/talk-keycloak/issues/1).

Evidence collected 2026-10-09 (release checks completed by 15:18 UTC). This is a planning report: supported alternatives, constraints and remaining experiments. No final visual direction or delivery mechanism is selected. The ticket and map Notes were read; the map requires locally prepared offline demos, coordinated Prague/Stockholm branding, a Keycloakify demonstration, and research artifacts on unmerged throwaway branches.

## Findings in brief

- The actual latest non-prerelease releases returned by GitHub's release APIs are **Keycloak 26.8.0** (published 2026-10-01) and **Keycloakify 11.16.1** (published 2026-10-02). Use these exact versions as the candidate reproducible baseline, rather than the old example versions in documentation. [S1–S2]
- Keycloakify documents Keycloak 26 support; its released source contains WebAuthn registration, authentication, errors, conditional-passkey pages, and passkeys in ordinary login forms. This establishes implemented support, not a tested guarantee for every flow on the presenter's Mac. [S3–S5]
- Native inherited CSS/resources and Keycloakify CSS/shared-template customization are both viable. Native theming alone would not fulfill the map's explicit Keycloakify stage; it remains a comparison or stock recovery option. [S6–S7; map]
- One prebuilt theme can accept public city configuration from declared server environment variables, or contain two prebuilt named variants selectable through realm/client configuration. Neither requires rebuilding to switch existing editions. New compiled assets, variants, layouts or translation defaults require rebuilding. [S8–S9]
- A custom Account Console is optional for passkey registration: Keycloak supports `kc_action=webauthn-register-passwordless` through application-initiated actions (AIA), which use the **login theme**. Credential listing/deletion is a separate Account Console concern. [S10–S12]
- Storybook is useful for visual states and accessibility work, but cannot verify server authentication, actual WebAuthn ceremonies or Touch ID. The modern single-page Account theme has **no Keycloakify Storybook integration**. [S11, S13–S14]

## 1. Releases and compatibility evidence

### Observed release snapshot

| Component | API result | Publication | Evidence |
| --- | --- | --- | --- |
| Keycloak server | `26.8.0`, `prerelease: false` | `2026-10-01T05:57:20Z` | `GET /repos/keycloak/keycloak/releases/latest` [S1] |
| Keycloakify | `v11.16.1`, `prerelease: false` | `2026-10-02T17:58:41Z` | `GET /repos/keycloakify/keycloakify/releases/latest` [S2] |
| Optional repackaged single-page Account UI | npm `latest` = `260700.0.3` | Registry snapshot, not inferred from server version | npm API and package source show upstream dependency `@keycloak/keycloak-account-ui: 26.7.0` [S15] |

The Keycloak release list also returned stable `26.7.5` dated 2026-09-30 and a separate prerelease `nightly`. Thus “latest” here means the stable release endpoint, not the first list entry or the website's release/nightly navigation label. [S1]

Source tags resolved through GitHub's commits API to:

- Keycloak `26.8.0`: `4246609cf2024c85016d3fb1254c3d2533367c31`.
- Keycloakify `v11.16.1`: `d6b857ccc15f68cbf3b8db6174652279b2c85bac`.

Keycloakify's general backward-compatibility statement goes back to Keycloak 11, but individual theme types and server features have narrower ranges. Do not infer that new passkey behavior exists on every old version. The maintainers explicitly disclaim guaranteed compatibility with future major Keycloak versions. [S3]

### Select the correct build target

For login-only or login plus **single-page** Account UI on Keycloak 26.8.0, the documented JAR is `keycloak-theme-for-kc-all-other-versions.jar` (unless renamed through build options). For a theme including the **multi-page Account v1** implementation, released compiler source instead selects range **`26.2-and-above`**, with Account v1 extension `0.7`; ranges `26.0-to-26.1`, `25`, etc. use other extension combinations. General deployment-doc JAR advice is incomplete for this case. Pin the account implementation and inspect the actual generated artifacts/typed `keycloakVersionTargets`. [S4, S16]

For the candidate baseline, pin the server image (ideally also its digest), Keycloakify, starter commit, lockfile, Node/build tools and any Account UI package. Do not assume updating Keycloakify also updates separately packaged Account UI code: npm currently repackages upstream **26.7.0**, not 26.8.0. A custom SPA must be smoke-tested against the selected server. Stock Account Console ships with the server and avoids that extra package boundary. [S11, S15; research inference]

Keycloak 26.8.0's migration notes flag redesigned identity-provider buttons and potential CSS/template incompatibilities. Inheriting native templates picks up upstream changes; copied FTL/ejected components need review. Stable version numbers alone do not remove upgrade work. [S17]

**Verification limit:** This research inspected first-party docs, APIs and released source. It did not build a theme, run Keycloak or execute Touch ID. The baseline is source-supported and a suitable smoke-test candidate, not a hardware-certified pairing.

## 2. Native themes versus Keycloakify

| Alternative | Supported customization boundary | Trade-off |
| --- | --- | --- |
| Native inherited theme | Extend a bundled parent; override CSS, images, messages, properties, footer and selected FTL templates. Login `keycloak.v2` is the current built-in theme family. | Small CSS/resource surface can keep stock page logic, with fewer frontend dependencies. Full FTL copies need upgrade maintenance. [S6, S18] |
| Keycloakify, CSS plus shared template | Keep library `DefaultPage` implementations; change `kc*` selectors/classes and shared `Template`/branding shell. | Modern tooling, Storybook, typed contexts and JAR generation. Shared styling needs checking across every visible page. [S3, S7] |
| Keycloakify, selected component overrides | Eject/customize only chosen React pages; use `DefaultPage` and default CSS for the remainder. | More design control; preserve forms, field names, action URLs, hidden fields and WebAuthn DOM/script contracts. Owned page code becomes an upgrade-maintenance surface. [S5, S7] |
| Native Quick Theme | Built-in experimental editor creates logo/color themes extending defaults, downloadable as a deployment JAR covering login/account/admin. | Useful comparison, but **experimental**, still deployment plus restart, and does not demonstrate Keycloakify. [S18–S19] |

Keycloakify generates ordinary Keycloak theme resources/FTL wrappers and loads React and serialized `window.kcContext` in the browser. It does not replace Keycloak's authentication engine, OIDC endpoints, flows, authorization rules or credential verification. The Blazor app remains a separate app; using React for identity pages does not change the chosen Blazor WASM architecture. [S5, S20; research inference]

Keycloak's Account and Admin Consoles are SPAs rendered through `index.ftl`, rather than a login-style FTL page for each route. Login required actions (profile update, password update, WebAuthn registration) remain login-theme pages even when launched from Account Console. Email is another separate theme type; native message/FTL customization remains available. [S6, S10–S12]

## 3. Login/account coverage and the passkey state surface

### Released login coverage

Keycloakify 11.16.1's `DefaultPage` dispatch includes ordinary login, username/password split pages, registration, profile update, password reset/update, verify email, OTP/TOTP, recovery codes, authenticator selection, consent, logout, informational/error and expired-page states, organization selection and broker-linking pages. Customize only audience-visible screens if desired; retain the shipped dispatcher for all other supported pages. [S5]

| Passkey-related state | Released page/support | Implication for this demo |
| --- | --- | --- |
| Register a passwordless credential | `webauthn-register.ftl`; registration script forwards challenge, RP identity, algorithms, attachment, resident-key/discoverable preference, verification and timeout | Register Passwordless and ordinary WebAuthn registration use this page family with different server policies. Label prompt is browser-side behavior too. [S5, S10, S21] |
| Explicit WebAuthn authentication | `webauthn-authenticate.ftl` | Available for WebAuthn/passwordless flow executions; layout is theme-controlled, cryptographic verification remains server-controlled. [S5, S10] |
| Passkey on combined login | `login.ftl` uses `autocomplete="username webauthn"`, hidden assertion form, explicit button and passkey initialization hook | Covers conditional/autofill and explicit modal UI while password login remains available when the flow permits it. [S22] |
| Username-only or password/reauthentication form | `login-username.ftl` and `login-password.ftl` plus corresponding passkey hooks | Include if the chosen flow uses split steps or reauthentication; cannot validate only the combined screen. [S5, S23] |
| Separate conditional authenticator page | `login-passkeys-conditional-authenticate.ftl` | Implemented, but the separate server authenticator feature is **deprecated** in 26.8.0; prefer evaluating integrated forms first. [S18, S24] |
| Error, retry, AIA cancel | `webauthn-error.ftl`, `error.ftl`, `info.ftl`; shipped error stories include retry/AIA cancel | Review cancellation, failed assertion, duplicate/unavailable credential and timeout transitions in real Keycloak. Exact page depends on flow/failure. [S5, S25] |
| Try another way / expired login | `select-authenticator.ftl`, `login-page-expired.ftl`, shared template links | Keep escape/restart paths usable; a missing native prompt is not evidence that the custom theme is broken. [S5, S10] |

The 26.8.0 source classifies **WebAuthn and passkeys as default supported features**, while the separate conditional-UI authenticator is deprecated. Realm activation still needs the **Enable Passkeys** switch plus the enabled **WebAuthn Register Passwordless** required action and passwordless policy. Conditional, none, optional, required and silent mediation settings are documented; ordinary Keycloakify login's released hook forwards `mediation` and `authenticatorAttachment` to the Keycloak-provided JS modules. [S18, S22, S26]

The dedicated older conditional page hook has a smaller parameter contract than the integrated form hook (no explicit mediation/attachment forwarding); do not treat the two as interchangeable for all new policy options. [S22, S24]

Keycloak's default browser flow skips conditional 2FA after a passkey login using the credential condition. Theme selection cannot alter this. If the talk wants a different authentication policy or OTP prominence, that remains a flow decision. [S26]

### Account options

1. **Keep stock Account Console; brand login required actions.** AIA registration can be triggered using the existing OIDC authorization flow with `kc_action=webauthn-register-passwordless`. Standard OIDC state/nonce/PKCE/redirect handling must still be preserved. This avoids building an Account theme when the app's profile page only inspects claims. [S10–S11; research inference]
2. **Customize modern single-page Account UI.** React-only Keycloakify integration, initialized through `npx keycloakify initialize-account-theme`. It uses the separately packaged upstream Account UI, PatternFly, react-router and i18next; own only files being changed. No Keycloakify Storybook integration; use a real server and instant-refresh dev workflow. [S11–S12, S15]
3. **Use legacy multi-page Account UI.** Storybook/CSS support and familiar login-like programming model, but fewer modern features out of the box and a Keycloakify-maintained Java extension relying on internal Keycloak APIs. Do not assume complete current passkey management; missing features require Account REST API work. This is an additional maintenance surface. [S11, S16]

Current upstream Account Console `SigningIn.tsx` lists credential metadata, invokes required actions to add credentials, and uses `delete_credential:<id>` to remove credentials through login actions. Registration/deletion confirmation pages can therefore cross from account styling into login styling. An app claims page, Account Console and registration page are distinct visible surfaces. [S27]

### Browser/OS boundary

Themes control the page around the ceremony, not Chrome's credential chooser, OS passkey storage dialogs or Touch ID prompt. RP display name is configurable public identity; **RP ID, issuer hostname and origin are credential/security boundaries**, not cosmetic city settings. Keep them stable across rehearsals unless deliberately testing a new credential scope. WebAuthn requires a secure context; localhost can be potentially trustworthy, but arbitrary local HTTP hostnames should not be assumed equivalent. Rehearse the exact external URL used by the browser. [S10, S26, S28]

Touch ID success and offline credential-provider behavior remain machine/browser/provider facts to verify. Neither official theme support nor a Storybook screenshot proves them. Synced and cross-device credentials can have external dependencies; prepare an offline-tested on-device/USB alternative and password fallback if the selected flow permits it. [S26, S28; research recommendation]

## 4. Build, deploy and reload mechanics

### Prepared artifact path

1. Build with the starter's `npm run build-keycloak-theme`; artifacts appear in `dist_keycloak`. Node dependencies and Maven/JDK build prerequisites must be prepared in advance. [S4, S14]
2. Choose the artifact for the selected Account implementation/server range (section 1). Copy or mount it into `/opt/keycloak/providers/`. [S4, S16]
3. For an optimized custom image, place provider JARs **before** `kc.sh build`; start the completed image with `start --optimized`. Replacing a JAR in an optimized image may require server reaugmentation/rebuild. Official docs describe a Docker JAR-mtime detection pitfall. [S29]
4. Configure the application realm's `loginTheme` and, if needed, `accountTheme`, or a client's login-theme override. Account theme selection is realm-level. Client login overrides can cause the app login and Account Console reauthentication to differ; configure/check the `account-console` client if using overrides. [S4, S6]

Aspire can host a pinned container with the prepared artifact via image or mount; this report establishes the Keycloak boundary, not a final Aspire implementation. A baked image reduces moving parts on stage; a local JAR mount makes artifacts easy to inspect but still needs a tested restart/rebuild procedure. [S29; research alternatives]

### Reload is not one mechanism

| Change | Rebuild? | Reload/restart boundary |
| --- | --- | --- |
| Select another already-deployed theme/variant | No | Save realm/client config; start/refresh an authorization request and verify selected client. [S4, S9] |
| Change declared public server env value | No theme rebuild | Recreate/restart the Keycloak process/container with new env; it is runtime substitution, not live env mutation. [S8; process-boundary inference] |
| Native exploded CSS/FTL/resources | No frontend/JAR build for direct edits | Disable theme/static/template caches during development; refresh. [S6] |
| Keycloakify React/CSS/assets | Yes, prepared JAR path | Rebuild artifact, redeploy, restart server (and reaugment optimized server as appropriate). [S4, S6, S29] |
| Keycloakify dev-container edits | Tool performs rebuild/update | `npx keycloakify start-keycloak` watches/rebuilds login theme; refresh browser. Account SPA has instant-refresh development integration. [S12, S14] |

Native development flags documented in the **26.8.0 source** are:

```text
--spi-theme--static-max-age=-1
--spi-theme--cache-themes=false
--spi-theme--cache-templates=false
```

These facilitate exploded theme development; they are not a promise of arbitrary in-place JAR hot swapping. Keycloak also documents `data/tmp/kc-gzip-cache` for stale compressed-resource cache cleanup. A no-cache development setup and a prebuilt stage artifact are separate supported workflows. [S6]

The Keycloakify helper defaults to a separate prepared test realm, persists settings in `.keycloakify`, and advertises an external test app at `https://my-theme.keycloakify.dev`. It is useful while developing, but that hosted app is an internet dependency. The conference path should authenticate through the prepared local City Picks client. The helper supports explicit `dockerImage`, `realmJsonFilePath`, local `extensionJars`, ports and extra arguments; pin these instead of relying on interactive/default version selection. [S14, S30]

## 5. Storybook: what it establishes

`npx keycloakify add-story` imports selected page stories; `npm run storybook` previews them locally with partially overridden mock contexts. Useful fixtures include both city editions; long names and translated copy; invalid credentials/profile validation; registration; WebAuthn register/authenticate/error; unsupported-browser copy; expired session; retry/cancel; authenticator selection; light/dark and narrow/zoomed layouts. Dedicated WebAuthn/passkey stories exist in the released tree. [S13, S25, S31]

These fixtures establish layout and rendering behavior for supplied data. They do not establish valid server context, flow configuration, form submission, session expiry, redirect handling, credential verification, native prompts or Touch ID. Even a story named “JavaScript disabled” still runs inside a JavaScript UI; it only mocks warning copy. Test the built artifact inside real Keycloak after Storybook work. [S14, S20, S25; research inference]

Keep the normal dispatcher intact when testing real login: forcing a mock page/context can make real authentication impossible. Single-page Account UI is tested in the real-server dev workflow, not this Storybook loop. [S12–S14]

## 6. Coordinating Prague Picks and Stockholm Picks

The following are architecture options assembled from supported primitives. Keycloakify does not supply automatic Blazor/seed-data synchronization. A configuration adapter must be implemented once; subsequent edition changes can then avoid application-code edits.

| Option | Login configuration | App/seed coordination | Constraints |
| --- | --- | --- | --- |
| **A. One JAR with declared runtime public values** | Declare e.g. `CITY_EDITION`, title/copy/palette keys in Keycloakify `environmentVariables`; read `kcContext.properties`. Bundle both cities' assets and choose by allowlisted edition. | One local edition file feeds orchestration env for Keycloak, a public runtime JSON/settings response for WASM, and the seed loader. | No rebuild for existing assets/values. Restart Keycloak on env change; public app settings need reload/cache handling. Server-wide env does not inherently provide distinct values for simultaneous realms/clients. [S8, S32; design inference] |
| **B. Two prebuilt theme variants in one JAR** | Build `themeName: ["prague-picks", "stockholm-picks"]`; use `kcContext.themeName` and variant translations/styles. Select via realm or client. | Edition file maps to installed theme name, app public config and seed set. | Runtime selection without new build; adding a new variant/asset/default copy needs a build. Client overrides cover login, while Account selection remains realm-level. [S4, S9] |
| **C. Public local branding JSON fetched by theme and app** | Theme has a compiled public URL/env property; fetch schema-validated branding from a prepared local host. | Both consume the same document; seed loader can consume the associated seed selector. | Feasible frontend composition, not a built-in Keycloakify branding service. Adds availability, CORS/CSP, startup/error/loading and cache coordination; login must render a bundled default if host/JSON fails. Avoid requiring a running application service just to render login. [S8, S20, S32; design inference] |
| **D. Build-time edition artifact(s)** | Generate theme resources from edition config and build JAR(s). | Generate app public config/assets and choose seeds from the same input. | Meets “without application-code edits,” but edition change requires generation/build/redeploy. Strong offline artifact story; weaker live-switch story. [S4, S9; design inference] |

Native themes support environment substitution in custom `theme.properties` values too, so A's basic server-side substitution is not proprietary to Keycloakify. Shared CSS variables, SVG/logo files, locally licensed fonts and an edition manifest can be reused across React and Blazor without sharing component code. The asset paths still differ: app web root versus Keycloak theme resource URLs. [S6, S33; design inference]

Blazor WASM .NET 10 loads public `wwwroot/appsettings.json` and environment-specific settings, or can explicitly fetch another JSON config before startup. Server container env does **not** automatically become .NET 10 WASM configuration: generate/serve a public file or endpoint. The fetched Learn document includes environment-variable guidance scoped to .NET 11/12, which must not be mistaken for .NET 10 support. PWA/service-worker caching can prevent between-deployment edits from taking effect. [S32]

Keep display identity, introductory copy, palette/asset selector and city seed selection in the public edition boundary. Bootstrap/admin passwords, database credentials, signing keys and confidential-client secrets belong in separate server-only configuration. `kcContext.properties`, browser-fetched JSON and WASM web-root settings are browser-visible; an environment variable becomes public when explicitly transferred into the theme. Do not transfer an entire server environment or embed arbitrary HTML/asset URLs from unvalidated input. [S8, S20, S32; implementation constraint]

Realm display name and translation overrides can supply some runtime text, but are not a complete shared city configuration mechanism. In particular, **arbitrary realm attributes are not automatically available**: released context serialization excludes `realmAttributes` and `realm.attributes`, and the current FAQ's links are broken. Using them would need explicit allowlisted context/provider work plus verification, rather than assuming `kcContext.realm.attributes.city` exists. Declared theme env values or named variants have clearer documented support. [S20, S34]

## 7. Local assets, accessibility and fallback

### Offline resource boundary

- Keycloakify documents self-hosted fonts bundled in the theme JAR; native themes serve resources from their own `resources` directories. Import/bundle local logo, city illustrations, font files and icon assets. Do not edit `public/keycloakify-dev-resources` expecting deployment changes: those simulate development resources and are not the actual theme. [S6–S7, S33]
- A further documented asset-only alternative uses predictable `public/img/logo.png` paths with Vite's `import.meta.env.BASE_URL` (or Webpack's Keycloakify `PUBLIC_URL`) so resource URLs are rewritten correctly. The logo guide describes replacement under an **exploded** `themes/<name>/login/resources/dist/img/logo.png` layout without recompiling. This is a mutable filesystem/cache workflow, not automatic replacement inside a deployed immutable JAR; it needs an exploded deployment or an explicitly tested resource override. New imported/content-hashed compiled assets still require a build. [S6, S20, S33]
- Build and cache dependencies/artifacts/images beforehand. Rehearse disconnected with browser cache cleared and inspect network requests for external fonts, images, analytics, hosted helper pages and provider requests. Offline here means the local identity server/app still run; bundled assets do not enable authentication with Keycloak stopped. [S14, S29, S33; research recommendation]
- For unavailable optional images/fonts, keep readable text branding, a system font stack and a stable background. For option C, include bundled valid default branding, an allowlisted edition selector and an explicit handling policy for missing/malformed config. These are proposed requirements, not built-in guarantees. [design recommendation]
- A realm/database reset can invalidate the server's record of a passkey while leaving the authenticator credential behind. Credential storage and server storage are distinct; resetting a theme cannot restore their association. Establish a reset/re-registration procedure in the presenter workflow. [S10, S28; research inference]

### Accessibility implications

Keycloakify's released ordinary login includes explicit labels, password visibility `aria-label`, `aria-invalid`, live error messages and appropriate autocomplete tokens. Preserve those semantics when redesigning. Removing PatternFly CSS/classes does not itself preserve usable focus, contrast or layout. [S7, S22]

For every city palette and visible passkey/error page, use WCAG 2.2 as the verification basis: text alternatives; 4.5:1 normal-text/3:1 large-text contrast; 3:1 applicable non-text controls; keyboard operation and visible/unobscured focus; logical focus order; labels/error association and announced status; 200% text resize and reflow; usable targets; accessible authentication without blocking password-manager/paste assistance. Verify manually as well as with automated checks. This is a bounded theme acceptance surface, not a claim of automatic WCAG compliance. [S35]

Provide clear password/alternative-method and cancel/retry routes where the chosen flow permits them; avoid copy promising fingerprint-only login because authenticators can use other user-verification gestures. The browser/OS owns ceremony accessibility; the page still owns launch controls, instructions and recovery. [S10, S26, S28]

### Three different fallbacks

1. **Uncustomized supported pages:** use Keycloakify `DefaultPage` with default CSS for pages not redesigned. This is the library's implementation, not a server-rendered stock FTL fallback. [S5, S7]
2. **Native inheritance:** native themes resolve resources/templates through parents. Missing templates can inherit, but a broken existing override does not become a reliable automatic recovery mechanism. New extension pages are not automatically covered by Keycloakify; register their contexts/pages explicitly or verify inherited behavior. [S6, S36; research inference]
3. **Presenter recovery:** keep the built-in login theme available and a rehearsed realm/client setting change to stock `keycloak.v2`. Check client overrides as well as realm selection. This can restore stock UI independently of the custom JAR, but cannot repair a broken authentication flow, wrong RP ID or missing credential. Account can remain stock throughout. [S4, S6, S18; research recommendation]

The generated Keycloakify wrapper depends on JavaScript to render its UI; there is no evidence here of automatic stock rendering when its JS fails or is disabled. Preserve that distinction in the recovery story. [S20]

## 8. Questions for subsequent decisions and concrete verification

The external feasibility question is resolved: both city editions, locally packaged branding and the selected passkey page families have supported implementation routes. The human/implementation decisions remaining are:

- How extensive should the audience-visible redesign be: CSS/shared shell or owned page components? Which secondary/error screens need matching polish?
- Is passkey enrollment sufficient through app-initiated registration, or must a branded Account Console list/delete credentials on stage?
- Must city switching work during a running talk, or is an edition selected before startup enough? Are two editions ever simultaneous on one server? Choose A/B/C/D accordingly.
- Which single public edition schema/input should coordinate copy, app/login assets and seeds? What should missing/unknown edition config do?
- Which passkey entry experience (conditional autofill versus explicit button) and password/OTP fallback fits the narrative? These are flow/UX choices, not theme facts.
- Confirm Mac model, macOS, Chrome version/profile, credential provider, RP origin/ID and offline Touch ID behavior. Confirm recovery after database reset and credential cleanup.

Before pinning the final baseline, a small real-Keycloak smoke test should prove: correct JAR selected and loaded; both edition configurations; no external asset requests with empty browser cache; login success/invalid credentials; passkey AIA registration and return; logout followed by conditional and explicit passkey login; cancellation/error/retry/password fallback; session-expired screen; Account list/delete if selected; and restart/redeploy/stock recovery. If a custom Account SPA is selected, include its currently upstream-26.7.0 package against server 26.8.0. Capture exact artifact versions/hashes and the actual presenter's environment. This is proposed acceptance evidence, not work performed in this research.

## Sources

First-party documentation is mutable unless a version or commit appears below. Release/source claims use released tags or resolved commits; API results are the observed snapshot above. Design recommendations and composition options are explicitly identified rather than represented as vendor guarantees.

- **S1** [Keycloak latest release API](https://api.github.com/repos/keycloak/keycloak/releases/latest), [release list API](https://api.github.com/repos/keycloak/keycloak/releases), [26.8.0 release](https://github.com/keycloak/keycloak/releases/tag/26.8.0), [resolved commit API](https://api.github.com/repos/keycloak/keycloak/commits/26.8.0).
- **S2** [Keycloakify latest release API](https://api.github.com/repos/keycloakify/keycloakify/releases/latest), [11.16.1 release](https://github.com/keycloakify/keycloakify/releases/tag/v11.16.1), [resolved commit API](https://api.github.com/repos/keycloakify/keycloakify/commits/v11.16.1).
- **S3** [Keycloakify introduction/compatibility and framework boundaries](https://docs.keycloakify.dev/readme.md).
- **S4** [Keycloakify deployment and JAR naming](https://docs.keycloakify.dev/deploying-your-theme.md), [version-target configuration](https://docs.keycloakify.dev/features/compiler-options/keycloakversiontargets.md).
- **S5** [Released login page dispatcher](https://github.com/keycloakify/keycloakify/blob/d6b857ccc15f68cbf3b8db6174652279b2c85bac/src/login/DefaultPage.tsx).
- **S6** [Keycloak 26.8.0 native theme guide source: inheritance, properties, cache flags, deployment, selectors](https://github.com/keycloak/keycloak/blob/4246609cf2024c85016d3fb1254c3d2533367c31/docs/guides/ui-customization/themes.adoc).
- **S7** [Keycloakify CSS and per-page default-style fallback](https://docs.keycloakify.dev/css-customization.md).
- **S8** [Runtime environment variables, explicit declaration and `kcContext.properties`](https://docs.keycloakify.dev/features/environment-variables.md).
- **S9** [Theme variants and variant-specific translations](https://docs.keycloakify.dev/features/theme-variants.md).
- **S10** [Keycloak 26.8.0 WebAuthn policies, registration and AIA source](https://github.com/keycloak/keycloak/blob/4246609cf2024c85016d3fb1254c3d2533367c31/docs/documentation/server_admin/topics/authentication/webauthn.adoc).
- **S11** [Keycloakify Account implementations, dependencies and Storybook limits](https://docs.keycloakify.dev/theme-types/account-theme.md).
- **S12** [Single-page Account initialization, ownership and real-server preview](https://docs.keycloakify.dev/theme-types/account-theme/single-page.md).
- **S13** [Storybook mock contexts and add-story workflow](https://docs.keycloakify.dev/testing-your-theme/outside-of-keycloak.md).
- **S14** [Real-Keycloak helper, Maven/Docker prerequisites, rebuild/refresh and hosted test app](https://docs.keycloakify.dev/testing-your-theme/inside-of-keycloak.md).
- **S15** [Account UI npm latest API](https://registry.npmjs.org/@keycloakify/keycloak-account-ui/latest), [observed package source and upstream 26.7.0 dependency](https://github.com/keycloakify/keycloak-account-ui/blob/b1984f684a9a221615ef9fb3ade7c534d77f7922/package.json).
- **S16** [Released compiler range selection](https://github.com/keycloakify/keycloakify/blob/d6b857ccc15f68cbf3b8db6174652279b2c85bac/src/bin/keycloakify/buildJars/getKeycloakVersionRangeForJar.ts), [multi-page implementation detection](https://github.com/keycloakify/keycloakify/blob/d6b857ccc15f68cbf3b8db6174652279b2c85bac/src/bin/keycloakify/buildJars/buildJars.ts).
- **S17** [Keycloak 26.8.0 migration source: redesigned provider buttons](https://github.com/keycloak/keycloak/blob/4246609cf2024c85016d3fb1254c3d2533367c31/docs/documentation/upgrading/topics/changes/changes-26_8_0.adoc).
- **S18** [Keycloak released feature classifications: login v2, WebAuthn/passkeys, deprecated conditional authenticator, experimental Quick Theme](https://github.com/keycloak/keycloak/blob/4246609cf2024c85016d3fb1254c3d2533367c31/common/src/main/java/org/keycloak/common/Profile.java).
- **S19** [Quick Theme released documentation source](https://github.com/keycloak/keycloak/blob/4246609cf2024c85016d3fb1254c3d2533367c31/docs/guides/ui-customization/quick-theme.adoc).
- **S20** [Generated FTL wrapper and asset rewriting](https://github.com/keycloakify/keycloakify/blob/d6b857ccc15f68cbf3b8db6174652279b2c85bac/src/bin/keycloakify/generateFtl/generateFtl.ts), [context serialization, public properties and realm-attribute exclusions](https://github.com/keycloakify/keycloakify/blob/d6b857ccc15f68cbf3b8db6174652279b2c85bac/src/bin/keycloakify/generateFtl/kcContextDeclarationTemplate.ftl).
- **S21** [Released WebAuthn registration script contract](https://github.com/keycloakify/keycloakify/blob/d6b857ccc15f68cbf3b8db6174652279b2c85bac/src/login/pages/WebauthnRegister.useScript.tsx).
- **S22** [Released combined login implementation](https://github.com/keycloakify/keycloakify/blob/d6b857ccc15f68cbf3b8db6174652279b2c85bac/src/login/pages/Login.tsx), [passkey initialization hook](https://github.com/keycloakify/keycloakify/blob/d6b857ccc15f68cbf3b8db6174652279b2c85bac/src/login/pages/Login.useScript.tsx).
- **S23** [Released login pages directory](https://github.com/keycloakify/keycloakify/tree/d6b857ccc15f68cbf3b8db6174652279b2c85bac/src/login/pages).
- **S24** [Released separate conditional-page script](https://github.com/keycloakify/keycloakify/blob/d6b857ccc15f68cbf3b8db6174652279b2c85bac/src/login/pages/LoginPasskeysConditionalAuthenticate.useScript.tsx).
- **S25** [Released error-state stories](https://github.com/keycloakify/keycloakify/blob/d6b857ccc15f68cbf3b8db6174652279b2c85bac/stories/login/pages/WebauthnError.stories.tsx).
- **S26** [Keycloak released passkey forms, mediation, setup and environment limits](https://github.com/keycloak/keycloak/blob/4246609cf2024c85016d3fb1254c3d2533367c31/docs/documentation/server_admin/topics/authentication/passkeys.adoc).
- **S27** [Upstream 26.8.0 Account Signing In credential management](https://github.com/keycloak/keycloak/blob/4246609cf2024c85016d3fb1254c3d2533367c31/js/apps/account-ui/src/account-security/SigningIn.tsx).
- **S28** [W3C WebAuthn Level 3: API, RP scopes, user-agent mediation, secure context/origin validation and accessibility](https://www.w3.org/TR/2026/REC-webauthn-3-20260825/), [W3C Secure Contexts editor's draft, potentially trustworthy origins (work in progress)](https://w3c.github.io/webappsec-secure-contexts/#is-origin-trustworthy).
- **S29** [Official container build, providers, optimized start and timestamp pitfall](https://www.keycloak.org/server/containers).
- **S30** [Keycloakify test-container pin/configuration options](https://docs.keycloakify.dev/features/compiler-options/startkeycloakoptions.md).
- **S31** [Released Storybook login pages](https://github.com/keycloakify/keycloakify/tree/d6b857ccc15f68cbf3b8db6174652279b2c85bac/stories/login/pages).
- **S32** [Microsoft Blazor configuration (.NET 10), public files, custom JSON and caching](https://learn.microsoft.com/en-us/aspnet/core/blazor/fundamentals/configuration?view=aspnetcore-10.0), [observed docs source with version-scoped sections](https://github.com/dotnet/AspNetCore.Docs/blob/cb8ebc40c6f1ad3729f05670528384e64899b347/aspnetcore/blazor/fundamentals/configuration.md).
- **S33** [Keycloakify self-hosted fonts bundled in JAR](https://docs.keycloakify.dev/common-use-case-examples/custom-fonts.md), [local logo import](https://docs.keycloakify.dev/common-use-case-examples/adding-your-logo.md).
- **S34** [Current realm-attributes FAQ with broken references](https://docs.keycloakify.dev/faq/accessing-the-realm-attributes.md); use released serialization source S20 for the actual exclusion behavior.
- **S35** [W3C WCAG 2.2 quick reference](https://www.w3.org/WAI/WCAG22/quickref/), specifically 1.1.1, 1.4.3/4/10/11, 2.1.1, 2.4.3/7/11, 2.5.8, 3.3.1/2/8 and 4.1.3.
- **S36** [Additional extension pages need explicit Keycloakify integration](https://docs.keycloakify.dev/features/styling-a-custom-page-not-included-in-base-keycloak.md).
