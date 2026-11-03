using System.Net;
using Xunit;
using Microsoft.Playwright;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using CityPicks.Web.Models;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.JsonWebTokens;

namespace CityPicks.IntegrationTests;

[Collection(CityPicksStackCollection.Name)]
public sealed class CityPicksIdentityTests(CityPicksStackFixture fixture)
{
    [Fact]
    public async Task AnonymousRefreshShowsSignInWhileSessionDiscoveryIsStillPending()
    {
        var page = await fixture.NewPageAsync();
        var releaseDiscovery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            // Session discovery now happens at the BFF, with no Keycloak iframe.
            await page.RouteAsync("**/auth/user", async route =>
            {
                await releaseDiscovery.Task;
                await route.ContinueAsync();
            });
            await page.GotoAsync(fixture.BffUrl);
            await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("A little more Prague.");
            await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }))
                .ToBeVisibleAsync(new() { Timeout = 2000 });
            await Assertions.Expect(page.GetByRole(AriaRole.Status)).ToHaveTextAsync("Checking session…");
            await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true })).ToBeDisabledAsync();
            releaseDiscovery.TrySetResult();
            await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }))
                .ToBeEnabledAsync(new() { Timeout = 15000 });
        }
        finally
        {
            releaseDiscovery.TrySetResult();
            await page.Context.CloseAsync();
        }
    }

    [Fact]
    public async Task ConfigResourceRecreatesAMissingBrowserClientWithoutRestartingKeycloak()
    {
        using var admin = await fixture.CreateAdminClientAsync();
        var clients = await admin.GetFromJsonAsync<JsonArray>("/admin/realms/city-picks/clients?clientId=city-picks-bff");
        using var delete = await admin.DeleteAsync($"/admin/realms/city-picks/clients/{clients![0]!["id"]!.GetValue<string>()}");
        delete.EnsureSuccessStatusCode();
        try
        {
            await fixture.ReapplyConfigurationAsync();
            var page = await fixture.NewPageAsync();
            try
            {
                var redirect = Uri.EscapeDataString($"{fixture.BffUrl}/signin-oidc");
                var challenge = Base64UrlEncoder.Encode(SHA256.HashData(RandomNumberGenerator.GetBytes(32)));
                await page.GotoAsync($"{fixture.KeycloakUrl}/realms/city-picks/protocol/openid-connect/auth?client_id=city-picks-bff&redirect_uri={redirect}&response_type=code&scope=openid&code_challenge={challenge}&code_challenge_method=S256");
                await Assertions.Expect(page.Locator("#username")).ToBeVisibleAsync(new() { Timeout = 2000 });
                await page.GotoAsync(fixture.BffUrl);
                await SignInAndGetApiTestTokenAsync(page, "traveler");
            }
            finally { await page.Context.CloseAsync(); }
        }
        finally { await fixture.ReapplyConfigurationAsync(); }
    }

    [Fact]
    public async Task OwnIdentityRequiresAnAuthenticatedAccessToken()
    {
        using var response = await fixture.PlacesApi.GetAsync("/api/identity");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put })
        {
            using var request = new HttpRequestMessage(method, method == HttpMethod.Post ? "/api/places" : "/api/places/charles-bridge");
            using var denied = await fixture.PlacesApi.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        }
    }

    [Fact]
    public async Task BrowserLoginReturnsToTheInitiatingPlaceAndShowsOwnIdentity()
    {
        var page = await fixture.NewPageAsync();
        try
        {
            var usedS256 = false;
            page.Request += (_, request) =>
            {
                if (new Uri(request.Url).AbsolutePath.EndsWith("/protocol/openid-connect/auth", StringComparison.Ordinal))
                {
                    var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(request.Url).Query);
                    usedS256 = query["response_type"] == "code" && query["code_challenge_method"] == "S256"
                        && query["code_challenge"].ToString().Length >= 43;
                }
            };
            await page.GotoAsync($"{fixture.BffUrl}/places/charles-bridge");
            await page.GetByRole(AriaRole.Button, new() { Name = "Sign in to save favourites" }).ClickAsync(new() { Timeout = 10000 });
            await page.Locator("#username").FillAsync("traveler");
            await page.Locator("#password").FillAsync("demo");
            await page.Locator("#kc-login").ClickAsync();
            await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Charles Bridge");
            await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Sign out", Exact = true })).ToBeVisibleAsync();
            await page.GetByRole(AriaRole.Link, new() { Name = "Your identity", Exact = true }).ClickAsync();
            await Assertions.Expect(page.Locator("#api-identity")).ToContainTextAsync("traveler");
            await Assertions.Expect(page.Locator("#browser-identity")).ToContainTextAsync("traveler");
            await Assertions.Expect(page.Locator("#permissions")).ToContainTextAsync("Management: Denied");
            Assert.True(usedS256, "The actual browser authorization request must use Code/S256 PKCE.");
        }
        finally { await page.Context.CloseAsync(); }
    }

    [Theory]
    [InlineData("traveler", true, "traveler")]
    [InlineData("curator", true, "curator,traveler")]
    public async Task BrowserSessionAndIndependentApiTokensExposeOwnRolesAndEnforceManagementPolicy(string username, bool favourites, string roles)
    {
        var page = await fixture.NewPageAsync();
        try
        {
            var token = await SignInAndGetApiTestTokenAsync(page, username);
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/identity");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await fixture.PlacesApi.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var identity = await response.Content.ReadFromJsonAsync<IdentityView>();
            Assert.NotNull(identity);
            Assert.Equal(username, identity.Username);
            Assert.Equal($"{fixture.KeycloakUrl}/realms/city-picks", identity.Issuer);
            Assert.False(string.IsNullOrWhiteSpace(identity.Subject));
            Assert.Equal(roles, string.Join(",", identity.Roles));
            await Assertions.Expect(page.Locator("#permissions")).ToContainTextAsync($"Favourites: {(favourites ? "Allowed" : "Denied")}");
            var management = roles.Split(',').Contains("curator") && CityPicks.Api.Services.PlaceEndpoints.ManagementPolicy == "CanManagePlaces";
            Assert.Equal(management, identity.Permissions.CanManagePlaces);
            await Assertions.Expect(page.Locator("#browser-identity dd").Last)
                .ToHaveTextAsync(roles.Length == 0 ? "None" : roles.Replace(",", ", ", StringComparison.Ordinal));
            await Assertions.Expect(page.Locator("#browser-identity dd").Nth(3))
                .ToHaveTextAsync($"{fixture.KeycloakUrl}/realms/city-picks");
            await Assertions.Expect(page.Locator("#browser-identity dd").Nth(2)).ToHaveTextAsync(identity.Subject!);

            foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put })
            {
                using var mutation = new HttpRequestMessage(method, method == HttpMethod.Post ? "/api/places" : "/api/places/charles-bridge");
                mutation.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                mutation.Content = JsonContent.Create(new { name = "Unchanged" });
                using var denied = await fixture.PlacesApi.SendAsync(mutation);
                Assert.Equal(management ? HttpStatusCode.BadRequest : HttpStatusCode.Forbidden, denied.StatusCode);
            }
            Assert.False((await page.Locator("body").InnerTextAsync()).Contains(token, StringComparison.Ordinal),
                "The page must not render a bearer token.");
            var captureDirectory = Environment.GetEnvironmentVariable("CITYPICKS_IDENTITY_SCREENSHOT_DIRECTORY");
            if (captureDirectory is not null)
            {
                Directory.CreateDirectory(captureDirectory);
                var name = $"profile-{username}-{(favourites ? "traveler" : "no-favourites")}";
                await page.SetViewportSizeAsync(1440, 1000);
                await page.ScreenshotAsync(new() { Path = Path.Combine(captureDirectory, $"{name}-desktop.png"), FullPage = true });
                await page.SetViewportSizeAsync(390, 844);
                Assert.False(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth > document.documentElement.clientWidth"));
                await page.ScreenshotAsync(new() { Path = Path.Combine(captureDirectory, $"{name}-narrow.png"), FullPage = true });
            }
            await page.GotoAsync($"{fixture.BffUrl}/places/charles-bridge/edit");
            if (management) await Assertions.Expect(page.GetByLabel("Name", new() { Exact = true })).ToBeVisibleAsync();
            else await Assertions.Expect(page.Locator("main").GetByRole(AriaRole.Status)).ToContainTextAsync("denied");
        }
        finally { await page.Context.CloseAsync(); }
    }

    [Fact]
    public async Task PublicConfigurationAndRealClientAllowOnlyTheApprovedBrowserFlow()
    {
        var session = await fixture.Bff.GetFromJsonAsync<JsonObject>("/auth/user");
        Assert.Equal(new[] { "authenticated", "claims", "csrfToken" }, session!.Select(item => item.Key).Order().ToArray());
        Assert.False(session!["authenticated"]!.GetValue<bool>());
        Assert.Empty(session["claims"]!.AsArray());
        using var admin = await fixture.CreateAdminClientAsync();
        var clients = await admin.GetFromJsonAsync<JsonArray>("/admin/realms/city-picks/clients?clientId=city-picks-bff");
        var client = clients![0]!;
        Assert.False(client["publicClient"]!.GetValue<bool>());
        Assert.True(client["standardFlowEnabled"]!.GetValue<bool>());
        Assert.False(client["implicitFlowEnabled"]!.GetValue<bool>());
        Assert.False(client["directAccessGrantsEnabled"]!.GetValue<bool>());
        Assert.False(client["serviceAccountsEnabled"]!.GetValue<bool>());
        Assert.Equal("S256", client["attributes"]!["pkce.code.challenge.method"]!.GetValue<string>());
        Assert.Equal($"{fixture.BffUrl}/signin-oidc", Assert.Single(client["redirectUris"]!.AsArray())!.GetValue<string>());
        Assert.Empty(client["webOrigins"]!.AsArray());
        Assert.Equal($"{fixture.BffUrl}/signout-callback-oidc", client["attributes"]!["post.logout.redirect.uris"]!.GetValue<string>());
    }

    [Fact]
    public async Task CompleteLogoutEndsApplicationAndKeycloakSessions()
    {
        var page = await fixture.NewPageAsync();
        try
        {
            await SignInAndGetApiTestTokenAsync(page, "traveler");
            var callback = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            page.Request += (_, request) =>
            {
                if (new Uri(request.Url).AbsolutePath == "/signout-callback-oidc") callback.TrySetResult(true);
            };
            await page.GetByRole(AriaRole.Button, new() { Name = "Sign out", Exact = true }).ClickAsync();
            await callback.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true })).ToBeVisibleAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
            await Assertions.Expect(page.Locator("#username")).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator("#password")).ToBeVisibleAsync();
        }
        finally { await page.Context.CloseAsync(); }
    }

    [Theory]
    [InlineData("traveler")]
    [InlineData("curator")]
    public async Task PreparedUsersCanOpenStockAccountConsole(string username)
    {
        var page = await fixture.NewPageAsync();
        try
        {
            await SignInAndGetApiTestTokenAsync(page, username);
            await VerifyAccountConsoleAsync(page, username);
        }
        finally { await page.Context.CloseAsync(); }
    }

    [Fact]
    public async Task RegistrationGrantsOnlyTravelerAndEnablesAccountConsole()
    {
        var page = await fixture.NewPageAsync();
        var username = $"registered-{Guid.NewGuid():N}";
        try
        {
            await page.GotoAsync($"{fixture.BffUrl}/profile");
            await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).Last.ClickAsync();
            await page.GetByRole(AriaRole.Link, new() { Name = "Register", Exact = true }).ClickAsync();
            await page.Locator("#username").FillAsync(username);
            await page.Locator("#email").FillAsync($"{username}@example.test");
            await page.Locator("#firstName").FillAsync("New");
            await page.Locator("#lastName").FillAsync("Traveler");
            await page.Locator("#password").FillAsync("demo");
            await page.Locator("#password-confirm").FillAsync("demo");
            await page.GetByRole(AriaRole.Button, new() { Name = "Register", Exact = true }).ClickAsync();
            await Assertions.Expect(page.Locator("#api-identity")).ToContainTextAsync(username);
            await Assertions.Expect(page.Locator("#api-identity dd").Last).ToHaveTextAsync("traveler");
            await Assertions.Expect(page.Locator("#permissions")).ToContainTextAsync("Favourites: Allowed");
            await Assertions.Expect(page.Locator("#permissions")).ToContainTextAsync("Management: Denied");
            await VerifyAccountConsoleAsync(page, username);
            await fixture.ReapplyConfigurationAsync();
            await page.GotoAsync($"{fixture.BffUrl}/profile");
            await Assertions.Expect(page.Locator("#api-identity")).ToContainTextAsync(username);
        }
        finally { await page.Context.CloseAsync(); }
    }

    [Fact]
    public async Task MissingOptionalClaimsAreNotInventedAndUnrelatedRolesStayOut()
    {
        var username = $"sparse-{Guid.NewGuid():N}";
        var id = await fixture.CreateTestUserAsync(username, "traveler");
        using var admin = await fixture.CreateAdminClientAsync();
        var user = (await admin.GetFromJsonAsync<JsonObject>($"/admin/realms/city-picks/users/{id}"))!;
        var profile = await admin.GetFromJsonAsync<JsonObject>("/admin/realms/city-picks/users/profile");
        var optionalProfile = profile!.DeepClone();
        foreach (var attribute in optionalProfile["attributes"]!.AsArray())
        {
            if (new[] { "email", "firstName", "lastName" }.Contains(attribute!["name"]!.GetValue<string>())) attribute.AsObject().Remove("required");
        }
        try
        {
            using var profileUpdate = await admin.PutAsJsonAsync("/admin/realms/city-picks/users/profile", optionalProfile);
            profileUpdate.EnsureSuccessStatusCode();
            user["email"] = "";
            user["firstName"] = "";
            user["lastName"] = "";
            using var update = await admin.PutAsJsonAsync($"/admin/realms/city-picks/users/{id}", user);
            update.EnsureSuccessStatusCode();
            using var createRole = await admin.PostAsJsonAsync("/admin/realms/city-picks/roles", new { name = "unrelated" });
            createRole.EnsureSuccessStatusCode();
            var role = await admin.GetFromJsonAsync<JsonObject>("/admin/realms/city-picks/roles/unrelated");
            using var assign = await admin.PostAsJsonAsync($"/admin/realms/city-picks/users/{id}/role-mappings/realm", new[] { role });
            assign.EnsureSuccessStatusCode();
            var page = await fixture.NewPageAsync();
            try
            {
                await SignInAndGetApiTestTokenAsync(page, username);
                foreach (var panel in new[] { "#browser-identity", "#api-identity" })
                {
                    await Assertions.Expect(page.Locator($"{panel} dd").Nth(0)).ToHaveTextAsync("Not provided");
                    await Assertions.Expect(page.Locator($"{panel} dd").Nth(4)).ToHaveTextAsync("Not provided");
                    await Assertions.Expect(page.Locator($"{panel} dd").Last).ToHaveTextAsync("traveler");
                }
                await page.SetViewportSizeAsync(390, 844);
                Assert.False(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth > document.documentElement.clientWidth"));
            }
            finally { await page.Context.CloseAsync(); }
        }
        finally
        {
            await fixture.DeleteTestUserAsync(id);
            using var delete = await admin.DeleteAsync("/admin/realms/city-picks/roles/unrelated");
            delete.EnsureSuccessStatusCode();
            using var restoreProfile = await admin.PutAsJsonAsync("/admin/realms/city-picks/users/profile", profile);
            restoreProfile.EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task GenuineWrongAudienceAndExpiredBrowserTokensAreRejected()
    {
        using var admin = await fixture.CreateAdminClientAsync();
        var clients = await admin.GetFromJsonAsync<JsonArray>("/admin/realms/city-picks/clients?clientId=city-picks-bff");
        var template = clients![0]!.AsObject();
        foreach (var kind in new[] { "wrong-audience", "expired" })
        {
            var client = template.DeepClone().AsObject();
            client.Remove("id");
            var clientId = $"token-fixture-{kind}";
            client["clientId"] = clientId;
            client["publicClient"] = true;
            client.Remove("secret");
            client["redirectUris"] = new JsonArray($"{fixture.BffUrl}/token-fixture-callback");
            foreach (var mapper in client["protocolMappers"]!.AsArray()) mapper!.AsObject().Remove("id");
            var audienceMapper = client["protocolMappers"]!.AsArray().Single(mapper => mapper!["name"]!.GetValue<string>() == "api-audience")!;
            if (kind == "wrong-audience") audienceMapper["config"]!["included.custom.audience"] = "another-api";
            if (kind == "expired") client["attributes"]!["access.token.lifespan"] = "1";
            using var create = await admin.PostAsJsonAsync("/admin/realms/city-picks/clients", client);
            create.EnsureSuccessStatusCode();
            var page = await fixture.NewPageAsync();
            try
            {
                var token = await ObtainPkceTokenAsync(page, clientId);
                var jwt = new JsonWebToken(token);
                Assert.Equal($"{fixture.KeycloakUrl}/realms/city-picks", jwt.Issuer);
                Assert.Equal(kind == "wrong-audience" ? "another-api" : "city-picks-api", Assert.Single(jwt.Audiences));
                using var keysClient = new HttpClient();
                var keys = new JsonWebKeySet(await keysClient.GetStringAsync($"{fixture.KeycloakUrl}/realms/city-picks/protocol/openid-connect/certs"));
                var validation = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
                {
                    IssuerSigningKeys = keys.GetSigningKeys(), ValidateIssuer = false, ValidateAudience = false,
                    ValidateLifetime = false, RequireSignedTokens = true
                });
                Assert.True(validation.IsValid, "The negative fixture must retain a genuine Keycloak signature.");
                if (kind == "expired") await Task.Delay(TimeSpan.FromSeconds(2));
                await AssertUnauthorizedAsync(token);
            }
            finally
            {
                await page.Context.CloseAsync();
                var created = await admin.GetFromJsonAsync<JsonArray>($"/admin/realms/city-picks/clients?clientId={clientId}");
                using var delete = await admin.DeleteAsync($"/admin/realms/city-picks/clients/{created![0]!["id"]!.GetValue<string>()}");
                delete.EnsureSuccessStatusCode();
            }
        }
    }

    [Fact]
    public async Task InvalidSignatureIsRejectedDespiteCorrectIssuerAudienceAndLifetime()
    {
        var page = await fixture.NewPageAsync();
        try
        {
            var token = await SignInAndGetApiTestTokenAsync(page, "traveler");
            var parts = token.Split('.');
            var signature = Base64UrlEncoder.DecodeBytes(parts[2]);
            signature[0] ^= 1;
            await AssertUnauthorizedAsync($"{parts[0]}.{parts[1]}.{Base64UrlEncoder.Encode(signature)}");
        }
        finally { await page.Context.CloseAsync(); }
    }

    [Fact]
    public async Task WrongIssuerIsRejectedEvenWithATrustedSignatureAndCorrectAudienceAndLifetime()
    {
        // A narrowly scoped invalid-token fixture: publish a temporary RSA key through
        // the real disposable realm, then sign only a wrong-issuer token with that key.
        using var admin = await fixture.CreateAdminClientAsync();
        using var rsa = RSA.Create(2048);
        var realm = await admin.GetFromJsonAsync<JsonObject>("/admin/realms/city-picks");
        using var create = await admin.PostAsJsonAsync("/admin/realms/city-picks/components", new
        {
            name = "wrong-issuer-fixture", providerId = "rsa", providerType = "org.keycloak.keys.KeyProvider",
            parentId = realm!["id"]!.GetValue<string>(),
            config = new Dictionary<string, string[]>
            {
                ["privateKey"] = [rsa.ExportPkcs8PrivateKeyPem()], ["priority"] = ["0"],
                ["active"] = ["false"], ["enabled"] = ["true"], ["algorithm"] = ["RS256"]
            }
        });
        create.EnsureSuccessStatusCode();
        var componentId = create.Headers.Location!.Segments.Last();
        try
        {
            using var http = new HttpClient();
            var keys = new JsonWebKeySet(await http.GetStringAsync($"{fixture.KeycloakUrl}/realms/city-picks/protocol/openid-connect/certs"));
            var publicModulus = Base64UrlEncoder.Encode(rsa.ExportParameters(false).Modulus);
            var keyId = keys.Keys.Single(key => key.N == publicModulus).KeyId;
            var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = "https://unrelated.example.test/realms/other", Audience = "city-picks-api",
                Claims = new Dictionary<string, object> { ["sub"] = "invalid-issuer-fixture" },
                Expires = DateTime.UtcNow.AddMinutes(5),
                SigningCredentials = new SigningCredentials(new RsaSecurityKey(rsa) { KeyId = keyId }, SecurityAlgorithms.RsaSha256)
            });
            var validation = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
            {
                IssuerSigningKeys = keys.GetSigningKeys(), ValidateIssuer = false,
                ValidAudience = "city-picks-api", ValidateLifetime = true, ClockSkew = TimeSpan.Zero
            });
            Assert.True(validation.IsValid, "The wrong-issuer fixture must have a trusted signature, valid audience and lifetime.");
            await AssertUnauthorizedAsync(token);
        }
        finally
        {
            using var delete = await admin.DeleteAsync($"/admin/realms/city-picks/components/{componentId}");
            delete.EnsureSuccessStatusCode();
        }
    }

    private async Task AssertUnauthorizedAsync(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/identity");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await fixture.PlacesApi.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CuratorRoleDoesNotImplicitlyGrantTraveler()
    {
        using var admin = await fixture.CreateAdminClientAsync();
        var users = await admin.GetFromJsonAsync<JsonArray>("/admin/realms/city-picks/users?username=curator&exact=true");
        var id = users![0]!["id"]!.GetValue<string>();
        var role = await admin.GetFromJsonAsync<JsonObject>("/admin/realms/city-picks/roles/traveler");
        using var removeRequest = new HttpRequestMessage(HttpMethod.Delete, $"/admin/realms/city-picks/users/{id}/role-mappings/realm") { Content = JsonContent.Create(new[] { role }) };
        using var remove = await admin.SendAsync(removeRequest);
        remove.EnsureSuccessStatusCode();
        try { await BrowserSessionAndIndependentApiTokensExposeOwnRolesAndEnforceManagementPolicy("curator", false, "curator"); }
        finally
        {
            using var restore = await admin.PostAsJsonAsync($"/admin/realms/city-picks/users/{id}/role-mappings/realm", new[] { role });
            restore.EnsureSuccessStatusCode();
        }
    }

    private async Task<string> ObtainPkceTokenAsync(IPage page, string clientId)
    {
        var verifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Guid.NewGuid().ToString("N");
        var redirect = $"{fixture.BffUrl}/token-fixture-callback";
        var code = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        page.FrameNavigated += (_, frame) =>
        {
            if (!frame.Url.StartsWith(redirect, StringComparison.Ordinal)) return;
            var values = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(frame.Url).Query);
            if (values["state"] == state && values.TryGetValue("code", out var value)) code.TrySetResult(value.ToString());
            else code.TrySetException(new InvalidOperationException("PKCE callback did not contain the expected state and authorization code."));
        };
        await page.GotoAsync($"{fixture.KeycloakUrl}/realms/city-picks/protocol/openid-connect/auth?client_id={clientId}&redirect_uri={Uri.EscapeDataString(redirect)}&response_type=code&scope=openid%20profile%20email&state={state}&code_challenge={challenge}&code_challenge_method=S256");
        await page.Locator("#username").FillAsync("traveler");
        await page.Locator("#password").FillAsync("demo");
        await page.Locator("#kc-login").ClickAsync();
        using var http = new HttpClient();
        string authorizationCode;
        try { authorizationCode = await code.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (TimeoutException)
        {
            throw new InvalidOperationException($"PKCE fixture {clientId} stopped at {new Uri(page.Url).AbsolutePath}: {await page.Locator("body").InnerTextAsync()}");
        }
        using var response = await http.PostAsync($"{fixture.KeycloakUrl}/realms/city-picks/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId, ["grant_type"] = "authorization_code", ["redirect_uri"] = redirect,
                ["code_verifier"] = verifier, ["code"] = authorizationCode
            }));
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        return body!["access_token"]!.GetValue<string>();
    }

    private async Task VerifyAccountConsoleAsync(IPage page, string username)
    {
        var accountResponse = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        page.Response += (_, response) =>
        {
            if (new Uri(response.Url).AbsolutePath.TrimEnd('/') == "/realms/city-picks/account")
                accountResponse.TrySetResult(response.Status);
        };
        await page.GotoAsync($"{fixture.KeycloakUrl}/realms/city-picks/account/");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Personal info", Exact = true })).ToBeVisibleAsync();
        Assert.Equal(200, await accountResponse.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        await Assertions.Expect(page.GetByRole(AriaRole.Textbox, new() { Name = "Username", Exact = true })).ToHaveValueAsync(username);
        await page.GetByRole(AriaRole.Button, new() { Name = "Account security", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "Signing in", Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Signing in", Exact = true })).ToBeVisibleAsync();
    }

    private Task<string> SignInAndGetApiTestTokenAsync(IPage page, string username) =>
        fixture.SignInAndGetApiTestTokenAsync(page, username);
}
