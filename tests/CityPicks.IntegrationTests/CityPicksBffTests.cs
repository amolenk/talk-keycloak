using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Xunit;

namespace CityPicks.IntegrationTests;

[Collection(CityPicksStackCollection.Name)]
public sealed class CityPicksBffTests(CityPicksStackFixture fixture)
{
    [Fact]
    public async Task Api401UpdatesTheVisibleAuthenticationState()
    {
        var page = await fixture.NewPageAsync();
        try
        {
            await fixture.SignInAndGetApiTestTokenAsync(page, "visitor");
            await page.RouteAsync("**/api/identity", route => route.FulfillAsync(new() { Status = 401 }));
            await page.ReloadAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Sign out", Exact = true })).ToHaveCountAsync(0);
            await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).First).ToBeEnabledAsync();
        }
        finally { await page.Context.CloseAsync(); }
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("favourites")]
    public async Task FailedSessionDiscoveryCanRestoreAuthenticatedPageWithoutReloading(string path)
    {
        var page = await fixture.NewPageAsync();
        try
        {
            await fixture.SignInAndGetApiTestTokenAsync(page, "visitor");
            await page.RouteAsync("**/auth/user", route => route.FulfillAsync(new() { Status = 503 }));
            await page.GotoAsync(fixture.BffUrl + "/" + path);
            await Assertions.Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("session could not be checked");
            await page.UnrouteAsync("**/auth/user");
            await page.GetByRole(AriaRole.Button, new() { Name = "Retry session check" }).ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Alert)).ToHaveCountAsync(0);
            await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Sign out", Exact = true })).ToBeVisibleAsync();
            if (path == "profile") await Assertions.Expect(page.Locator("#api-identity")).ToContainTextAsync("visitor");
            else await Assertions.Expect(page.GetByText("Loading your favourites…", new() { Exact = true })).ToHaveCountAsync(0);
        }
        finally { await page.Context.CloseAsync(); }
    }

    [Fact]
    public async Task BrowserUsesProtectedCookiesAndNeverSendsOrStoresRawTokens()
    {
        var page = await fixture.NewPageAsync();
        var requests = new List<IRequest>();
        page.Request += (_, request) => requests.Add(request);
        try
        {
            await fixture.SignInAndGetApiTestTokenAsync(page, "visitor");
            var browserApiRequests = requests.Where(r => new Uri(r.Url).AbsolutePath.StartsWith("/api/", StringComparison.Ordinal)).ToArray();
            Assert.NotEmpty(browserApiRequests);
            Assert.All(browserApiRequests, r => Assert.False(r.Headers.ContainsKey("authorization")));
            Assert.DoesNotContain(requests, r => new Uri(r.Url).AbsolutePath.EndsWith("/openid-connect/token", StringComparison.Ordinal));
            var cookies = await page.Context.CookiesAsync([fixture.BffUrl]);
            var session = cookies.Where(c => c.Name.StartsWith("__Host-city-picks", StringComparison.Ordinal) && !c.Name.Contains("csrf")).ToArray();
            Assert.NotEmpty(session);
            Assert.All(session, cookie => { Assert.True(cookie.HttpOnly); Assert.True(cookie.Secure); Assert.Equal("/", cookie.Path); });
            var stored = await page.EvaluateAsync<string>("() => JSON.stringify({local: {...localStorage}, session: {...sessionStorage}})");
            Assert.DoesNotContain("access_token", stored);
            Assert.DoesNotContain("refresh_token", stored);
            var user = await page.EvaluateAsync<string>("""
                async () => {
                    const response = await fetch('/auth/user');
                    if (!response.ok) throw new Error(`Session discovery failed: ${response.status}`);
                    return await response.text();
                }
                """);
            var json = JsonNode.Parse(user)!;
            Assert.True(json["authenticated"]!.GetValue<bool>());
            Assert.Equal(new[] { "authenticated", "claims", "csrfToken" }, json.AsObject().Select(p => p.Key).Order());
        }
        finally { await page.Context.CloseAsync(); }
    }

    [Fact]
    public async Task MutationsAndLogoutRequireCsrfAndBearerAloneCannotAuthenticateToBff()
    {
        var page = await fixture.NewPageAsync();
        try
        {
            var token = await fixture.SignInAndGetApiTestTokenAsync(page, "visitor");
            foreach (var path in new[] { "/api/favourites/charles-bridge", "/api/places/charles-bridge", "/auth/logout" })
            {
                using var request = new HttpRequestMessage(path == "/auth/logout" ? HttpMethod.Post : HttpMethod.Put, path);
                using var response = await fixture.SendBffAsync(request, token, csrf: false);
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            }
            using var bearer = new HttpRequestMessage(HttpMethod.Get, "/api/identity");
            bearer.Headers.Authorization = new("Bearer", token);
            using var denied = await fixture.Bff.SendAsync(bearer);
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            Assert.Null(denied.Headers.Location);
            using var unknown = await fixture.Bff.GetAsync("/api/not-a-route");
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        }
        finally { await page.Context.CloseAsync(); }
    }

    [Fact]
    public async Task DuendeRefreshesExpiredAccessTokensWithoutBrowserTokenRequests()
    {
        using var admin = await fixture.CreateAdminClientAsync();
        var clients = await admin.GetFromJsonAsync<JsonArray>("/admin/realms/city-picks/clients?clientId=city-picks-bff");
        var client = clients![0]!.AsObject();
        var original = client.DeepClone();
        client["attributes"]!["access.token.lifespan"] = "5";
        var path = $"/admin/realms/city-picks/clients/{client["id"]!.GetValue<string>()}";
        using var update = await admin.PutAsJsonAsync(path, client);
        update.EnsureSuccessStatusCode();
        var page = await fixture.NewPageAsync();
        try
        {
            await fixture.SignInAndGetApiTestTokenAsync(page, "visitor");
            await Task.Delay(TimeSpan.FromSeconds(7));
            await page.ReloadAsync();
            await Assertions.Expect(page.Locator("#api-identity")).ToContainTextAsync("visitor");
            await Assertions.Expect(page.Locator("#api-identity [role=alert]")).ToHaveCountAsync(0);
        }
        finally
        {
            await page.Context.CloseAsync();
            using var restore = await admin.PutAsJsonAsync(path, original);
            restore.EnsureSuccessStatusCode();
        }
    }
}
