using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CityPicks.Web.Models;
using Microsoft.Playwright;
using Xunit;

namespace CityPicks.IntegrationTests;

[Collection(CityPicksStackCollection.Name)]
public sealed class CityPicksManagementTests(CityPicksStackFixture fixture)
{
    private static bool Enabled => CityPicks.Api.Services.PlaceEndpoints.ManagementPolicy == "CanManagePlaces";

    [Fact]
    public async Task CuratorOnlyCanManageAnyPlaceWithoutFavouritesAndEditsPreserveSavedRelationships()
    {
        var username = $"curator-only-{Guid.NewGuid():N}";
        var userId = await fixture.CreateTestUserAsync(username, "curator");
        var curatorPage = await fixture.NewPageAsync();
        var visitorPage = await fixture.NewPageAsync();
        var original = (await fixture.PlacesApi.GetFromJsonAsync<PlaceView>("/api/places/charles-bridge"))!;
        string? curatorToken = null, visitorToken = null;
        var addedIds = new List<string>();
        try
        {
            curatorToken = await fixture.SignInAndGetApiTestTokenAsync(curatorPage, username);
            visitorToken = await fixture.SignInAndGetApiTestTokenAsync(visitorPage, "visitor");
            using var identity = await SendAsync(HttpMethod.Get, "/api/identity", curatorToken);
            var own = (await identity.Content.ReadFromJsonAsync<IdentityView>())!;
            Assert.False(own.Permissions.CanUseFavourites);
            Assert.Equal(Enabled, own.Permissions.CanManagePlaces);
            using var favouritesDenied = await SendAsync(HttpMethod.Get, "/api/favourites", curatorToken);
            Assert.Equal(HttpStatusCode.Forbidden, favouritesDenied.StatusCode);

            foreach (var token in new string?[] { null, visitorToken, curatorToken })
            foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put })
            {
                using var response = await SendAsync(method, method == HttpMethod.Post ? "/api/places" : "/api/places/charles-bridge", token, PlaceEdit.From(original));
                Assert.Equal(token is null ? HttpStatusCode.Unauthorized : token == curatorToken && Enabled
                    ? method == HttpMethod.Post ? HttpStatusCode.Created : HttpStatusCode.OK : HttpStatusCode.Forbidden, response.StatusCode);
                if (response.StatusCode == HttpStatusCode.Created)
                    addedIds.Add((await response.Content.ReadFromJsonAsync<PlaceView>())!.Id);
            }
            if (!Enabled) return;

            using var save = await SendAsync(HttpMethod.Put, "/api/favourites/charles-bridge", visitorToken);
            Assert.Equal(HttpStatusCode.NoContent, save.StatusCode);
            var changed = PlaceEdit.From(original);
            changed.Description = "A curator's updated river walk.";
            using var edit = await SendAsync(HttpMethod.Put, "/api/places/charles-bridge", curatorToken, changed);
            Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
            var updated = (await edit.Content.ReadFromJsonAsync<PlaceView>())!;
            Assert.Equal(original.Id, updated.Id);
            using var saved = await SendAsync(HttpMethod.Get, "/api/favourites", visitorToken);
            var favourite = Assert.Single((await saved.Content.ReadFromJsonAsync<PlaceView[]>())!);
            Assert.Equal(original.Id, favourite.Id);
            Assert.Equal(changed.Description, favourite.Description);

            // A second curator edits a place added by the curator-only identity.
            using var add = await SendAsync(HttpMethod.Post, "/api/places", curatorToken, changed);
            var added = (await add.Content.ReadFromJsonAsync<PlaceView>())!;
            addedIds.Add(added.Id);
            Assert.NotEqual(original.Id, added.Id);
            await curatorPage.Context.CloseAsync();
            curatorPage = await fixture.NewPageAsync();
            var bothToken = await fixture.SignInAndGetApiTestTokenAsync(curatorPage, "curator");
            using var otherEdit = await SendAsync(HttpMethod.Put, $"/api/places/{added.Id}", bothToken, PlaceEdit.From(original));
            Assert.Equal(HttpStatusCode.OK, otherEdit.StatusCode);
            Assert.Equal(added.Id, (await otherEdit.Content.ReadFromJsonAsync<PlaceView>())!.Id);

