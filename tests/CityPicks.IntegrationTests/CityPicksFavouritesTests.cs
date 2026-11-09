using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CityPicks.Web.Models;
using Microsoft.Playwright;
using System.Text.Json.Nodes;
using Xunit;

namespace CityPicks.IntegrationTests;

[Collection(CityPicksStackCollection.Name)]
public sealed class CityPicksFavouritesTests(CityPicksStackFixture fixture)
{
    [Fact]
    public async Task ASecondVisitorCannotListOrRemoveAnotherAccountsFavouritesOrChooseAnOwner()
    {
        var username = $"second-{Guid.NewGuid():N}";
        var id = await fixture.CreateTestUserAsync(username, "visitor");
        var firstPage = await fixture.NewPageAsync();
        var secondPage = await fixture.NewPageAsync();
        string? firstToken = null;
        try
        {
            firstToken = await fixture.SignInAndGetApiTestTokenAsync(firstPage, "visitor");
            var secondToken = await fixture.SignInAndGetApiTestTokenAsync(secondPage, username);
            await ExpectStatusAsync(HttpMethod.Put, "/api/favourites/charles-bridge", firstToken, HttpStatusCode.NoContent);
            Assert.Empty(await ListAsync(secondToken));
            await ExpectStatusAsync(HttpMethod.Delete, "/api/favourites/charles-bridge", secondToken, HttpStatusCode.NoContent);
            Assert.Equal("charles-bridge", Assert.Single(await ListAsync(firstToken)).Id);

            using var identityResponse = await SendAsync(HttpMethod.Get, "/api/identity", firstToken);
            var owner = (await identityResponse.Content.ReadFromJsonAsync<IdentityView>())!;
            var forgedQuery = $"?owner={owner.Subject}&subject={owner.Subject}&issuer={Uri.EscapeDataString(owner.Issuer!)}&userId={owner.Subject}";
            using var forgedList = await SendAsync(HttpMethod.Get, "/api/favourites" + forgedQuery, secondToken);
            Assert.Equal(HttpStatusCode.OK, forgedList.StatusCode);
            Assert.Empty((await forgedList.Content.ReadFromJsonAsync<PlaceView[]>())!);
            foreach (var method in new[] { HttpMethod.Put, HttpMethod.Delete })
            {
                using var forged = new HttpRequestMessage(method, "/api/favourites/old-town-square" + forgedQuery);
                forged.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secondToken);
                forged.Headers.Add("X-Owner-Subject", owner.Subject);
                forged.Content = JsonContent.Create(new { owner = owner.Subject, subject = owner.Subject, issuer = owner.Issuer, userId = owner.Subject });
                using var response = await fixture.SendBffAsync(forged, secondToken);
                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
                Assert.Equal("charles-bridge", Assert.Single(await ListAsync(firstToken)).Id);
                Assert.Equal(method == HttpMethod.Put ? 1 : 0, (await ListAsync(secondToken)).Length);
            }
            await ExpectStatusAsync(HttpMethod.Delete, "/api/favourites/charles-bridge" + forgedQuery, secondToken, HttpStatusCode.NoContent);
            Assert.Equal("charles-bridge", Assert.Single(await ListAsync(firstToken)).Id);
        }
        finally
        {
            if (firstToken is not null) await ExpectStatusAsync(HttpMethod.Delete, "/api/favourites/charles-bridge", firstToken, HttpStatusCode.NoContent);
            await firstPage.Context.CloseAsync();
            await secondPage.Context.CloseAsync();
            await fixture.DeleteTestUserAsync(id);
        }
    }

    [Fact]
    public async Task FavouritesPersistThroughLogoutProfileAndRoleChangesButNotAccountRecreation()
    {
        var username = $"continuity-{Guid.NewGuid():N}";
        var id = await fixture.CreateTestUserAsync(username, "visitor");
        var page = await fixture.NewPageAsync();
        try
        {
            var token = await fixture.SignInAndGetApiTestTokenAsync(page, username);
            await ExpectStatusAsync(HttpMethod.Put, "/api/favourites/old-town-square", token, HttpStatusCode.NoContent);
            await page.GetByRole(AriaRole.Button, new() { Name = "Sign out", Exact = true }).ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true })).ToBeEnabledAsync();
            token = await fixture.SignInAndGetApiTestTokenAsync(page, username);
            Assert.Equal("old-town-square", Assert.Single(await ListAsync(token)).Id);
            await page.GotoAsync($"{fixture.BffUrl}/favourites");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Old Town Square");
            await page.Context.CloseAsync();

            using (var admin = await fixture.CreateAdminClientAsync())
            {
                var realm = (await admin.GetFromJsonAsync<JsonObject>("/admin/realms/city-picks"))!;
                using var allowRename = await admin.PutAsJsonAsync("/admin/realms/city-picks", new { editUsernameAllowed = true });
                allowRename.EnsureSuccessStatusCode();
                try
                {
                    var user = (await admin.GetFromJsonAsync<JsonObject>($"/admin/realms/city-picks/users/{id}"))!;
                    username += "-renamed";
                    user["username"] = username;
                    user["email"] = $"{username}@example.test";
                    using var update = await admin.PutAsJsonAsync($"/admin/realms/city-picks/users/{id}", user);
                    Assert.True(update.IsSuccessStatusCode, await update.Content.ReadAsStringAsync());
                }
                finally
                {
                    using var restoreRename = await admin.PutAsJsonAsync("/admin/realms/city-picks", new { editUsernameAllowed = realm["editUsernameAllowed"]?.GetValue<bool>() ?? false });
                    restoreRename.EnsureSuccessStatusCode();
                }
            }
            page = await fixture.NewPageAsync();
            token = await fixture.SignInAndGetApiTestTokenAsync(page, username);
            using var identityResponse = await SendAsync(HttpMethod.Get, "/api/identity", token);
            var identity = (await identityResponse.Content.ReadFromJsonAsync<IdentityView>())!;
            Assert.Equal(id, identity.Subject);
            Assert.Equal($"{username}@example.test", identity.Email);
            Assert.Equal("old-town-square", Assert.Single(await ListAsync(token)).Id);
            await page.Context.CloseAsync();

            await fixture.SetTestUserRolesAsync(id);
            page = await fixture.NewPageAsync();
            var withoutRole = await fixture.SignInAndGetApiTestTokenAsync(page, username);
            foreach (var method in new[] { HttpMethod.Get, HttpMethod.Put, HttpMethod.Delete })
                await ExpectStatusAsync(method, method == HttpMethod.Get ? "/api/favourites" : "/api/favourites/old-town-square", withoutRole, HttpStatusCode.Forbidden);
            await page.GotoAsync($"{fixture.BffUrl}/favourites");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Favourites require the visitor role");
            Assert.Equal("old-town-square", Assert.Single(await ListAsync(token)).Id); // Previously issued tokens aren't immediately revoked.
            await page.Context.CloseAsync();

            await fixture.SetTestUserRolesAsync(id, "visitor");
            page = await fixture.NewPageAsync();
            token = await fixture.SignInAndGetApiTestTokenAsync(page, username);
            Assert.Equal("old-town-square", Assert.Single(await ListAsync(token)).Id);
            await page.Context.CloseAsync();

            var originalId = id;
            await fixture.DeleteTestUserAsync(id);
            id = await fixture.CreateTestUserAsync(username, "visitor");
            Assert.NotEqual(originalId, id);
            page = await fixture.NewPageAsync();
            token = await fixture.SignInAndGetApiTestTokenAsync(page, username);
            Assert.Empty(await ListAsync(token));
            await ExpectStatusAsync(HttpMethod.Delete, "/api/favourites/old-town-square", token, HttpStatusCode.NoContent);
            Assert.Empty(await ListAsync(token));
        }
        finally { await page.Context.CloseAsync(); await fixture.DeleteTestUserAsync(id); }
    }

    [Theory]
    [InlineData("curator-only")]
    public async Task AnonymousAndAuthenticatedNonVisitorsCannotUseFavourites(string account)
    {
        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Put, HttpMethod.Delete })
            await ExpectStatusAsync(method, method == HttpMethod.Get ? "/api/favourites" : "/api/favourites/charles-bridge", null, HttpStatusCode.Unauthorized);
        string? id = null;
        var username = account;
        if (account == "curator-only")
        {
            username = $"curator-only-{Guid.NewGuid():N}";
            id = await fixture.CreateTestUserAsync(username, "curator");
        }
        var page = await fixture.NewPageAsync();
        try
        {
            var token = await fixture.SignInAndGetApiTestTokenAsync(page, username);
            foreach (var method in new[] { HttpMethod.Get, HttpMethod.Put, HttpMethod.Delete })
                await ExpectStatusAsync(method, method == HttpMethod.Get ? "/api/favourites" : "/api/favourites/charles-bridge", token, HttpStatusCode.Forbidden);
            foreach (var path in new[] { "/favourites", "/places/charles-bridge" })
            {
                await page.GotoAsync(fixture.BffUrl + path);
                await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Favourites require the visitor role");
                await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Save to favourites", Exact = true })).ToHaveCountAsync(0);
            }
        }
        finally { await page.Context.CloseAsync(); if (id is not null) await fixture.DeleteTestUserAsync(id); }
    }

    [Fact]
    public async Task LoginReturnsToThePlaceWithoutSavingAndExplicitActionsGiveFeedback()
    {
        var page = await fixture.NewPageAsync();
        try
        {
            await page.GotoAsync($"{fixture.BffUrl}/favourites");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Sign in to see your private favourites");
            await page.GotoAsync($"{fixture.BffUrl}/places/charles-bridge");
            await page.GetByRole(AriaRole.Button, new() { Name = "Sign in to save favourites" }).ClickAsync();
            await page.Locator("#username").FillAsync("visitor");
            await page.Locator("#password").FillAsync("demo");
            await page.Locator("#kc-login").ClickAsync();
            await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Charles Bridge");
            var save = page.GetByRole(AriaRole.Button, new() { Name = "Save to favourites", Exact = true });
            await Assertions.Expect(save).ToBeVisibleAsync();
            await page.GetByRole(AriaRole.Link, new() { Name = "Your favourites", Exact = true }).ClickAsync();
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("You haven't saved any places yet");
            await page.GotoAsync($"{fixture.BffUrl}/places/charles-bridge");
            await page.RouteAsync("**/api/favourites/charles-bridge", route => route.FulfillAsync(new() { Status = 503 }));
            await save.ClickAsync();
            await Assertions.Expect(page.Locator("main").GetByRole(AriaRole.Alert)).ToContainTextAsync("could not be saved");
            await page.UnrouteAsync("**/api/favourites/charles-bridge");
            await save.FocusAsync();
            await page.Keyboard.PressAsync("Enter");
            await Assertions.Expect(page.Locator("main").GetByRole(AriaRole.Status)).ToHaveTextAsync("Saved to your favourites.");
            await CaptureAsync(page, "place-saved");
            await page.ReloadAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Saved to favourites", Exact = true })).ToBeDisabledAsync();
            await page.GetByRole(AriaRole.Link, new() { Name = "Your favourites", Exact = true }).ClickAsync();
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Charles Bridge");
            await CaptureAsync(page, "favourites");
            await page.RouteAsync("**/api/favourites/charles-bridge", route => route.FulfillAsync(new() { Status = 503 }));
            var remove = page.GetByRole(AriaRole.Button, new() { Name = "Remove Charles Bridge from favourites", Exact = true });
            await remove.ClickAsync();
            await Assertions.Expect(page.Locator("main").GetByRole(AriaRole.Alert)).ToContainTextAsync("could not be removed");
            await Assertions.Expect(remove).ToBeVisibleAsync();
            await page.UnrouteAsync("**/api/favourites/charles-bridge");
            await remove.FocusAsync();
            await page.Keyboard.PressAsync("Enter");
            await Assertions.Expect(page.Locator("main").GetByRole(AriaRole.Status)).ToHaveTextAsync("Charles Bridge removed from your favourites.");
            await Assertions.Expect(page.Locator("main")).ToContainTextAsync("You haven't saved any places yet");
            await Assertions.Expect(page.Locator("h1")).ToBeFocusedAsync();
            await page.SetViewportSizeAsync(390, 844);
            Assert.False(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth > document.documentElement.clientWidth"));
        }
        finally { await page.Context.CloseAsync(); }
    }

    [Theory]
    [InlineData("visitor")]
    [InlineData("curator")]
    public async Task ExplicitSaveIsIdempotentAndAbsentRemovalIsHarmless(string username)
    {
        var page = await fixture.NewPageAsync();
        try
        {
            var token = await fixture.SignInAndGetApiTestTokenAsync(page, username);
            await ExpectStatusAsync(HttpMethod.Delete, "/api/favourites/charles-bridge", token, HttpStatusCode.NoContent);
            Assert.Empty(await ListAsync(token));
            var saves = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
                SendAsync(HttpMethod.Put, "/api/favourites/charles-bridge", token)));
            foreach (var response in saves)
            {
                using (response) Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            }
            Assert.Equal("charles-bridge", Assert.Single(await ListAsync(token)).Id);
            await ExpectStatusAsync(HttpMethod.Put, "/api/favourites/not-a-place", token, HttpStatusCode.NotFound);
            await ExpectStatusAsync(HttpMethod.Delete, "/api/favourites/charles-bridge", token, HttpStatusCode.NoContent);
            await ExpectStatusAsync(HttpMethod.Delete, "/api/favourites/charles-bridge", token, HttpStatusCode.NoContent);
            await ExpectStatusAsync(HttpMethod.Delete, "/api/favourites/not-a-place", token, HttpStatusCode.NoContent);
            Assert.Empty(await ListAsync(token));
        }
        finally { await page.Context.CloseAsync(); }
    }

    private static async Task CaptureAsync(IPage page, string name)
    {
        var directory = Environment.GetEnvironmentVariable("CITYPICKS_FAVOURITES_SCREENSHOT_DIRECTORY");
        if (directory is null) return;
        Directory.CreateDirectory(directory);
        foreach (var (width, height, suffix) in new[] { (1440, 1000, "desktop"), (390, 844, "narrow") })
        {
            await page.SetViewportSizeAsync(width, height);
            Assert.False(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth > document.documentElement.clientWidth"));
            await page.ScreenshotAsync(new() { Path = Path.Combine(directory, $"{name}-{suffix}.png"), FullPage = true });
        }
    }

    private async Task<PlaceView[]> ListAsync(string token)
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/favourites", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PlaceView[]>())!;
    }

    private async Task ExpectStatusAsync(HttpMethod method, string path, string? token, HttpStatusCode status)
    {
        using var response = await SendAsync(method, path, token);
        Assert.Equal(status, response.StatusCode);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? token)
    {
        using var request = new HttpRequestMessage(method, path);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await fixture.SendBffAsync(request, token);
    }
}
