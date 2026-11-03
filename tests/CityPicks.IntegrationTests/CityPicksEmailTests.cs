using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Xunit;

namespace CityPicks.IntegrationTests;

[Collection(CityPicksStackCollection.Name)]
public sealed partial class CityPicksEmailTests(CityPicksStackFixture fixture)
{
    [Fact]
    public async Task BrandedPasswordResetEmailArrivesInMailpitAndResetsThePassword()
    {
        var username = $"email-reset-{Guid.NewGuid():N}";
        var recipient = $"{username}@example.test";
        var id = await fixture.CreateTestUserAsync(username, "traveler");
        var page = await fixture.NewPageAsync();
        try
        {
            await CaptureStockLoginAsync();
            await fixture.ApplyIdentitySettingsAsync("branding", "city-picks");
            await fixture.ApplyIdentitySettingsAsync("email", "city-picks");

            await page.GotoAsync($"{fixture.BffUrl}/profile");
            await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).Last.ClickAsync();
            await page.GetByRole(AriaRole.Link, new() { Name = "Forgot Password?", Exact = true }).ClickAsync();
            await page.Locator("#username").FillAsync(username);
            await page.GetByRole(AriaRole.Button, new() { Name = "Submit", Exact = true }).ClickAsync();
            await Assertions.Expect(page.Locator(".alert-success")).ToContainTextAsync("You should receive an email shortly");

            var email = await fixture.WaitForEmailAsync(recipient);
            Assert.Equal("Reset your City Picks password", email["Subject"]!.GetValue<string>());
            Assert.Equal("no-reply@citypicks.test", email["From"]!["Address"]!.GetValue<string>());
            var html = email["HTML"]!.GetValue<string>();
            Assert.Contains("Choose a new password", html);
            Assert.Contains("#23634c", html);
            var text = email["Text"]!.GetValue<string>();
            var link = ActionTokenLink(fixture.KeycloakUrl).Match(text);
            Assert.True(link.Success, $"No action-token link in the plain-text body:\n{text}");
            await CaptureAsync(html, "password-reset-email");

            // Same browser, so Keycloak resumes the original sign-in after the new password is saved.
            await page.GotoAsync(link.Value);
            await Assertions.Expect(page.Locator("html.city-picks-login")).ToBeVisibleAsync();
            await page.Locator("#password-new").FillAsync("reset-demo");
            await page.Locator("#password-confirm").FillAsync("reset-demo");
            await page.GetByRole(AriaRole.Button, new() { Name = "Submit", Exact = true }).ClickAsync();
            await Assertions.Expect(page.Locator("#api-identity")).ToContainTextAsync(username);
        }
        finally
        {
            await page.Context.CloseAsync();
            await fixture.DeleteTestUserAsync(id);
            await fixture.ApplyIdentitySettingsAsync("email", "keycloak");
            await fixture.ApplyIdentitySettingsAsync("branding", "keycloak");
        }
    }

    [Fact]
    public async Task WebhookPasswordResetDeliversThroughCommunicationApiAndPropagatesDeliveryFailure()
    {
        var username = $"webhook-reset-{Guid.NewGuid():N}";
        var id = await fixture.CreateTestUserAsync(username, "traveler");
        var page = await fixture.NewPageAsync();
        try
        {
            await fixture.SetEmailModeAsync("webhook");
            await fixture.ApplyIdentitySettingsAsync("branding", "city-picks");
            await page.GotoAsync($"{fixture.BffUrl}/profile");
            await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).Last.ClickAsync();
            await page.GetByRole(AriaRole.Link, new() { Name = "Forgot Password?", Exact = true }).ClickAsync();
            await page.Locator("#username").FillAsync(username);
            await page.GetByRole(AriaRole.Button, new() { Name = "Submit", Exact = true }).ClickAsync();
            var email = await fixture.WaitForEmailAsync($"{username}@example.test");
            Assert.Equal("Reset your City Picks password", email["Subject"]!.GetValue<string>());
            Assert.Contains("local identity-email demo", email["HTML"]!.GetValue<string>());
            var link = ActionTokenLink(fixture.KeycloakUrl).Match(email["Text"]!.GetValue<string>());
            Assert.True(link.Success);
            await page.GotoAsync(link.Value);
            await page.Locator("#password-new").FillAsync("webhook-demo");
            await page.Locator("#password-confirm").FillAsync("webhook-demo");
            await page.GetByRole(AriaRole.Button, new() { Name = "Submit", Exact = true }).ClickAsync();
            await Assertions.Expect(page.Locator("#api-identity")).ToContainTextAsync(username);

            using var invalid = await fixture.CommunicationApi.PostAsJsonAsync("/api/keycloak-email", new
            {
                emailType = "password-reset", destinationEmail = $"{username}@example.test",
                bodyAttributes = new { link = "javascript:alert(1)", linkExpirationMinutes = 5 }
            });
            Assert.Equal(System.Net.HttpStatusCode.UnprocessableEntity, invalid.StatusCode);

            await fixture.SetMailpitRunningAsync(false);
            try
            {
                using var failed = await fixture.CommunicationApi.PostAsJsonAsync("/api/keycloak-email", new
                {
                    emailType = "smtp-test", destinationEmail = $"{username}@example.test"
                });
                Assert.Equal(System.Net.HttpStatusCode.BadGateway, failed.StatusCode);
                using var admin = await fixture.CreateAdminClientAsync();
                using var rejected = await admin.PutAsJsonAsync(
                    $"/admin/realms/city-picks/users/{id}/execute-actions-email", new[] { "UPDATE_PASSWORD" });
                Assert.False(rejected.IsSuccessStatusCode);
            }
            finally { await fixture.SetMailpitRunningAsync(true); }
        }
        finally
        {
            await page.Context.CloseAsync();
            await fixture.DeleteTestUserAsync(id);
            await fixture.ApplyIdentitySettingsAsync("branding", "keycloak");
            await fixture.SetEmailModeAsync("smtp");
        }
    }

    private async Task CaptureStockLoginAsync()
    {
        var directory = Environment.GetEnvironmentVariable("CITYPICKS_STOCK_LOGIN_SCREENSHOT_DIRECTORY");
        if (directory is null) return;
        var page = await fixture.NewPageAsync();
        using var admin = await fixture.CreateAdminClientAsync();
        var realm = (await admin.GetFromJsonAsync<JsonObject>("/admin/realms/city-picks"))!;
        var registrationAllowed = realm["registrationAllowed"]!.GetValue<bool>();
        try
        {
            await fixture.ApplyIdentitySettingsAsync("branding", "keycloak");
            realm["registrationAllowed"] = false;
            using var configure = await admin.PutAsJsonAsync("/admin/realms/city-picks", realm);
            configure.EnsureSuccessStatusCode();
            await page.SetViewportSizeAsync(1280, 800);
            await page.GotoAsync($"{fixture.BffUrl}/profile");
            await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).Last.ClickAsync();
            await Assertions.Expect(page.Locator("#kc-login")).ToBeVisibleAsync();
            await page.ScreenshotAsync(new() { Path = Path.Combine(directory, "keycloak-login-default.png") });
        }
        finally
        {
            await page.Context.CloseAsync();
            realm["registrationAllowed"] = registrationAllowed;
            using var restore = await admin.PutAsJsonAsync("/admin/realms/city-picks", realm);
            restore.EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task EmailThemeSwitchesFromStockToCityPicksThroughConfig()
    {
        var stockUser = $"email-stock-{Guid.NewGuid():N}";
        var brandedUser = $"email-branded-{Guid.NewGuid():N}";
        var stockId = await fixture.CreateTestUserAsync(stockUser, "traveler");
        var brandedId = await fixture.CreateTestUserAsync(brandedUser, "traveler");
        try
        {
            await SendUpdatePasswordEmailAsync(stockId);
            var stock = await fixture.WaitForEmailAsync($"{stockUser}@example.test");
            Assert.Equal("Update Your Account", stock["Subject"]!.GetValue<string>());

            await fixture.ApplyIdentitySettingsAsync("email", "city-picks");
            await SendUpdatePasswordEmailAsync(brandedId);
            var branded = await fixture.WaitForEmailAsync($"{brandedUser}@example.test");
            Assert.Equal("Action needed on your City Picks account", branded["Subject"]!.GetValue<string>());
            Assert.Contains("Update my account", branded["HTML"]!.GetValue<string>());
        }
        finally
        {
            await fixture.DeleteTestUserAsync(stockId);
            await fixture.DeleteTestUserAsync(brandedId);
            await fixture.ApplyIdentitySettingsAsync("email", "keycloak");
        }
    }

    private async Task SendUpdatePasswordEmailAsync(string userId)
    {
        using var admin = await fixture.CreateAdminClientAsync();
        using var response = await admin.PutAsJsonAsync(
            $"/admin/realms/city-picks/users/{userId}/execute-actions-email", new[] { "UPDATE_PASSWORD" });
        response.EnsureSuccessStatusCode();
    }

    private async Task CaptureAsync(string html, string name)
    {
        var directory = Environment.GetEnvironmentVariable("CITYPICKS_EMAIL_SCREENSHOT_DIRECTORY");
        if (directory is null) return;
        Directory.CreateDirectory(directory);
        var page = await fixture.NewPageAsync();
        try
        {
            foreach (var (width, height, size) in new[] { (1024, 900, "desktop"), (390, 844, "narrow") })
            {
                await page.SetViewportSizeAsync(width, height);
                await page.SetContentAsync(html);
                await page.ScreenshotAsync(new() { Path = Path.Combine(directory, $"{name}-{size}.png"), FullPage = true });
            }
        }
        finally { await page.Context.CloseAsync(); }
    }

    private static Regex ActionTokenLink(string keycloakUrl) =>
        new($@"{Regex.Escape(keycloakUrl)}/realms/city-picks/login-actions/action-token\?\S+");
}
