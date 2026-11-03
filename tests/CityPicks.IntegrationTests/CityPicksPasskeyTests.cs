using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Xunit;

namespace CityPicks.IntegrationTests;

[Collection(CityPicksStackCollection.Name)]
public sealed class CityPicksPasskeyTests(CityPicksStackFixture fixture)
{
    [Theory]
    [InlineData("The operation was cancelled.")]
    [InlineData("The operation timed out.")]
    public async Task BrandedCancellationOrTimeoutKeepsPasswordRecovery(string reason)
    {
        var page = await fixture.NewPageAsync();
        using var admin = await fixture.CreateAdminClientAsync();
        try
        {
            await fixture.ApplyIdentitySettingsAsync("branding", "city-picks");
            await fixture.ApplyPasskeySettingsAsync(true);
            var mediation = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            await page.ExposeFunctionAsync("reportPasskeyMediation", (string value) => mediation.TrySetResult(value));
            // Inject only the browser's failure result, exercising the real supplied
            // script/form/error handling against Keycloak. This is not provider proof.
            await page.AddInitScriptAsync("""
                window.passkeyRequests = [];
                navigator.credentials.get = async options => {
                    window.passkeyRequests.push(options.mediation);
                    if (options.mediation === 'conditional') return new Promise(() => {});
                    await window.reportPasskeyMediation(options.mediation);
                    throw new DOMException('Cancelled or timed out', 'NotAllowedError');
                };
                """.Replace("Cancelled or timed out", reason, StringComparison.Ordinal));
            await StartLoginAsync(page);
            await Assertions.Expect(page.Locator("#username")).ToHaveAttributeAsync("autocomplete", "username webauthn");
            await page.WaitForFunctionAsync("window.passkeyRequests.includes('conditional')");
            await page.Locator("#authenticateWebAuthnButton").ClickAsync();
            // Conditional autofill remains pending; the explicit button uses
            // the browser default (omits mediation) and exercises error recovery.
            Assert.Null(await mediation.Task.WaitAsync(TimeSpan.FromSeconds(10)));
            await Assertions.Expect(page.Locator(".alert-error")).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator("html.city-picks-login")).ToBeVisibleAsync();
            await page.SetViewportSizeAsync(390, 844);
            Assert.False(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth > document.documentElement.clientWidth"));
            await page.Locator("#username").FillAsync("traveler");
            await page.Locator("#password").FillAsync("demo");
            await page.Locator("#kc-login").ClickAsync();
            await Assertions.Expect(page.Locator("#api-identity")).ToContainTextAsync("traveler");
        }
        finally
        {
            await page.Context.CloseAsync();
            await fixture.ApplyPasskeySettingsAsync(false);
            await fixture.ApplyIdentitySettingsAsync("branding", "keycloak");
        }
    }

