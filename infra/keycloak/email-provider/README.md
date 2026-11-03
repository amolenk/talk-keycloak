# Keycloak email webhook providers

Adapted from the supplied `gc-keycloak-poc-code/src/AppHost/KeycloakConfiguration/EmailProvider`.
The two extension points are retained, with an explicit demo contract instead of reflective
model dumps, and Keycloak's JSON serializer instead of the PoC's hand-written serializer.

- `JsonEmailTemplateProvider`: captures the identity email's meaning and data; it does not
  render the FreeMarker template identified in `bodyTemplate`.
- `ApiWebhookEmailSenderProvider`: posts the serialized body to the communication API.
- The two factories expose IDs `json` and `api-webhook` and are discovered through
  `src/main/resources/META-INF/services`.

Build from the repo root with `bash scripts/build-email-provider.sh`. The Docker build
exports `dist/keycloak-email-webhook-provider.jar`; Aspire mounts that file alongside the
independent Keycloakify theme JAR. `CityPicks:EmailMode=webhook` selects both providers;
`smtp` selects Keycloak's `freemarker` and `default` providers.

Compile against the exact server version on upgrades and run real email flows. Keycloak
26.8.0 adds `sendVerifiableCredentialOffer`, absent from the PoC's 26.6.3 interface.
The SPI is server-side Java code and requires both build-time and runtime compatibility checks.
