using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using CityPicks.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Npgsql;
using Xunit;

namespace CityPicks.IntegrationTests;

/// <summary>
/// Runs the whole City Picks stack through the Aspire AppHost, plus a headless browser,
/// and offers helpers to drive and inspect it from tests.
/// </summary>
public sealed class CityPicksStackFixture : IAsyncLifetime
{
    private const string DemoPassword = "demo";
    private const string CityDirectory = "content/cities/prague";

    private readonly string _volumeName = $"city-picks-test-{Guid.NewGuid():N}";
    // A private copy of the realm YAML that tests can edit, bind-mounted into keycloak-config.
    // Not under /tmp: CreateTempSubdirectory makes 0700 folders the container can't read.
    private readonly DirectoryInfo _configDirectory = Directory.CreateDirectory(
        Path.Combine(AppContext.BaseDirectory, "keycloak-config", Guid.NewGuid().ToString("N")));
    private readonly Dictionary<string, (string Cookie, string Csrf)> _sessions = [];
    private DistributedApplication? _app;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private string _emailMode = "smtp";
    private string _databaseConnectionString = null!;

    /// <summary>The BFF as the browser sees it, without a trailing slash.</summary>
    public string BffUrl { get; private set; } = null!;
    /// <summary>Keycloak's browser-facing URL for this run, without a trailing slash.</summary>
    public string KeycloakUrl { get; private set; } = null!;

    /// <summary>Calls the BFF without a cookie jar, so each request carries exactly the session it is given.</summary>
    public HttpClient Bff { get; private set; } = null!;
    public HttpClient PlacesApi { get; private set; } = null!;
    public HttpClient CommunicationApi { get; private set; } = null!;
    public HttpClient Mail { get; private set; } = null!;

    private string ConfigPath => Path.Combine(_configDirectory.FullName, "city-picks.yaml");

    // ---------------------------------------------------------------- Lifecycle