    [Fact]
    public async Task BrandedExplicitPasskeyReturnsToSameFavouriteAndSurvivesDisableAndReenable()
    {
        using var admin = await fixture.CreateAdminClientAsync();
        var users = await admin.GetFromJsonAsync<JsonArray>("/admin/realms/city-picks/users?username=traveler&exact=true");
        var id = users![0]!["id"]!.GetValue<string>();
        var page = await fixture.NewPageAsync();
        // The virtual authenticator auto-selects conditional requests without a
        // human choosing autofill. Keep those pending to exercise the explicit
        // button deterministically; production keeps default conditional UI.
        await page.AddInitScriptAsync("""
            const getCredential = navigator.credentials.get.bind(navigator.credentials);
            navigator.credentials.get = options => options.mediation === 'conditional'
                ? new Promise(() => {}) : getCredential(options);
            """);
        var cdp = await page.Context.NewCDPSessionAsync(page);
        await cdp.SendAsync("WebAuthn.enable");
        string? authenticatorId = await AddVirtualAuthenticatorAsync();
        var dialogs = new List<string>();
        page.Dialog += async (_, dialog) =>
        {
            dialogs.Add(dialog.Message);
            await dialog.DismissAsync();
        };
        string? token = null;
        try
        {
            token = await fixture.SignInAndGetApiTestTokenAsync(page, "traveler");
            await RemoveFavouriteAsync(token);
            var identity = await page.Locator("#api-identity").InnerTextAsync();
            var permissions = await page.Locator("#permissions").InnerTextAsync();
            await page.GotoAsync($"{fixture.BffUrl}/places/charles-bridge");
            await page.GetByRole(AriaRole.Button, new() { Name = "Save to favourites", Exact = true }).ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Saved to favourites", Exact = true })).ToBeVisibleAsync();
            await page.GotoAsync($"{fixture.KeycloakUrl}/realms/city-picks/account/account-security/signing-in");
            await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Signing in", Exact = true })).ToBeVisibleAsync();
            await fixture.ApplyIdentitySettingsAsync("branding", "city-picks");
            await fixture.ApplyPasskeySettingsAsync(true);
            await page.ReloadAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Set up Passkey" }).ClickAsync();
            await Assertions.Expect(page.Locator("html.city-picks-login")).ToBeVisibleAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Register", Exact = true }).ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Signing in", Exact = true })).ToBeVisibleAsync();
            Assert.Empty(dialogs);
            var credentials = (await admin.GetFromJsonAsync<JsonArray>($"/admin/realms/city-picks/users/{id}/credentials"))!;
            var firstCredential = Assert.Single(credentials, credential => credential!["type"]!.GetValue<string>() == "webauthn-passwordless")!;
            Assert.False(string.IsNullOrWhiteSpace(firstCredential["userLabel"]?.GetValue<string>()));

            // A second device must register without a prompt or a duplicate-label error.
            var savedCredentials = await cdp.SendAsync("WebAuthn.getCredentials", new Dictionary<string, object> { ["authenticatorId"] = authenticatorId });
            await cdp.SendAsync("WebAuthn.removeVirtualAuthenticator", new Dictionary<string, object> { ["authenticatorId"] = authenticatorId });
            authenticatorId = null;
            var secondAuthenticatorId = await AddVirtualAuthenticatorAsync();
            authenticatorId = secondAuthenticatorId;
            try
            {
                await page.GetByRole(AriaRole.Button, new() { Name = "Set up Passkey" }).ClickAsync();
                await page.GetByRole(AriaRole.Button, new() { Name = "Register", Exact = true }).ClickAsync();
                await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Signing in", Exact = true })).ToBeVisibleAsync();
                Assert.Empty(dialogs);
                var enrolled = (await admin.GetFromJsonAsync<JsonArray>($"/admin/realms/city-picks/users/{id}/credentials"))!;
                var passkeys = enrolled.Where(credential => credential!["type"]!.GetValue<string>() == "webauthn-passwordless").ToArray();
                Assert.Equal(2, passkeys.Length);
                var labels = passkeys.Select(credential => credential!["userLabel"]?.GetValue<string>()).ToArray();
                Assert.All(labels, label => Assert.False(string.IsNullOrWhiteSpace(label)));
                Assert.Equal(2, labels.Distinct(StringComparer.OrdinalIgnoreCase).Count());
                var secondCredential = passkeys.Single(credential => credential!["id"]!.GetValue<string>() != firstCredential["id"]!.GetValue<string>())!;
                using var deleteSecond = await admin.DeleteAsync($"/admin/realms/city-picks/users/{id}/credentials/{secondCredential["id"]!.GetValue<string>()}");
                deleteSecond.EnsureSuccessStatusCode();
            }
            finally
            {
                await cdp.SendAsync("WebAuthn.removeVirtualAuthenticator", new Dictionary<string, object> { ["authenticatorId"] = secondAuthenticatorId });
                authenticatorId = null;
                authenticatorId = await AddVirtualAuthenticatorAsync();
                foreach (var credential in savedCredentials!.Value.GetProperty("credentials").EnumerateArray())
                    await cdp.SendAsync("WebAuthn.addCredential", new Dictionary<string, object>
                    {
                        ["authenticatorId"] = authenticatorId, ["credential"] = credential
                    });
            }
            await fixture.ReapplyConfigurationAsync();
            Assert.Equal(Passkeys(credentials), Passkeys((await admin.GetFromJsonAsync<JsonArray>($"/admin/realms/city-picks/users/{id}/credentials"))!));

            await LogoutAsync(page);
            await PasskeySignInAsync(page);
            await AssertContinuityAsync(page, identity, permissions);

            credentials = (await admin.GetFromJsonAsync<JsonArray>($"/admin/realms/city-picks/users/{id}/credentials"))!;
            await fixture.ApplyPasskeySettingsAsync(false);
            Assert.Equal(Passkeys(credentials), Passkeys((await admin.GetFromJsonAsync<JsonArray>($"/admin/realms/city-picks/users/{id}/credentials"))!));
            await LogoutAsync(page);
            await StartLoginAsync(page);
            await Assertions.Expect(page.Locator("#authenticateWebAuthnButton")).ToHaveCountAsync(0);
            await page.Locator("#username").FillAsync("traveler");
            await page.Locator("#password").FillAsync("demo");
            await page.Locator("#kc-login").ClickAsync();
            await AssertContinuityAsync(page, identity, permissions);
            await fixture.ApplyPasskeySettingsAsync(true);
            await LogoutAsync(page);
            await PasskeySignInAsync(page);
            await AssertContinuityAsync(page, identity, permissions);

            // Server deletion must reject the authenticator's retained private credential.
            using var freshAdmin = await fixture.CreateAdminClientAsync();
            var credentialId = credentials.Single(credential => credential!["type"]!.GetValue<string>() == "webauthn-passwordless")!["id"]!.GetValue<string>();
            using var delete = await freshAdmin.DeleteAsync($"/admin/realms/city-picks/users/{id}/credentials/{credentialId}");
            delete.EnsureSuccessStatusCode();
            await LogoutAsync(page);
            await StartLoginAsync(page);
            await page.Locator("#authenticateWebAuthnButton").ClickAsync();
            await Assertions.Expect(page.Locator("#kc-form-login")).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator(".alert-error")).ToBeVisibleAsync();
            await page.Locator("#username").FillAsync("traveler");
            await page.Locator("#password").FillAsync("demo");
            await page.Locator("#kc-login").ClickAsync();
            await AssertContinuityAsync(page, identity, permissions);
        }
        finally
        {
            if (token is not null) await RemoveFavouriteAsync(token);
            try
            {
                if (authenticatorId is not null)
                    await cdp.SendAsync("WebAuthn.removeVirtualAuthenticator", new Dictionary<string, object> { ["authenticatorId"] = authenticatorId });
            }
            finally
            {
                await page.Context.CloseAsync();
                using var cleanup = await fixture.CreateAdminClientAsync();
                var credentials = await cleanup.GetFromJsonAsync<JsonArray>($"/admin/realms/city-picks/users/{id}/credentials");
                foreach (var credential in credentials!.Where(item => item!["type"]!.GetValue<string>() == "webauthn-passwordless"))
                {
                    using var delete = await cleanup.DeleteAsync($"/admin/realms/city-picks/users/{id}/credentials/{credential!["id"]!.GetValue<string>()}");
                    delete.EnsureSuccessStatusCode();
                }
                await fixture.ApplyPasskeySettingsAsync(false);
                await fixture.ApplyIdentitySettingsAsync("branding", "keycloak");
            }
        }

        async Task<string> AddVirtualAuthenticatorAsync()
        {
            var result = await cdp.SendAsync("WebAuthn.addVirtualAuthenticator", new Dictionary<string, object>
            {
                ["options"] = new { protocol = "ctap2", transport = "internal", hasResidentKey = true,
                    hasUserVerification = true, isUserVerified = true, automaticPresenceSimulation = true }
            });
            return result!.Value.GetProperty("authenticatorId").GetString()!;
        }
    }

    private async Task RemoveFavouriteAsync(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/favourites/charles-bridge");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await fixture.SendBffAsync(request, token);
        response.EnsureSuccessStatusCode();
    }

    private static string[] Passkeys(JsonArray credentials) => credentials
        .Where(credential => credential!["type"]!.GetValue<string>() == "webauthn-passwordless")
        .Select(credential => credential!.ToJsonString()).ToArray();

    private async Task StartLoginAsync(IPage page)
    {
        await page.GotoAsync($"{fixture.BffUrl}/profile");
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).Last.ClickAsync();
        await Assertions.Expect(page.Locator("html.city-picks-login")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#password")).ToBeVisibleAsync();
    }

    private async Task PasskeySignInAsync(IPage page)
    {
        await StartLoginAsync(page);
        await page.Locator("#authenticateWebAuthnButton").ClickAsync();
        await Assertions.Expect(page.Locator("#api-identity")).ToContainTextAsync("traveler");
    }

    private async Task LogoutAsync(IPage page)
    {
        await page.GotoAsync($"{fixture.BffUrl}/profile");
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign out", Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true })).ToBeVisibleAsync();
    }

    private async Task AssertContinuityAsync(IPage page, string identity, string permissions)
    {
        await Assertions.Expect(page.Locator("#api-identity")).ToHaveTextAsync(identity, new() { UseInnerText = true });
        await Assertions.Expect(page.Locator("#permissions")).ToHaveTextAsync(permissions, new() { UseInnerText = true });
        await page.GotoAsync($"{fixture.BffUrl}/favourites");
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Charles Bridge", Exact = true })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task ExplicitApplyEnablesIntegratedPasskeysWithoutAssigningEnrollment()
    {
        using var admin = await fixture.CreateAdminClientAsync();
        try
        {
            await fixture.ApplyPasskeySettingsAsync(true);
            var realm = (await admin.GetFromJsonAsync<JsonObject>("/admin/realms/city-picks"))!;
            Assert.True(realm["webAuthnPolicyPasswordlessPasskeysEnabled"]!.GetValue<bool>());
            Assert.Equal("localhost", realm["webAuthnPolicyPasswordlessRpId"]!.GetValue<string>());
            Assert.Equal("required", realm["webAuthnPolicyPasswordlessResidentKey"]!.GetValue<string>());
            Assert.Equal("required", realm["webAuthnPolicyPasswordlessUserVerificationRequirement"]!.GetValue<string>());
            Assert.Equal("none", realm["webAuthnPolicyPasswordlessAttestationConveyancePreference"]!.GetValue<string>());
            Assert.Equal("conditional", realm["webAuthnPolicyPasswordlessMediation"]?.GetValue<string>() ?? "conditional");
            Assert.Equal("not specified", realm["webAuthnPolicyPasswordlessAuthenticatorAttachment"]!.GetValue<string>());
            Assert.Equal("ES256", Assert.Single(realm["webAuthnPolicyPasswordlessSignatureAlgorithms"]!.AsArray())!.GetValue<string>());
            Assert.Empty(realm["webAuthnPolicyPasswordlessAcceptableAaguids"]!.AsArray());
            var actions = await admin.GetFromJsonAsync<JsonArray>("/admin/realms/city-picks/authentication/required-actions");
            var enrollment = actions!.Single(action => action!["alias"]!.GetValue<string>() == "webauthn-register-passwordless")!;
            Assert.True(enrollment["enabled"]!.GetValue<bool>());
            Assert.False(enrollment["defaultAction"]!.GetValue<bool>());
            foreach (var username in new[] { "traveler", "curator" })
            {
                var users = await admin.GetFromJsonAsync<JsonArray>($"/admin/realms/city-picks/users?username={username}&exact=true");
                Assert.Empty(users![0]!["requiredActions"]!.AsArray());
            }
            var page = await fixture.NewPageAsync();
            try
            {
                await fixture.SignInAndGetApiTestTokenAsync(page, "traveler");
                await page.GotoAsync($"{fixture.KeycloakUrl}/realms/city-picks/account/account-security/signing-in");
                await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Signing in", Exact = true })).ToBeVisibleAsync();
                await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Set up Passkey" })).ToBeVisibleAsync();
                Assert.Equal(0, await page.Locator("html.city-picks-login").CountAsync());
            }
            finally { await page.Context.CloseAsync(); }
        }
        finally { await fixture.ApplyPasskeySettingsAsync(false); }
    }
}
