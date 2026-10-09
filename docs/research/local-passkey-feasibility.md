# Local Keycloak passkey feasibility in Chrome on a MacBook

Research for [issue #3](https://github.com/amolenk/talk-keycloak/issues/3), part of [map #1](https://github.com/amolenk/talk-keycloak/issues/1).

## Conclusion and evidence boundary

**A local Keycloak passkey registration/sign-in demo in Chrome on a MacBook is technically feasible. Touch ID and operation without internet are plausible, but remain unvalidated on the actual presenter machine.** Supported Keycloak login-form integration, classic WebAuthn passwordless/loginless flows, and WebAuthn as a password-plus-second-factor mechanism are distinct available options. This report establishes their constraints; it does not choose the presenter journey.

No Keycloak instance, browser ceremony, Mac hardware, or offline scenario was exercised for this report. The exact Mac model, Touch ID availability, macOS version, Chrome version/profile, credential provider and policy remain unknown. Every proposed experiment below is **required evidence still to collect**, not a completed validation.

## Release reality and terminology

Research session date: 2026-10-09 (environment date). The version conclusion comes from live first-party release evidence, not inference from that date:

- `gh api repos/keycloak/keycloak/releases/latest` returned `tag_name: 26.8.0`, `published_at: 2026-10-01T05:57:20Z`, `prerelease: false`, `draft: false`. The official downloads page independently advertised **26.8.0** with downloadable server archives. Thus **26.8.0 is the latest available stable community server release observed in this research**. This does not establish compatibility with Aspire or Keycloakify; those are outside this ticket. [S1, S2]
- In tagged 26.8.0 source, `WEB_AUTHN`, `PASSKEYS`, and `RECOVERY_CODES` are `Type.DEFAULT` (supported and enabled by default). `passkeys-conditional-ui-authenticator` is deprecated. A server feature being enabled is separate from enabling passkeys for a realm. [S3]
- **26.4.0 introduced supported seamless passkeys integration** in the login forms, with both conditional and modal UI. Earlier tutorials describing only preview passkeys or requiring the separate conditional-UI authenticator are not the current supported setup. Classic WebAuthn registration and passwordless authentication existed independently of that integration. [S4, S5, S6]
- Keycloak's passkey integration uses **WebAuthn passwordless credentials**, not a separate cryptographic protocol or credential type. The stored types are `webauthn` (second factor) and `webauthn-passwordless` (passwordless). Registering the former does not automatically populate the latter. A screen saying “Passkey” alone does not identify which type is being enrolled. [S5, S6, S7]
- Passwordless means no account password is entered; loginless also avoids entering a username. Discoverable credentials enable the browser/authenticator to select an account. A passkey may be synced or device-bound; neither “passkey” nor “platform” guarantees a particular cloud provider or Touch ID. Touch ID authorizes the local private-key operation; the fingerprint and private key are not sent to Keycloak. [S6, S8, S9]

## Available Keycloak mechanisms

| Mechanism | Registration and policy | Authentication and implications |
| --- | --- | --- |
| Supported integrated passkeys | Enable **WebAuthn Register Passwordless** required action; configure **WebAuthn Passwordless Policy**. In 26.8.0, enable **Realm Settings → Login → Login Screen Customization → Enable Passkeys**. | Default username/password forms offer autofill and **Sign in with Passkey**. A successful passkey substitutes for account-password entry. Password entry remains an available route. [S5] |
| Classic username-first passwordless WebAuthn | Same passwordless required action/policy. | A custom flow can use **Username Form**, followed by alternative **WebAuthn Passwordless Authenticator** or a password-plus-second-factor subflow. This is a documented feasible mechanism when explicit flow steps are wanted. [S6] |
| Classic loginless WebAuthn | Same passwordless registration; discoverable credential and user verification required. | A flow containing required **WebAuthn Passwordless** can identify and authenticate the user without username/password. A passkey-only flow needs an independent enrollment and recovery route; do not assume an unenrolled user can bootstrap it. [S6] |
| WebAuthn MFA | **WebAuthn Register**, **WebAuthn Policy**, `webauthn` credential type. | Username/password first, then **WebAuthn Authenticator** in conditional/required 2FA. It can be an alternative to OTP via **Try Another Way**, or mandatory. This demonstrates a security-key/biometric second factor, rather than password replacement. [S6, S7] |

For integrated passkeys, the passwordless policy's current defaults require **user verification** and **discoverable credentials**. In 26.8.0, use **Discoverable Credential = required**, rather than the deprecated **Require Discoverable Credential** setting. Reasonable experimental policy parameters are RP ID matching the fixed browser-visible Keycloak host, attestation **none**, ES256 included in accepted signature algorithms, no AAGUID allow-list, and user verification **required**. `platform` attachment narrows enrollment to a platform authenticator; leaving attachment unspecified keeps USB-key alternatives available. These are candidate settings to test, not a chosen configuration. [S5, S6]

The current passwordless policy also offers **Passkey Mediation**: `conditional` (default autofill), `none` (explicit button), `optional`/`required` (automatic selection dialog), and `silent`. Browser behavior varies; silent authentication should not be relied upon for an interactive Touch ID demo. The explicit modal button is documented as the alternative when autofill does not enumerate all credentials. [S5]

Version-specific detail matters: 26.4.0 release notes locate the switch under Authentication/Policies, while 26.8.0 documentation locates activation in Realm Settings/Login and links back to passwordless policy. Use the pinned version's UI and exported configuration. Existing custom flows must be inspected; upgrading does not prove that an old copied flow has the new default behavior. [S4, S5]

## Chrome, Mac, Touch ID and offline feasibility

Google's current supported-environments documentation describes all three macOS stores below, and now identifies Google Password Manager as Chrome's default. The older 2023 iCloud integration article supplies useful minimum versions and historical UI, but its default-provider assumptions should not override the current page. [S10, S11]

| Candidate credential store | Established primary-source facts | Offline inference and remaining experiment |
| --- | --- | --- |
| Local Chrome profile | Chrome on macOS can store passkeys in a Chrome profile without syncing to other environments. The 2023 Chrome article describes local-profile enrollment/use and management. [S10, S11] | Strong candidate for a fully local rehearsal. Check that the current Chrome UI still lets this profile choose that store; test both new enrollment and sign-in with external networking disabled. Preserve the profile and OS context. |
| iCloud Keychain | Supported by **Chrome 118+ on macOS 13.5+**. Chrome needs permission; iCloud Keychain must be enabled. Apple requires Apple Account 2FA. System verification can be Touch ID, Apple Watch or system password, depending on the machine. Credentials sync across Apple devices. [S9, S11] | Complete account setup, permissions and synchronization beforehand. A locally available credential can plausibly sign a local challenge offline; the cited sources do not guarantee fresh enrollment or every provider state offline. Test cold-start local sign-in and enrollment separately. |
| Google Password Manager | Chrome on macOS supports it; passkeys sync with end-to-end encryption. Initial desktop setup asks for a GPM PIN; accessing synced passkeys in a new environment requires Google Account sign-in and Android screen lock or GPM PIN. Google illustrates Touch ID sign-in in Chrome on macOS. [S10] | Prepare/unlock the store beforehand. Do not assume sync, first-use setup, recovery or fetching a missing credential can work offline. Test the prepared profile after Chrome/Mac restart with no external network. |
| USB FIDO2 authenticator | WebAuthn supports roaming authenticators over USB; Keycloak documents FIDO2, user verification and discoverable-credential requirements for loginless use. [S6, S8] | Feasible local alternate authenticator with PIN/touch, subject to an actual compatible key and available adapter/port. Test enrollment and login; it does not provide Mac Touch ID. |

**Offline means the internet is unavailable while browser, Keycloak, database and application remain reachable locally.** The WebAuthn ceremony is challenge/signature verification between the RP and the authenticator; an external identity provider is not intrinsically required. This supports feasibility, not a guarantee about a provider's cloud/account implementation. Synced credentials already on the Mac are a different case from syncing or recovering them during the talk. [S8–S11]

Phone/QR cross-device authentication is a separate possibility supported by Chrome. It should not be assumed to be an offline fallback: the cited platform documentation does not establish an offline cross-device guarantee, and the phone adds provider and transport dependencies. Same-device credentials or a prepared USB authenticator offer fewer unverified dependencies. [S10]

Do not equate `isUserVerifyingPlatformAuthenticatorAvailable()` returning true with verified Touch ID behavior. It detects an available user-verifying platform authenticator; it does not attest to a particular biometric sensor, selected provider or presenter gesture. macOS may display a system-password alternative. Normal-profile and Incognito behavior must be verified separately; a fresh profile may not expose an existing local-profile credential. [S8, S11]

## RP ID, origin, HTTPS and local hostnames

There are three separate checks: browser secure-context/API exposure, WebAuthn RP-ID domain validation, and Keycloak's verification of the response origin. Relaxing Keycloak's realm SSL mode does not relax browser WebAuthn rules. [S6, S8, S12, S13]

1. **RP ID** is a domain string with no scheme, port or realm path. Normally it equals the caller's effective domain or a permitted registrable-domain suffix, never a public suffix. For `https://login.example.com:8443`, `login.example.com` or `example.com` can be valid; `com` or an unrelated app domain cannot. A credential remains scoped to the RP ID at enrollment. [S8]
2. **Origin** includes scheme, host and port, without the URL path. Keycloak 26.8.0 constructs its expected WebAuthn origin from its base URI and validates it alongside RP ID and challenge. Wrong reverse-proxy headers or a mismatch between configured frontend hostname and browser URL can fail even when the RP ID is valid. [S14, S15]
3. WebAuthn requires a secure context. **`http://localhost:<fixed-port>` is a documented local exception**; public/non-loopback hosts normally require trusted HTTPS. Secure Contexts also treats loopback IPs as potentially trustworthy, but this alone does not satisfy WebAuthn's domain-only RP-ID rules. Avoid substituting `127.0.0.1`/`::1` for `localhost` in a planned passkey demo. [S8, S12]
4. A hosts-file entry such as `identity.demo.test` pointing to loopback does **not** by itself make ordinary HTTP a secure context. Use trusted local HTTPS and local name resolution for such a custom name; test browser and provider acceptance. A certificate-warning click-through is not a reliable preparation strategy. `.localhost` has special Secure Contexts treatment, but use of custom subdomains still deserves browser/provider testing. [S8, S12]
5. Keycloak defaults a blank RP ID to the host of its base URL. `localhost`, `keycloak` inside a container network, and `identity.demo.test` are different host identities. Use the browser-visible frontend host for registration and login; keep container backchannel addressing separate. The passkey belongs to Keycloak's login origin, not the Blazor application's origin. [S6, S14, S15]
6. Changing ports leaves the RP ID unchanged but changes the origin. A credential may remain usable under the same RP ID if Keycloak correctly expects the new origin; OIDC issuer and redirects can still change. Changing RP ID requires re-enrollment. Realm paths do not isolate credentials at the authenticator level; realms sharing one host can share the RP ID, with separate server-side user/credential records. These are deductions from RP scoping and Keycloak validation, not tested migrations. [S6, S8, S14]

Feasible endpoint options are therefore **fixed localhost HTTP** for a same-Mac development experiment, or **fixed browser-trusted local HTTPS** (localhost or a locally resolved custom domain). The endpoint decision remains with the presenter/planning process. Keycloak SSL mode **All requests** blocks HTTP even on localhost; **External requests** permits local/private-address HTTP, while **None** is development-only. Allowing private-LAN HTTP in Keycloak does not make it WebAuthn-capable in Chrome. [S12, S13]

## Enrollment and account management

An enabled registration required action is a capability, not automatic enrollment. Registration can be initiated in several documented ways:

- Authenticate an existing user and visit `<keycloak-root>/realms/<realm>/account`, then **Account Security → Signing In → Set up a Passkey** in the **Passwordless** section. Choose the second-factor section only when enrolling `webauthn` MFA. The required action and relevant authentication mechanism must be available. [S6, S16]
- Assign **WebAuthn Register Passwordless** to a known user so that successful initial authentication leads into enrollment; optionally make it a default action for new users. Preserve a bootstrap authentication method. [S6]
- An application may invoke OIDC Application Initiated Actions using `kc_action=webauthn-register-passwordless` (or `webauthn-register` for MFA), with normal OIDC redirect/client configuration. `:skip_if_exists` can suppress repeat registration, which is useful for setup but can also skip an intended on-stage enrollment. Re-authentication and navigation must be rehearsed. [S6]

Users can manage credentials through the Account Console; administrators can label, inspect or delete a WebAuthn credential and request new registration. The passwordless Account Console documentation contains a sentence calling its login a “second factor” while also saying no password is needed; the authentication chapter and distinct credential types make clear that passwordless is a first-factor replacement. [S6, S7, S16]

For a real sign-in demonstration, terminate the Keycloak SSO session as well as the application's session. Ending only an application session can let Keycloak sign the browser back in silently. Credential deletion and session logout are different operations. [S16]

## Persistence, reset and recovery

**A working passkey needs both sides:** authenticator/provider credential state (private key and credential source) and Keycloak's user/credential record (public key, credential ID, counters and metadata). Keycloak enrollment uses its internal user ID as the WebAuthn user handle. The credential model explicitly stores the public key, not the authenticator private key. [S6–S9, S14]

Consequences below are derived from those data dependencies; exact reset workflows require experiments:

| Operation | Expected effect |
| --- | --- |
| Restart containers while retaining the Keycloak database | Registration remains, provided browser/provider credentials also remain; persistent server state is the necessary condition. A restart alone is not a clean enrollment reset. |
| Clear application storage/cookies or sign out | Removes login/session state, not intrinsically the provider's passkey or Keycloak credential. Useful for demonstrating login, insufficient for a fresh-enrollment reset. |
| Delete/reseed realm or database from a credential-free seed | Local/synced passkeys can remain offered by Chrome, but Keycloak lacks matching registration. Recreating a username is insufficient, particularly with a new internal user ID. |
| Delete the Keycloak credential | Revokes its server-side acceptance. Do not assume the browser/provider entry is also removed; check for stale autofill entries and remove through the appropriate provider UI. |
| Delete the passkey from its provider/profile | Keycloak can retain an unusable public-key record. Synced deletion may affect other devices, according to provider semantics; rehearse using demo-specific entries. |
| Restore a matching server snapshot | May restore acceptance if user ID and credential data match and the authenticator still has the key. Counter/state rollback must be tested. A configuration-only realm import cannot recreate the private key. |

Keycloak's startup `--import-realm` **skips an already existing realm**; it does not reset user credentials on each restart. Admin Console partial export does not export users. CLI export can include users but has consistency/backup limitations and excludes sessions and other state; coordinate any experiment that attempts to retain enrolled credentials through reset. Do not infer that a declarative seed or partial export preserves a physical passkey. [S17]

Feasible recovery paths, depending on the configured flow, are:

- Keep a prepared **password route**, with OTP if the user's conditional-2FA flow requires it. Cancelling or failing a passkey selection does not make a required second factor disappear. [S5, S6]
- Enroll **multiple passwordless credentials**, such as a platform passkey and a compatible USB FIDO2 key, then rehearse selection and login with each. [S5, S6]
- Configure Keycloak **recovery codes** as an alternative second-factor execution. These are currently 12 sequential one-use codes, offered through the Account Console/required action; they back up OTP/WebAuthn MFA. They are **not automatically a standalone passwordless first-factor recovery method**. [S18]
- A local administrator can restore a password route/remove a stale credential/assign re-enrollment. Email-based password recovery requires working SMTP and delivery, so its offline viability depends on prepared local mail infrastructure; it is not an automatic offline fallback. [S6, S19]
- Chrome DevTools can emulate a CTAP2 authenticator with resident keys and user verification. This is a possible visibly identified software fallback for demonstrating WebAuthn mechanics. It proves neither Touch ID nor actual provider persistence/offline behavior; disable emulation before physical-authenticator experiments. [S20]

Which fallback should be audience-visible, which account should be used, and whether to retain or re-enroll credentials between stages are human journey decisions.

## OTP interaction

- Keycloak supports **TOTP** (time plus shared secret) and **HOTP** (counter plus shared secret). Once enrolled, code generation/verification can operate locally without an internet service; TOTP needs sufficiently aligned clocks, while HOTP needs counter consistency. This offline conclusion follows from the documented algorithms. Authenticator-app/cloud setup may introduce separate dependencies. [S21]
- **Configure OTP** can be assigned as a required action or initiated through Account Console enrollment using a QR code. OTP is separate from Touch ID/user verification inside a passkey. [S16, S18, S21]
- In the current **default browser flow**, successful integrated passkey authentication skips **Browser – Conditional 2FA** via **Condition – credential**. Password-based login can still require OTP. Having an OTP credential does not mean a passkey login will automatically ask for OTP. Disabling that condition can retain 2FA after passkey login; copied/custom flows require explicit inspection. [S4, S5]
- WebAuthn MFA and OTP can be **Alternative** executions within conditional 2FA, giving **Try Another Way**. Making the subflow/authenticator **Required** changes enrollment and fallback behavior. Required actions such as Configure OTP can still create additional screens; the flow and each user's pending actions both matter. [S6, S18]

## Experiments required on the presenter machine

All items below are **unperformed**. They are a focused feasibility acceptance plan, not instructions to implement a demo during this research ticket.

1. **Inventory:** record Mac model/Touch ID hardware, macOS version, exact stable Chrome build, normal vs managed profile, credential-provider settings, available USB key/adapter, and relevant enterprise restrictions. Confirm enrollment of a fingerprint and what verification prompt actually appears.
2. **Endpoint:** choose one candidate fixed browser-visible Keycloak URL and RP ID; inspect `window.isSecureContext`, `window.PublicKeyCredential`, `isUserVerifyingPlatformAuthenticatorAvailable()` and conditional-mediation support on the actual Keycloak page. Verify origin/proxy configuration and HTTPS trust where applicable. Use registration/sign-in success as the deciding evidence, beyond feature detection.
3. **Enrollment:** with the candidate pinned stable Keycloak version, authenticate a known local user, enroll the **passwordless** type from Account Console (and AIA only if considered), record the selected provider and visible prompts. Check whether Chrome defaults to GPM, iCloud or profile storage. Confirm discoverable credentials and required user verification work.
4. **Login:** terminate SSO, test explicit modal sign-in and conditional autofill separately; verify no account password is requested and the observed Touch ID/system-password interaction. Record cancel/timeout/no-credential behavior and the actual path back to password login. Repeat with the proposed branded theme; stock-theme support does not prove custom-template/JavaScript support.
5. **Offline:** finish provider setup online, disable external network connectivity while leaving loopback services running, then test existing-key login **and new-key enrollment separately**. Repeat after Chrome restart and Mac reboot/unlock. Confirm every application/login/account-console asset is local. Record provider-specific success/failure and latency; browser “offline” simulation alone is insufficient if it also blocks local server requests or misses provider traffic.
6. **Persistence/reset:** repeat after non-destructive service restart; then on disposable demo data test the proposed database/realm reset, changed user ID, server-credential deletion, provider deletion, and re-enrollment. Confirm stale suggestions are removed and future rehearsals can reach the expected starting state.
7. **OTP/fallback:** with an OTP-enrolled account verify password→OTP versus passkey→2FA-skip behavior; inspect any custom flow. Test the selected alternate credential/password-plus-OTP/recovery-code path and cancellation before relying on it.
8. **Presentation conditions:** test the real screen-sharing/projector setup, lock/reboot state and profile/window mode. Confirm browser/OS prompts are visible to the intended audience and that the observed ceremony fits the passkey demo's time budget. No claim is made here about capture of protected system UI.

The research question can be resolved as “supported, feasible options established; machine/provider-dependent validation outstanding.” The main agent can use this report to frame the later endpoint, enrollment, reset, OTP and fallback decisions without treating them as already chosen.

## Primary sources

Keycloak source links below are pinned to the observed stable release tag. Living vendor pages can change; historical Chrome UI instructions are explicitly distinguished above.

- **S1:** [Keycloak GitHub latest-release API](https://api.github.com/repos/keycloak/keycloak/releases/latest) and [26.8.0 release](https://github.com/keycloak/keycloak/releases/tag/26.8.0). Live API values recorded above.
- **S2:** [Official Keycloak downloads](https://www.keycloak.org/downloads), observed advertising 26.8.0.
- **S3:** [26.8.0 feature classification, `Profile.java`](https://github.com/keycloak/keycloak/blob/26.8.0/common/src/main/java/org/keycloak/common/Profile.java); [feature guide](https://www.keycloak.org/server/features).
- **S4:** [26.4.0 release notes](https://github.com/keycloak/keycloak/blob/26.8.0/docs/documentation/release_notes/topics/26_4_0.adoc), supported passkeys integration and conditional credential authenticator.
- **S5:** [26.8.0 passkeys administration chapter](https://github.com/keycloak/keycloak/blob/26.8.0/docs/documentation/server_admin/topics/authentication/passkeys.adoc).
- **S6:** [26.8.0 WebAuthn administration chapter](https://github.com/keycloak/keycloak/blob/26.8.0/docs/documentation/server_admin/topics/authentication/webauthn.adoc).
- **S7:** [26.8.0 WebAuthn credential model](https://github.com/keycloak/keycloak/blob/26.8.0/server-spi/src/main/java/org/keycloak/models/credential/WebAuthnCredentialModel.java).
- **S8:** [W3C WebAuthn Level 3](https://www.w3.org/TR/webauthn-3/), especially [RP ID terminology](https://www.w3.org/TR/webauthn-3/#rp-id), [credential creation](https://www.w3.org/TR/webauthn-3/#sctn-createCredential), [RP verification](https://www.w3.org/TR/webauthn-3/#sctn-rp-operations) and [biometric privacy](https://www.w3.org/TR/webauthn-3/#sctn-biometric-privacy).
- **S9:** [Apple: About the security of passkeys](https://support.apple.com/en-us/102195), published 2024-09-16.
- **S10:** [Google: Passkey support on Android and Chrome](https://developers.google.com/identity/passkeys/supported-environments), last updated 2025-05-19 as fetched; current provider/default and macOS profile-store descriptions.
- **S11:** [Chrome: Passkeys on iCloud Keychain on macOS](https://developer.chrome.com/blog/passkeys-on-icloud-keychain), 2023-10-03; minimum versions, permissions, Touch ID/system-password UI and local-profile storage.
- **S12:** [W3C Secure Contexts §3.1](https://www.w3.org/TR/secure-contexts/#is-origin-trustworthy) and [localhost considerations](https://www.w3.org/TR/secure-contexts/#localhost).
- **S13:** [26.8.0 realm SSL modes](https://github.com/keycloak/keycloak/blob/26.8.0/docs/documentation/server_admin/topics/realms/ssl.adoc).
- **S14:** [26.8.0 WebAuthn authenticator source](https://github.com/keycloak/keycloak/blob/26.8.0/services/src/main/java/org/keycloak/authentication/authenticators/browser/WebAuthnAuthenticator.java), `getRpID` and `action` origin/user-handle validation.
- **S15:** [Keycloak hostname configuration](https://www.keycloak.org/server/hostname).
- **S16:** [26.8.0 Account Console documentation](https://github.com/keycloak/keycloak/blob/26.8.0/docs/documentation/server_admin/topics/account.adoc).
- **S17:** [Keycloak realm import/export guide](https://www.keycloak.org/server/importExport).
- **S18:** [26.8.0 recovery codes chapter](https://github.com/keycloak/keycloak/blob/26.8.0/docs/documentation/server_admin/topics/authentication/recovery-codes.adoc).
- **S19:** [26.8.0 Server Administration Guide, email and forgot password](https://www.keycloak.org/docs/26.8.0/server_admin/index.html#_email) and [forgot password](https://www.keycloak.org/docs/26.8.0/server_admin/index.html#enabling-forgot-password).
- **S20:** [Chrome DevTools: Emulate authenticators](https://developer.chrome.com/docs/devtools/webauthn).
- **S21:** [26.8.0 OTP policies](https://github.com/keycloak/keycloak/blob/26.8.0/docs/documentation/server_admin/topics/authentication/otp-policies.adoc).
