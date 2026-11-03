using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);
var databasePassword = builder.AddParameter("citypicks-db-password", "demo", secret: true);
var databaseUser = builder.AddParameter("citypicks-db-user", "citypicks");
var keycloakDatabasePassword = builder.AddParameter("keycloak-db-password", "demo", secret: true);
var keycloakAdminPassword = builder.AddParameter("keycloak-admin-password", "demo", secret: true);
var keycloakAdmin = builder.AddParameter("keycloak-admin", "admin");
var travelerPassword = builder.AddParameter("traveler-password", "demo", secret: true);
var curatorPassword = builder.AddParameter("curator-password", "demo", secret: true);
var bffSecret = builder.AddParameter("bff-client-secret", "city-picks-local-demo", secret: true);

var volumeName = builder.Configuration["CityPicks:Volume"] ?? "city-picks-postgres";
var cityDirectory = RepoPath(builder.Configuration["CityPicks:CityDirectory"] ?? "content/cities/prague");
if (builder.Environment.IsEnvironment("Testing") && !volumeName.StartsWith("city-picks-test-", StringComparison.Ordinal))
{
    throw new InvalidOperationException("Testing AppHosts require a unique city-picks-test-* volume name.");
}

var postgres = builder.AddPostgres("postgres", databaseUser, databasePassword)
    .WithImageTag("17.11-bookworm")
    .WithEnvironment("KEYCLOAK_DB_PASSWORD", keycloakDatabasePassword)
    .WithInitFiles(RepoPath("infra/postgres"))
    // Select the PostgreSQL 17 data path before the digest pin clears the tag.
    .WithDataVolume(volumeName)
    .WithImageSHA256("3645570cccdfa447589da9f57dd740faa29b30938e861289a5574b6ca6b03826");

var appDatabase = postgres.AddDatabase("citypicks", "citypicks");
var favouritesDatabase = postgres.AddDatabase("favourites", "favourites");
var keycloakDatabase = postgres.AddDatabase("keycloak-db", "keycloak");

// Local SMTP catcher: Keycloak sends realm emails here and the web UI shows them.
var mailpit = builder.AddContainer("mailpit", "axllent/mailpit", "v1.31.6")
    .WithImageSHA256("23fe39e81bde596a918000b3135c0f85dc8bb4c9ea6be274b051143f3e300387")
    .WithHttpEndpoint(port: 8025, targetPort: 8025, name: "http")
    .WithEndpoint(targetPort: 1025, name: "smtp", scheme: "tcp")
    .WithHttpHealthCheck("/livez", endpointName: "http");
var smtpEndpoint = mailpit.GetEndpoint("smtp");

var communication = builder.AddProject<Projects.CityPicks_Communication>("communication")
    .WithHttpEndpoint(name: "http")
    .WithEnvironment("Smtp__Host", smtpEndpoint.Property(EndpointProperty.Host))
    .WithEnvironment("Smtp__Port", smtpEndpoint.Property(EndpointProperty.Port))
    .WithHttpHealthCheck("/health", endpointName: "http")
    .WaitFor(mailpit);

var emailMode = builder.Configuration["CityPicks:EmailMode"] ?? "smtp";
if (emailMode is not ("smtp" or "webhook"))
    throw new InvalidOperationException("CityPicks:EmailMode must be smtp or webhook.");
var emailProviderJar = RepoPath("infra/keycloak/email-provider/dist/keycloak-email-webhook-provider.jar");
if (!File.Exists(emailProviderJar))
    throw new InvalidOperationException("Email provider JAR missing. Run bash ./scripts/build-email-provider.sh.");

var themeDirectory = RepoPath("infra/keycloak/theme/dist_keycloak");
if (!File.Exists(Path.Combine(themeDirectory, "city-picks.jar")))
    throw new InvalidOperationException("Keycloak theme JAR missing. Run ./scripts/build-theme.sh before starting.");

// Stable ports (8443, 7100, 8025) are for the dev stack. These endpoints are proxied, so the
// testing builder replaces them with random ports and tests can run next to the dev stack.
var keycloak = builder.AddKeycloak("keycloak", port: 8443, adminUsername: keycloakAdmin, adminPassword: keycloakAdminPassword)
    .WithImageTag("26.8.0")
    .WithBindMount(Path.Combine(themeDirectory, "city-picks.jar"), "/opt/keycloak/providers/city-picks.jar", isReadOnly: true)
    .WithBindMount(emailProviderJar, "/opt/keycloak/providers/keycloak-email-webhook-provider.jar", isReadOnly: true)
    .WithEnvironment("KC_SPI_EMAIL_TEMPLATE__PROVIDER", emailMode == "webhook" ? "json" : "freemarker")
    .WithEnvironment("KC_SPI_EMAIL_SENDER__PROVIDER", emailMode == "webhook" ? "api-webhook" : "default")
    .WithEnvironment("KEYCLOAK_EMAIL_WEBHOOK_URL", ReferenceExpression.Create($"{communication.GetEndpoint("http")}/api/keycloak-email"))
    // Keep an internal HTTP listener for config-cli; browser traffic uses HTTPS.
    .WithHttpEndpoint(targetPort: 8080, name: "config")
    .WithEnvironment("KC_DB", "postgres")
    .WithEnvironment("KC_DB_USERNAME", "keycloak")
    .WithEnvironment("KC_DB_PASSWORD", keycloakDatabasePassword)
    .WithEnvironment("KC_DB_URL", keycloakDatabase.Resource.JdbcConnectionString)
    .WaitFor(keycloakDatabase);