    public async Task InitializeAsync()
    {
        File.Copy(RepoPath("infra/keycloak/config/city-picks.yaml"), ConfigPath);
        // Registration tests must not depend on the presenter's current realm setting.
        var config = await File.ReadAllTextAsync(ConfigPath);
        const string registrationSetting = @"(?m)^registrationAllowed:[ \t]*(true|false)[ \t]*\r?$";
        if (!Regex.IsMatch(config, registrationSetting))
            throw new InvalidOperationException("The Keycloak configuration must specify registrationAllowed.");
        await File.WriteAllTextAsync(ConfigPath, Regex.Replace(config, registrationSetting, "registrationAllowed: true"));
        await StartApplicationAsync();
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });
    }

    public async Task DisposeAsync()
    {
        if (_browser is not null) await _browser.DisposeAsync();
        _playwright?.Dispose();
        await StopApplicationAsync();
        RemoveTestVolume();
        _configDirectory.Delete(recursive: true);
    }

    private async Task StartApplicationAsync()
    {
        _app = await CreateApplicationAsync();
        await _app.StartAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await _app.ResourceNotifications.WaitForResourceHealthyAsync("city-picks", timeout.Token);

        BffUrl = _app.GetEndpoint("city-picks", "https").ToString().TrimEnd('/');
        KeycloakUrl = _app.GetEndpoint("keycloak", "http").ToString().TrimEnd('/');

        Bff = new HttpClient(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(BffUrl)
        };
        PlacesApi = _app.CreateHttpClient("places-api");
        CommunicationApi = _app.CreateHttpClient("communication", "http");
        Mail = new HttpClient { BaseAddress = _app.GetEndpoint("mailpit", "http") };

        var postgres = _app.GetEndpoint("postgres", "tcp");
        _databaseConnectionString = new NpgsqlConnectionStringBuilder
        {
            Host = postgres.Host, Port = postgres.Port, Database = "citypicks",
            Username = "citypicks", Password = DemoPassword, Timeout = 10
        }.ConnectionString;
    }

    private async Task StopApplicationAsync()
    {
        Bff?.Dispose();
        PlacesApi?.Dispose();
        CommunicationApi?.Dispose();
        Mail?.Dispose();
        if (_app is not null) await _app.DisposeAsync();
        _app = null;
    }

    private async Task<DistributedApplication> CreateApplicationAsync()
    {
        // The testing builder gives every endpoint a random port, so tests can run next to the dev stack.
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.CityPicks_AppHost>([
            "--environment=Testing",
            $"--CityPicks:Volume={_volumeName}",
            $"--CityPicks:CityDirectory={CityDirectory}",
            $"--CityPicks:KeycloakConfigDirectory={_configDirectory.FullName}",
            $"--CityPicks:EmailMode={_emailMode}"
        ]);
        return await builder.BuildAsync();
    }

    // ---------------------------------------------------------------- Stack control

    /// <summary>Restarts the whole stack with Keycloak sending email over SMTP or through the webhook.</summary>
    public async Task SetEmailModeAsync(string mode)
    {
        _emailMode = mode;
        await StopApplicationAsync();
        await StartApplicationAsync();
    }

    public async Task SetMailpitRunningAsync(bool running)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var commands = _app!.Services.GetRequiredService<ResourceCommandService>();
        var result = await commands.ExecuteCommandAsync("mailpit",
            running ? KnownResourceCommands.StartCommand : KnownResourceCommands.StopCommand, timeout.Token);
        Assert.True(result.Success, result.Message);
        if (running) await _app.ResourceNotifications.WaitForResourceHealthyAsync("mailpit", timeout.Token);
        else await _app.ResourceNotifications.WaitForResourceAsync("mailpit", KnownResourceStates.Exited, timeout.Token);
    }

    private void RemoveTestVolume()
    {
        var startInfo = new ProcessStartInfo("docker", ["volume", "rm", "--force", _volumeName])
        {
            RedirectStandardError = true, RedirectStandardOutput = true
        };
        using var process = Process.Start(startInfo)!;
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Failed to remove isolated test volume '{_volumeName}': {process.StandardError.ReadToEnd()}");
    }

    // ---------------------------------------------------------------- Browser and BFF sessions

    public async Task<IPage> NewPageAsync() => await _browser!.NewPageAsync();

    /// <summary>
    /// Signs <paramref name="username"/> in through the browser and remembers the BFF session.
    /// Returns a bearer token from a separate test-only client, so tests can prove the API
    /// and BFF reject bearers; the application itself never exposes one to the browser.
    /// </summary>
    public async Task<string> SignInAndGetApiTestTokenAsync(IPage page, string username)
    {
        await page.GotoAsync($"{BffUrl}/profile");
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).Last.ClickAsync();
        await page.Locator("#username").FillAsync(username);
        await page.Locator("#password").FillAsync(DemoPassword);
        await page.Locator("#kc-login").ClickAsync();
        await Assertions.Expect(page.Locator("#api-identity")).ToContainTextAsync(username);

        var token = await ObtainApiTestTokenAsync(username);
        var cookies = await page.Context.CookiesAsync([BffUrl]);
        var cookie = string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}"));
        using var userRequest = new HttpRequestMessage(HttpMethod.Get, "/auth/user");
        userRequest.Headers.Add("Cookie", cookie);
        using var userResponse = await Bff.SendAsync(userRequest);
        var session = (await userResponse.Content.ReadFromJsonAsync<JsonObject>())!;
        _sessions[token] = (cookie, session["csrfToken"]!.GetValue<string>());
        return token;
    }

    /// <summary>Sends <paramref name="request"/> to the BFF with the session cookie (and CSRF header) that belongs to <paramref name="token"/>.</summary>
    public async Task<HttpResponseMessage> SendBffAsync(HttpRequestMessage request, string? token, bool csrf = true)
    {
        request.Headers.Authorization = null;
        if (token is not null)
        {
            var session = _sessions[token];
            request.Headers.Add("Cookie", session.Cookie);
            if (csrf) request.Headers.Add("X-CSRF-TOKEN", session.Csrf);
        }
        return await Bff.SendAsync(request);
    }

    private async Task<string> ObtainApiTestTokenAsync(string username)
    {
        await EnsureApiTestClientAsync();
        using var http = new HttpClient();
        using var response = await http.PostAsync($"{KeycloakUrl}/realms/city-picks/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = "api-test-client", ["grant_type"] = "password",
                ["username"] = username, ["password"] = DemoPassword,
                ["scope"] = "openid profile email"
            }));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!["access_token"]!.GetValue<string>();
    }

    /// <summary>Clones the BFF client into a public, password-grant client that issues tokens with the same claims.</summary>
    private async Task EnsureApiTestClientAsync()
    {
        using var admin = await CreateAdminClientAsync();
        var existing = await admin.GetFromJsonAsync<JsonArray>("/admin/realms/city-picks/clients?clientId=api-test-client");
        if (existing!.Count > 0) return;

        var clients = await admin.GetFromJsonAsync<JsonArray>("/admin/realms/city-picks/clients?clientId=city-picks-bff");
        var original = clients![0]!;
        var client = original.DeepClone().AsObject();
        client.Remove("id");
        client.Remove("secret");
        client["clientId"] = "api-test-client";
        client["publicClient"] = true;
        client["standardFlowEnabled"] = false;
        client["directAccessGrantsEnabled"] = true;
        foreach (var mapper in client["protocolMappers"]!.AsArray()) mapper!.AsObject().Remove("id");
        using var create = await admin.PostAsJsonAsync("/admin/realms/city-picks/clients", client);
        create.EnsureSuccessStatusCode();

        var scope = await admin.GetFromJsonAsync<JsonArray>(
            $"/admin/realms/city-picks/clients/{original["id"]!.GetValue<string>()}/scope-mappings/realm");
        using var assign = await admin.PostAsJsonAsync($"{create.Headers.Location!.AbsolutePath}/scope-mappings/realm", scope);
        assign.EnsureSuccessStatusCode();
    }

    // ---------------------------------------------------------------- Keycloak admin

    public async Task<HttpClient> CreateAdminClientAsync()
    {
        var client = new HttpClient { BaseAddress = new Uri(KeycloakUrl) };
        using var response = await client.PostAsync("/realms/master/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = "admin-cli", ["grant_type"] = "password",
                ["username"] = "admin", ["password"] = DemoPassword
            }));
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonObject>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json!["access_token"]!.GetValue<string>());
        return client;
    }

    public async Task<string> CreateTestUserAsync(string username, params string[] roles)
    {
        using var admin = await CreateAdminClientAsync();
        using var create = await admin.PostAsJsonAsync("/admin/realms/city-picks/users", new
        {
            username, enabled = true, emailVerified = true, email = $"{username}@example.test",
            firstName = "Test", lastName = "Traveler",
            credentials = new[] { new { type = "password", value = DemoPassword, temporary = false } }
        });
        create.EnsureSuccessStatusCode();
        var id = create.Headers.Location!.Segments.Last();
        await SetTestUserRolesAsync(id, roles);
        return id;
    }

    public async Task SetTestUserRolesAsync(string id, params string[] roles)
    {
        using var admin = await CreateAdminClientAsync();
        var path = $"/admin/realms/city-picks/users/{id}/role-mappings/realm";
        var current = await admin.GetFromJsonAsync<JsonArray>(path);
        using var request = new HttpRequestMessage(HttpMethod.Delete, path) { Content = JsonContent.Create(current) };
        using var remove = await admin.SendAsync(request);
        remove.EnsureSuccessStatusCode();
        if (roles.Length == 0) return;
        var assigned = new List<JsonObject>();
        foreach (var role in roles)
            assigned.Add((await admin.GetFromJsonAsync<JsonObject>($"/admin/realms/city-picks/roles/{role}"))!);
        using var assign = await admin.PostAsJsonAsync(path, assigned);
        assign.EnsureSuccessStatusCode();
    }

    public async Task DeleteTestUserAsync(string id)
    {
        using var admin = await CreateAdminClientAsync();
        using var response = await admin.DeleteAsync($"/admin/realms/city-picks/users/{id}");
        response.EnsureSuccessStatusCode();
    }

    // ---------------------------------------------------------------- Keycloak config (YAML + keycloak-config-cli)

    /// <summary>Sets a realm-level key in the YAML (branding, email or presentation) and re-runs keycloak-config.</summary>
    public async Task ApplyIdentitySettingsAsync(string setting, string value)
    {
        var key = setting switch
        {
            "branding" => "loginTheme",
            "email" => "emailTheme",
            "presentation" => "displayName",
            _ => throw new ArgumentOutOfRangeException(nameof(setting), setting, null)
        };
        var lines = await File.ReadAllLinesAsync(ConfigPath);
        for (var i = 0; i < lines.Length; i++)
            if (lines[i].StartsWith($"{key}:", StringComparison.Ordinal)) lines[i] = $"{key}: {value}";
        await File.WriteAllLinesAsync(ConfigPath, lines);
        await ReapplyConfigurationAsync();
    }

    public async Task ApplyPasskeySettingsAsync(bool enabled)
    {
        var flag = enabled ? "true" : "false";
        var yaml = await File.ReadAllTextAsync(ConfigPath);
        yaml = Regex.Replace(yaml,
            @"webAuthnPolicyPasswordlessPasskeysEnabled: (true|false)",
            $"webAuthnPolicyPasswordlessPasskeysEnabled: {flag}");
        yaml = Regex.Replace(yaml,
            @"(providerId: webauthn-register-passwordless\n    enabled:) (true|false)",
            $"$1 {flag}");
        await File.WriteAllTextAsync(ConfigPath, yaml);
        await ReapplyConfigurationAsync();
    }

    public async Task ReapplyConfigurationAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        // One-shot containers expose Start after exiting. Wait for Running before
        // awaiting Exited so the previous run cannot satisfy the completion wait.
        var commands = _app!.Services.GetRequiredService<ResourceCommandService>();
        var started = await commands.ExecuteCommandAsync("keycloak-config", KnownResourceCommands.StartCommand, timeout.Token);
        Assert.True(started.Success, started.Message);
        await _app.ResourceNotifications.WaitForResourceAsync("keycloak-config", KnownResourceStates.Running, timeout.Token);
        var finished = await _app.ResourceNotifications.WaitForResourceAsync("keycloak-config",
            resource => resource.Snapshot.State?.Text == KnownResourceStates.Exited, timeout.Token);
        Assert.Equal(0, finished.Snapshot.ExitCode);
    }

    // ---------------------------------------------------------------- Database

    public async Task DeleteAddedPlaceAsync(string id)
    {
        if (CityCatalog.Load(RepoPath(CityDirectory)).Places.Any(p => p.Id == id))
            throw new InvalidOperationException("Test cleanup cannot delete a seeded place.");
        await using var connection = new NpgsqlConnection(_databaseConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM \"Places\" WHERE \"Id\" = @id";
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync();
    }

    // ---------------------------------------------------------------- Mail

    /// <summary>Polls Mailpit until a message to <paramref name="recipient"/> arrives, then returns its full JSON.</summary>
    public async Task<JsonObject> WaitForEmailAsync(string recipient, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (true)
        {
            var list = await Mail.GetFromJsonAsync<JsonObject>("/api/v1/messages");
            var summary = list!["messages"]!.AsArray().FirstOrDefault(message => message!["To"]!.AsArray()
                .Any(to => string.Equals(to!["Address"]!.GetValue<string>(), recipient, StringComparison.OrdinalIgnoreCase)));
            if (summary is not null)
                return (await Mail.GetFromJsonAsync<JsonObject>($"/api/v1/message/{summary["ID"]!.GetValue<string>()}"))!;
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"No email to {recipient} arrived in Mailpit.");
            await Task.Delay(500);
        }
    }

    private static string RepoPath(string path) => Path.GetFullPath($"../../../../../{path}", AppContext.BaseDirectory);
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CityPicksStackCollection : ICollectionFixture<CityPicksStackFixture>
{
    public const string Name = "City Picks local stack";
}
