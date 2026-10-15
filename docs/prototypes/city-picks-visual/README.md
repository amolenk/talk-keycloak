# City Picks visual prototype (throwaway)

Question: which shared app/login visual direction should City Picks use for the conference demo?

Three structurally different directions on a single page:

- `?variant=A`: warm editorial card grid; spacious, shared split-panel login.
- `?variant=B`: compact field guide with an introduction rail and horizontal place list; narrow branded login rail.
- `?variant=C`: dark, featured-place composition; large editorial typography and a dark branded login.

Run from the repository root:

```sh
python3 -m http.server 8099 --bind 127.0.0.1 --directory docs/prototypes/city-picks-visual
```

Open <http://localhost:8099/?variant=A>. The bottom bar and left/right keys switch directions. The top lab controls select account permissions, screens and stock-style versus branded login. These controls are prototype scaffolding, not proposed application UI. This is a static-only artifact; it has no production build or route to ship it.

Suggested review:

1. Compare all three browse layouts at your usual projected browser size.
2. Open a place, choose sign-in, submit any nonempty password and explicitly save after returning. No automatic save occurs.
3. Switch to traveler + curator, add/edit a place, then choose traveler and directly select the editor to see access denied. Curator alone has no favourites permission.
4. Inspect profile: browser display claims and API-validated claims have separate panels. These are mock values, not a token decoder or API response.
5. Compare stock-style and branded login; check self-registration, invalid credentials, passkey handoff and cancellation/password recovery. Simulate passkey success to return as the same traveler with the prepared favourite.
6. Use keyboard navigation and a narrow viewport. Native HTML controls, explicit labels, visible focus, a skip link and non-colour-only state labels are included for review; accessibility compliance has not been established.

All identity, API responses, roles, authentication, passkey results and mutations are simulated. Password fields send nothing. Refresh resets all state. Account switching is visual permission exploration, not a multi-user persistence model. The stock comparison is an approximation, not an authoritative screenshot of the pinned Keycloak theme. The passkey placeholder marks where Chrome/provider UI takes over; it does not simulate or verify an authenticator. Enrollment remains in the unmodified stock Keycloak Account Console, as already decided.

Inline SVG illustrations stand in for prepared local place photography. No external fonts, images or network dependencies. Final assets need actual selected-city photos with recorded usage rights, suitable crops and meaningful alternatives where informative. App and login logo, palette and typography remain city-neutral. Theme page structure, registration fields and exact passkey/error messages must follow the pinned Keycloak/Keycloakify context rather than these mock forms. Keep shipped secondary pages working; verify the real flow later.

## Presenter verdict

The presenter selected **A — Editorial**, then confirmed all three follow-up recommendations:

- Carry A's city-neutral branding through login, self-registration and login/passkey errors using the shared Keycloakify shell/styling and selective overrides, preserving supplied behaviour and controls. Account Console stays stock; native Chrome/provider prompts stay native.
- Keep the two-panel profile and add collapsed **Demo tools** there for deliberate authorization requests with readable operation/HTTP results, including 401/403, and no raw tokens. The tools are a confirmed addition to the direction, not implemented in this mockup. Exact request sequencing belongs to the prepared-stage decision.
- Replace illustrations with prepared local place photos; retain the city-neutral mark, palette and system fonts. Bundle assets for offline use.

The matching light split-panel login is part of A. Browsing, explicit post-login saving, private favourites, role-aware curator add/edit and access-denied states follow the established application contract. This artifact records the alternatives considered; it is not final app/theme code or evidence that real Keycloak flows work.

Decision source: [Choose the City Picks visual direction and branded login experience](https://github.com/amolenk/talk-keycloak/issues/9). Keep this artifact on a throwaway branch when captured; do not promote this code into the final app/theme.