            foreach (var field in new[] { "Name", "Category", "Description", "Image", "Address" })
            foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put })
            foreach (var invalid in new string?[] { null, "", "   " })
            {
                var input = System.Text.Json.JsonSerializer.SerializeToNode(PlaceEdit.From(original))!.AsObject();
                input[field] = invalid;
                using var response = await SendAsync(method, method == HttpMethod.Post ? "/api/places" : "/api/places/charles-bridge", bothToken, input);
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            }
            foreach (var invalidImage in new[] { "https://example.test/photo.jpg", "../charles-bridge.jpg", "unprepared.jpg", "CHARLES-BRIDGE.JPG" })
            foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put })
            {
                changed.Image = invalidImage;
                using var response = await SendAsync(method, method == HttpMethod.Post ? "/api/places" : "/api/places/charles-bridge", bothToken, changed);
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            }
            foreach (var (field, maximum) in new[] { ("Name", 160), ("Category", 80), ("Description", 1000), ("Address", 240) })
            foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put })
            {
                var input = System.Text.Json.JsonSerializer.SerializeToNode(PlaceEdit.From(original))!.AsObject();
                input[field] = new string('x', maximum + 1);
                using var response = await SendAsync(method, method == HttpMethod.Post ? "/api/places" : "/api/places/charles-bridge", bothToken, input);
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            }
            using var missing = await SendAsync(HttpMethod.Put, "/api/places/does-not-exist", bothToken, PlaceEdit.From(original));
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
        finally
        {
            if (Enabled && curatorToken is not null)
            {
                using var restore = await SendAsync(HttpMethod.Put, "/api/places/charles-bridge", curatorToken, PlaceEdit.From(original));
                Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
            }
            if (visitorToken is not null)
            {
                using var remove = await SendAsync(HttpMethod.Delete, "/api/favourites/charles-bridge", visitorToken);
                Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
            }
            await curatorPage.Context.CloseAsync();
            await visitorPage.Context.CloseAsync();
            await fixture.DeleteTestUserAsync(userId);
            foreach (var id in addedIds) await fixture.DeleteAddedPlaceAsync(id);
        }
    }

    [Fact]
    public async Task OrdinaryEditorValidatesAddsAndVisiblyEditsWithoutChangingPlaceIdentity()
    {
        var page = await fixture.NewPageAsync();
        string? addedId = null;
        try
        {
            await fixture.SignInAndGetApiTestTokenAsync(page, "curator");
            await page.GotoAsync($"{fixture.BffUrl}/places/new");
            if (!Enabled)
            {
                await Assertions.Expect(page.Locator("main").GetByRole(AriaRole.Status)).ToContainTextAsync("denied");
                return;
            }
            var writes = 0;
            page.Request += (_, request) => { if (new Uri(request.Url).AbsolutePath.StartsWith("/api/places", StringComparison.Ordinal) && request.Method is "POST" or "PUT") writes++; };
            await page.GetByRole(AriaRole.Button, new() { Name = "Save place", Exact = true }).ClickAsync();
            await Assertions.Expect(page.Locator(".validation-errors")).ToContainTextAsync("Name field is required");
            Assert.Equal(0, writes);
            await page.GetByLabel("Name", new() { Exact = true }).FillAsync("   ");
            await page.GetByLabel("Category", new() { Exact = true }).FillAsync("Local walk");
            await page.GetByLabel("Description", new() { Exact = true }).FillAsync("A quiet route through the city.");
            await page.GetByLabel("Image", new() { Exact = true }).SelectOptionAsync("charles-bridge.jpg");
            await page.GetByLabel("Address", new() { Exact = true }).FillAsync("Prague riverbank");
            await page.GetByRole(AriaRole.Button, new() { Name = "Save place", Exact = true }).ClickAsync();
            await Assertions.Expect(page.Locator(".validation-errors")).ToContainTextAsync("Name field is required");
            Assert.Equal(0, writes);
            await page.GetByLabel("Name", new() { Exact = true }).FillAsync("Curator's riverside pick");
            foreach (var field in new[] { "Name", "Category", "Description", "Address" })
            {
                var control = page.GetByLabel(field, new() { Exact = true });
                var value = await control.InputValueAsync();
                await control.FillAsync("   ");
                await page.GetByRole(AriaRole.Button, new() { Name = "Save place", Exact = true }).ClickAsync();
                await Assertions.Expect(page.Locator(".validation-errors")).ToContainTextAsync($"{field} field is required");
                Assert.Equal(0, writes);
                await control.FillAsync(value);
            }
            await page.GetByLabel("Image", new() { Exact = true }).SelectOptionAsync("");
            await page.GetByRole(AriaRole.Button, new() { Name = "Save place", Exact = true }).ClickAsync();
            await Assertions.Expect(page.Locator(".validation-errors")).ToContainTextAsync("Image field is required");
            Assert.Equal(0, writes);
            await page.GetByLabel("Image", new() { Exact = true }).SelectOptionAsync("charles-bridge.jpg");
            await page.GetByLabel("Name", new() { Exact = true }).FocusAsync();
            await page.Keyboard.PressAsync("Tab");
            await Assertions.Expect(page.GetByLabel("Category", new() { Exact = true })).ToBeFocusedAsync();
            Assert.Equal("solid", await page.EvaluateAsync<string>("() => getComputedStyle(document.activeElement).outlineStyle"));
            await page.SetViewportSizeAsync(390, 844);
            Assert.False(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth > document.documentElement.clientWidth"));
            await page.GetByRole(AriaRole.Button, new() { Name = "Save place", Exact = true }).ClickAsync();
            await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Curator's riverside pick");
            var detailUrl = page.Url;
            addedId = new Uri(detailUrl).Segments.Last();
            await page.GetByRole(AriaRole.Link, new() { Name = "Edit place", Exact = true }).ClickAsync();
            await page.GetByLabel("Description", new() { Exact = true }).FillAsync("Visible ordinary editor change.");
            await page.GetByRole(AriaRole.Button, new() { Name = "Save place", Exact = true }).ClickAsync();
            await Assertions.Expect(page.Locator(".detail-description")).ToHaveTextAsync("Visible ordinary editor change.");
            Assert.Equal(detailUrl, page.Url);
            Assert.Equal(2, writes);
        }
        finally
        {
            await page.Context.CloseAsync();
            if (addedId is not null) await fixture.DeleteAddedPlaceAsync(addedId);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return path.StartsWith("/api/favourites", StringComparison.Ordinal)
            ? await fixture.SendBffAsync(request, token) : await fixture.PlacesApi.SendAsync(request);
    }

    [Fact]
    public async Task AnonymousVisitorsCannotNavigateToEitherEditorOrUseManagement()
    {
        var page = await fixture.NewPageAsync();
        try
        {
            foreach (var route in new[] { "/places/new", "/places/charles-bridge/edit" })
            {
                await page.GotoAsync(fixture.BffUrl + route);
                await Assertions.Expect(page.Locator("main").GetByRole(AriaRole.Status)).ToContainTextAsync("denied");
                await Assertions.Expect(page.Locator("form")).ToHaveCountAsync(0);
            }
        }
        finally { await page.Context.CloseAsync(); }
    }

    [Theory]
    [InlineData("visitor", false)]
    [InlineData("curator", true)]
    public async Task PermissionsControlsAndRealDemoRequestsAgreeWithSourcePolicy(string username, bool curator)
    {
        var page = await fixture.NewPageAsync();
        try
        {
            var token = await fixture.SignInAndGetApiTestTokenAsync(page, username);
            var allowed = Enabled && curator;
            await Assertions.Expect(page.Locator("#permissions")).ToContainTextAsync($"Management: {(allowed ? "Allowed" : "Denied")}");
            var tools = page.Locator("#demo-tools");
            Assert.Null(await tools.GetAttributeAsync("open"));
            await tools.Locator("summary").ClickAsync();
            foreach (var sendBearer in new[] { false, true })
            {
                var responseTask = page.WaitForResponseAsync(response => new Uri(response.Url).AbsolutePath.StartsWith("/api/places/", StringComparison.Ordinal) && response.Request.Method == "PUT");
                await tools.GetByRole(AriaRole.Button, new() { Name = sendBearer ? "Update through BFF" : "Update without session", Exact = true }).ClickAsync();
                var response = await responseTask;
                Assert.False(response.Request.Headers.ContainsKey("authorization"));
                Assert.Equal(!sendBearer ? 401 : allowed ? 200 : 403, response.Status);
                await Assertions.Expect(tools.GetByRole(AriaRole.Status)).ToContainTextAsync($"HTTP {response.Status}");
                var body = System.Text.Json.Nodes.JsonNode.Parse(response.Request.PostData!);
                var current = await fixture.PlacesApi.GetFromJsonAsync<PlaceView>(new Uri(response.Url).AbsolutePath);
                Assert.Equal(current!.Name, body!["name"]!.GetValue<string>());
                Assert.Equal(current.Category, body["category"]!.GetValue<string>());
                Assert.Equal(current.Description, body["description"]!.GetValue<string>());
                Assert.Equal(current.Image, body["image"]!.GetValue<string>());
                Assert.Equal(current.Address, body["address"]!.GetValue<string>());
                Assert.False((await page.Locator("body").InnerTextAsync()).Contains(token, StringComparison.Ordinal));
            }
            await page.GotoAsync(fixture.BffUrl);
            await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Add a place", Exact = true })).ToHaveCountAsync(allowed ? 1 : 0);
            await page.GotoAsync($"{fixture.BffUrl}/places/charles-bridge");
            await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Edit place", Exact = true })).ToHaveCountAsync(allowed ? 1 : 0);
            foreach (var route in new[] { "/places/new", "/places/charles-bridge/edit" })
            {
                await page.GotoAsync(fixture.BffUrl + route);
                if (allowed) await Assertions.Expect(page.GetByLabel("Name", new() { Exact = true })).ToBeVisibleAsync();
                else await Assertions.Expect(page.Locator("main").GetByRole(AriaRole.Status)).ToContainTextAsync("denied");
            }
        }
        finally { await page.Context.CloseAsync(); }
    }

    [Fact]
    public async Task PreparedImageCatalogueIsAvailableForTheEditor()
    {
        var images = await fixture.PlacesApi.GetFromJsonAsync<string[]>("/api/place-images");
        Assert.NotNull(images);
        Assert.Contains("charles-bridge.jpg", images);
        foreach (var image in images)
        {
            using var response = await fixture.PlacesApi.GetAsync($"/images/{image}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
