using Aspire.Hosting.ApplicationModel;
// Throwaway verification resource graph; this is not the City Picks application.
var builder = DistributedApplication.CreateBuilder(args);
var password = builder.AddParameter("demo-password", "demo", secret: true);
var admin = builder.AddParameter("admin-user", "admin");
var postgres = builder.AddPostgres("postgres", password: password)
    .WithImageTag("17.11-bookworm")
    .WithDataVolume(Environment.GetEnvironmentVariable("PROBE_VOLUME") ?? "wayfinder-stack-probe-postgres")
    .WithBindMount("../assets/db-init", "/docker-entrypoint-initdb.d", isReadOnly: true);
var appdb = postgres.AddDatabase("appdb", "citypicks");
var keycloak = builder.AddKeycloak("keycloak", adminUsername: admin, adminPassword: password)
    .WithImageTag("26.8.0")
    .WithRealmImport("../assets/realm")
    .WithHttpsEndpoint(port: 8443, targetPort: 8443, name: "public-https", isProxied: false)
    .WithEnvironment("KC_HOSTNAME", "https://localhost:8443")
    .WithEnvironment("KC_DB", "postgres")
    .WithEnvironment("KC_DB_USERNAME", "keycloak")
    .WithEnvironment("KC_DB_PASSWORD", "demo")
    .WithEnvironment("KC_DB_URL", ReferenceExpression.Create($"jdbc:postgresql://{postgres.GetEndpoint("tcp").Property(EndpointProperty.Host)}:{postgres.GetEndpoint("tcp").Property(EndpointProperty.Port)}/keycloak"))
    .WaitFor(postgres);
var connection = ReferenceExpression.Create($"Host={postgres.GetEndpoint("tcp").Property(EndpointProperty.Host)};Port={postgres.GetEndpoint("tcp").Property(EndpointProperty.Port)};Database=citypicks;Username=citypicks;Password=demo");
var initializer = builder.AddProject<Projects.Probe>("initializer")
    .WithArgs("--initialize")
    .WithEnvironment("PROBE_DB", connection)
    .WithEnvironment("INIT_FAIL", Environment.GetEnvironmentVariable("PROBE_INIT_FAIL") ?? "0")
    .WithEnvironment("INIT_DELAY_MS", Environment.GetEnvironmentVariable("PROBE_INIT_DELAY_MS") ?? "0")
    .WaitFor(postgres);
builder.AddProject<Projects.Probe>("probe")
    .WithEnvironment("PROBE_DB", connection)
    .WithHttpsEndpoint(port: 7100, targetPort: 7100, name: "https", isProxied: false)
    .WithHttpHealthCheck("/health", endpointName: "https")
    .WaitFor(postgres).WaitFor(keycloak).WaitForCompletion(initializer);
builder.Build().Run();