// The integration's primary endpoint is named "http"; it serves HTTPS once the dev certificate is configured.
var keycloakUrl = keycloak.GetEndpoint("http");
var realmIssuer = ReferenceExpression.Create($"{keycloakUrl}/realms/city-picks");
// The URL as the browser sees it. Containers resolve plain references to the container network instead.
keycloak.WithEnvironment("KC_HOSTNAME", keycloak.GetEndpoint("http", KnownNetworkIdentifiers.LocalhostNetwork));

if (emailMode == "webhook") keycloak.WaitFor(communication);

var keycloakConfig = builder.AddContainer("keycloak-config", "adorsys/keycloak-config-cli", "6.5.1-26.5.5")
    .WithBindMount(RepoPath(builder.Configuration["CityPicks:KeycloakConfigDirectory"] ?? "infra/keycloak/config"), "/config", isReadOnly: true)
    .WithEnvironment("KEYCLOAK_URL", keycloak.GetEndpoint("config"))
    .WithEnvironment("KEYCLOAK_USER", keycloakAdmin)
    .WithEnvironment("KEYCLOAK_PASSWORD", keycloakAdminPassword)
    .WithEnvironment("IMPORT_FILES_LOCATIONS", "/config/*.yaml")
    .WithEnvironment("IMPORT_VARSUBSTITUTION_ENABLED", "true")
    .WithEnvironment("IMPORT_CACHE_ENABLED", "false")
    .WithEnvironment("IMPORT_MANAGED_CLIENT", "no-delete")
    .WithEnvironment("IMPORT_MANAGED_ROLE", "no-delete")
    .WithEnvironment("IMPORT_MANAGED_GROUP", "no-delete")
    .WithEnvironment("IMPORT_BEHAVIORS_REMOVEDEFAULTROLEFROMUSER", "true")
    .WithEnvironment("CITYPICKS_TRAVELER_PASSWORD", travelerPassword)
    .WithEnvironment("CITYPICKS_CURATOR_PASSWORD", curatorPassword)
    .WithEnvironment("CITYPICKS_BFF_SECRET", bffSecret)
    // Keycloak connects to Mailpit over the container network, so pass the container-side address.
    .WithEnvironment("CITYPICKS_SMTP_HOST", smtpEndpoint.Property(EndpointProperty.Host))
    .WithEnvironment("CITYPICKS_SMTP_PORT", smtpEndpoint.Property(EndpointProperty.Port))
    .WaitFor(keycloak);

var initializer = builder.AddProject<Projects.CityPicks_Api>("initializer")
    .WithArgs("--initialize")
    .WithReference(appDatabase)
    .WithEnvironment("CityPicks__CityDirectory", cityDirectory)
    .WaitFor(appDatabase);

var placesApi = builder.AddProject<Projects.CityPicks_Api>("places-api")
    .WithReference(appDatabase)
    .WithEnvironment("CityPicks__CityDirectory", cityDirectory)
    .WithEnvironment("CityPicks__RealmIssuer", realmIssuer)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithHttpsEndpoint(name: "https")
    .WithHttpHealthCheck("/health", endpointName: "https")
    .WaitFor(postgres)
    .WaitFor(keycloak)
    .WaitFor(mailpit)
    .WaitForCompletion(keycloakConfig)
    .WaitForCompletion(initializer);

var bff = builder.AddProject<Projects.CityPicks_Bff>("city-picks")
    .WithReference(favouritesDatabase)
    .WithEnvironment("CityPicks__PlacesApi", placesApi.GetEndpoint("https"))
    .WithEnvironment("CityPicks__RealmIssuer", realmIssuer)
    .WithEnvironment("CityPicks__ClientSecret", bffSecret)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithHttpsEndpoint(port: 7100, name: "https")
    .WithHttpHealthCheck("/health", endpointName: "https")
    .WaitFor(favouritesDatabase)
    .WaitFor(placesApi);

// The realm's redirect URIs point at the BFF as the browser sees it.
keycloakConfig.WithEnvironment("CITYPICKS_BFF_URL", bff.GetEndpoint("https", KnownNetworkIdentifiers.LocalhostNetwork));

builder.Build().Run();

static string RepoPath(string path) => Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory,
    "../../../../../",
    path));
