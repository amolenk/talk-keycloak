using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Net.Http.Headers;
using Microsoft.Playwright;
using Xunit;

namespace CityPicks.IntegrationTests;

[Collection(CityPicksStackCollection.Name)]
public sealed class CityPicksBrandingTests(CityPicksStackFixture fixture)
{
    [Fact]
    public async Task EditingConfigAndRestartingResourceUpdatesLoginWithoutRestartingKeycloak()
    {
        using var admin = await fixture.CreateAdminClientAsync();
        try
        {
            await fixture.ApplyIdentitySettingsAsync("branding", "city-picks");
            var page = await fixture.NewPageAsync();
            try
            {
                await page.GotoAsync($"{fixture.BffUrl}/profile");
                await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).Last.ClickAsync();
                await Assertions.Expect(page.Locator("html.city-picks-login")).ToBeVisibleAsync();
                await Assertions.Expect(page.Locator("#kc-header-wrapper")).ToContainTextAsync("City Picks");
            }
            finally { await page.Context.CloseAsync(); }

            await fixture.ApplyIdentitySettingsAsync("presentation", "City Picks — Own Your Identity");
            using var drift = await admin.PutAsJsonAsync("/admin/realms/city-picks", new { loginTheme = "keycloak" });
            drift.EnsureSuccessStatusCode();
            await fixture.ReapplyConfigurationAsync();
            var reconciled = await admin.GetFromJsonAsync<JsonObject>("/admin/realms/city-picks");
            Assert.Equal("city-picks", reconciled!["loginTheme"]!.GetValue<string>());
            Assert.Equal("City Picks — Own Your Identity", reconciled["displayName"]!.GetValue<string>());
            var fresh = await fixture.NewPageAsync();
            try
            {
                await fresh.GotoAsync($"{fixture.BffUrl}/profile");
                await fresh.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).Last.ClickAsync();
                await Assertions.Expect(fresh.Locator("#kc-header-wrapper")).ToHaveTextAsync("City Picks — Own Your Identity");
            }
            finally { await fresh.Context.CloseAsync(); }
        }
        finally
        {
            await fixture.ApplyIdentitySettingsAsync("branding", "keycloak");
            await fixture.ApplyIdentitySettingsAsync("presentation", "City Picks");
        }
    }

    [Fact]
    public async Task BrandedLoginErrorsAndRegistrationKeepRealAuthenticationWorking()
    {
        using var admin = await fixture.CreateAdminClientAsync();
        await fixture.ApplyIdentitySettingsAsync("branding", "city-picks");
        var page = await fixture.NewPageAsync();
        var remoteRequests = new List<string>();
        page.Request += (_, request) =>
        {
            if (Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && !uri.IsLoopback)
                remoteRequests.Add(uri.Host);
        };
        try
        {
            await page.GotoAsync($"{fixture.BffUrl}/profile");
            await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).Last.ClickAsync();
            await page.Locator("#username").FillAsync("traveler");
            await page.Locator("#password").FillAsync("incorrect-password");
            await page.Locator("#kc-login").ClickAsync();
            await Assertions.Expect(page.Locator("#input-error")).ToContainTextAsync("Invalid username or password");
            await Assertions.Expect(page.Locator("#password")).ToHaveAttributeAsync("aria-invalid", "true");
            await CaptureAsync(page, "login-error");
            await page.GetByRole(AriaRole.Link, new() { Name = "Forgot Password?", Exact = true }).ClickAsync();
            await Assertions.Expect(page.Locator("#kc-reset-password-form")).ToBeVisibleAsync();
            await page.GetByRole(AriaRole.Link, new() { Name = "Back to Login" }).ClickAsync();
            await Assertions.Expect(page.Locator("#password")).ToBeVisibleAsync();
            await CaptureAsync(page, "login");
            await page.GetByRole(AriaRole.Link, new() { Name = "Register", Exact = true }).ClickAsync();
            await CaptureAsync(page, "registration");
            // Use the same real registration and stock Account Console assertions as baseline.
            await new CityPicksIdentityTests(fixture).RegistrationGrantsOnlyTravelerAndEnablesAccountConsole();

            // Server-generated protocol error, not a mock context.
            await page.GotoAsync($"{fixture.KeycloakUrl}/realms/city-picks/protocol/openid-connect/auth?client_id=city-picks-bff&redirect_uri=https%3A%2F%2Finvalid.example.test&response_type=code");
            await Assertions.Expect(page.Locator("#kc-error-message")).ToContainTextAsync("Invalid parameter");
            await CaptureAsync(page, "protocol-error");
            Assert.Empty(remoteRequests);
            await new CityPicksIdentityTests(fixture).CompleteLogoutEndsApplicationAndKeycloakSessions();
            await new CityPicksIdentityTests(fixture).PreparedUsersCanOpenStockAccountConsole("traveler");
            await Assertions.Expect(page.Locator("html.city-picks-login")).ToBeVisibleAsync();
        }
        finally
        {
            await page.Context.CloseAsync();
            await fixture.ApplyIdentitySettingsAsync("branding", "keycloak");
        }
    }

    [Fact]
    public async Task BrandedRequiredPasswordUpdateRetainsTheSuppliedActionHandler()
    {
        var username = $"theme-action-{Guid.NewGuid():N}";
        var id = await fixture.CreateTestUserAsync(username, "traveler");
        using var admin = await fixture.CreateAdminClientAsync();
        var page = await fixture.NewPageAsync();
        try
        {
            using var update = await admin.PutAsJsonAsync($"/admin/realms/city-picks/users/{id}", new { requiredActions = new[] { "UPDATE_PASSWORD" } });
            update.EnsureSuccessStatusCode();
            await fixture.ApplyIdentitySettingsAsync("branding", "city-picks");
            await page.GotoAsync($"{fixture.BffUrl}/profile");
            await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).Last.ClickAsync();
            await page.Locator("#username").FillAsync(username);
            await page.Locator("#password").FillAsync("demo");
            await page.Locator("#kc-login").ClickAsync();
            await Assertions.Expect(page.Locator("#password-new")).ToBeVisibleAsync();
            await CaptureAsync(page, "required-password-update");
            await page.Locator("#password-new").FillAsync("updated-demo");
            await page.Locator("#password-confirm").FillAsync("updated-demo");
            await page.GetByRole(AriaRole.Button, new() { Name = "Submit", Exact = true }).ClickAsync();
            await Assertions.Expect(page.Locator("#api-identity")).ToContainTextAsync(username);
            var user = await admin.GetFromJsonAsync<JsonObject>($"/admin/realms/city-picks/users/{id}");
            Assert.Empty(user!["requiredActions"]!.AsArray());
        }
        finally
        {
            await page.Context.CloseAsync();
            await fixture.DeleteTestUserAsync(id);
            await fixture.ApplyIdentitySettingsAsync("branding", "keycloak");
        }
    }

    [Fact]
    public async Task ReapplyPreservesUnmanagedUserIdentityCredentialsRolesAndFavourite()
    {
        var username = $"theme-preservation-{Guid.NewGuid():N}";
        var id = await fixture.CreateTestUserAsync(username, "traveler");
        using var admin = await fixture.CreateAdminClientAsync();
        var credentials = (await admin.GetFromJsonAsync<JsonArray>($"/admin/realms/city-picks/users/{id}/credentials"))!.ToJsonString();
        var roles = (await admin.GetFromJsonAsync<JsonObject>($"/admin/realms/city-picks/users/{id}/role-mappings"))!.ToJsonString();
        var page = await fixture.NewPageAsync();
        try
        {
            var token = await fixture.SignInAndGetApiTestTokenAsync(page, username);
            using var save = new HttpRequestMessage(HttpMethod.Put, "/api/favourites/charles-bridge");
            save.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var saved = await fixture.SendBffAsync(save, token);
            saved.EnsureSuccessStatusCode();
            await fixture.ApplyIdentitySettingsAsync("branding", "city-picks");
            await fixture.ReapplyConfigurationAsync();
            var after = await admin.GetFromJsonAsync<JsonArray>($"/admin/realms/city-picks/users?username={username}&exact=true");
            Assert.Equal(id, after![0]!["id"]!.GetValue<string>());
            Assert.Equal(credentials, (await admin.GetFromJsonAsync<JsonArray>($"/admin/realms/city-picks/users/{id}/credentials"))!.ToJsonString());
            Assert.Equal(roles, (await admin.GetFromJsonAsync<JsonObject>($"/admin/realms/city-picks/users/{id}/role-mappings"))!.ToJsonString());
            await page.GetByRole(AriaRole.Button, new() { Name = "Sign out", Exact = true }).ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true })).ToBeVisibleAsync();
            await fixture.SignInAndGetApiTestTokenAsync(page, username);
            await page.GotoAsync($"{fixture.BffUrl}/favourites");
            await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Charles Bridge", Exact = true })).ToBeVisibleAsync();
        }
        finally
        {
            await page.Context.CloseAsync();
            await fixture.DeleteTestUserAsync(id);
            await fixture.ApplyIdentitySettingsAsync("branding", "keycloak");
        }
    }

    private static async Task CaptureAsync(IPage page, string name)
    {
        foreach (var (width, height, size) in new[] { (1440, 1000, "desktop"), (390, 844, "narrow") })
        {
            await page.SetViewportSizeAsync(width, height);
            await Assertions.Expect(page.Locator("html.city-picks-login")).ToBeVisibleAsync();
            Assert.False(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth > document.documentElement.clientWidth"));
            var directory = Environment.GetEnvironmentVariable("CITYPICKS_THEME_SCREENSHOT_DIRECTORY");
            if (directory is null) continue;
            Directory.CreateDirectory(directory);
            await page.ScreenshotAsync(new() { Path = Path.Combine(directory, $"{name}-{size}.png"), FullPage = true });
        }
    }
}
