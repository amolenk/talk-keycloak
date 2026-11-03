using System.Net;
using System.Net.Http.Json;
using CityPicks.Api.Data;
using CityPicks.Api.Services;
using Xunit;

namespace CityPicks.IntegrationTests;

[Collection(CityPicksStackCollection.Name)]
public sealed class CityPicksBrowsingTests(CityPicksStackFixture fixture)
{
    [Fact]
    public async Task AnonymousVisitorCanBrowsePreparedPlacesAndDetails()
    {
        var city = await fixture.PlacesApi.GetFromJsonAsync<CityEdition>("/api/city");
        var places = await fixture.PlacesApi.GetFromJsonAsync<Place[]>("/api/places");

        Assert.NotNull(city);
        Assert.Equal("Prague", city.Name);
        Assert.NotNull(places);
        Assert.Equal(4, places.Length);
        Assert.Equal(["charles-bridge", "lennon-wall", "old-town-square", "prague-castle"],
            places.Select(place => place.Id).Order().ToArray());
        Assert.All(places, place =>
        {
            Assert.False(string.IsNullOrWhiteSpace(place.Category));
            Assert.False(string.IsNullOrWhiteSpace(place.Description));
            Assert.False(string.IsNullOrWhiteSpace(place.Image));
            Assert.False(string.IsNullOrWhiteSpace(place.Address));
        });

        var detail = await fixture.PlacesApi.GetFromJsonAsync<Place>("/api/places/charles-bridge");
        Assert.NotNull(detail);
        Assert.Equal("Charles Bridge", detail.Name);
        Assert.Contains("Karlův", detail.Address, StringComparison.Ordinal);

        using var image = await fixture.PlacesApi.GetAsync("/images/charles-bridge.jpg");
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/jpeg", image.Content.Headers.ContentType?.MediaType);
        Assert.True((await image.Content.ReadAsByteArrayAsync()).Length > 50_000);

        using var unselectedImage = await fixture.PlacesApi.GetAsync("/images/not-in-prague-catalog.jpg");
        Assert.Equal(HttpStatusCode.NotFound, unselectedImage.StatusCode);

        var page = await fixture.NewPageAsync();
        try
        {
            await page.GotoAsync(fixture.BffUrl);
            await Microsoft.Playwright.Assertions.Expect(page.Locator("h1"))
                .ToHaveTextAsync("A little more Prague.");
            await page.SetViewportSizeAsync(1440, 1000);
            await CaptureScreenAsync(page, "grid-desktop.png");
            await page.SetViewportSizeAsync(390, 844);
            await CaptureScreenAsync(page, "grid-narrow.png");
            var hasHorizontalOverflow = await page.EvaluateAsync<bool>(
                "() => document.documentElement.scrollWidth > document.documentElement.clientWidth");
            Assert.False(hasHorizontalOverflow);

            await page.Keyboard.PressAsync("Tab");
            Assert.True(await page.Locator(".skip-link").IsVisibleAsync());
            Assert.Equal("solid", await page.EvaluateAsync<string>(
                "() => getComputedStyle(document.activeElement).outlineStyle"));
            await page.Keyboard.PressAsync("Enter");
            Assert.Equal("A little more Prague.", (await page.Locator("h1").First.TextContentAsync())?.Trim());
            await page.GetByRole(Microsoft.Playwright.AriaRole.Link, new() { Name = "Charles Bridge", Exact = true }).FocusAsync();
            await page.Keyboard.PressAsync("Enter");
            await Microsoft.Playwright.Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Charles Bridge");
            Assert.Equal("Charles Bridge", (await page.Locator("h1").TextContentAsync())?.Trim());
            Assert.Contains("Karlův", await page.Locator("address").TextContentAsync(), StringComparison.Ordinal);
            Assert.False(await page.EvaluateAsync<bool>(
                "() => document.documentElement.scrollWidth > document.documentElement.clientWidth"));
            await CaptureScreenAsync(page, "details-narrow.png");
            await page.SetViewportSizeAsync(1440, 1000);
            await CaptureScreenAsync(page, "details-desktop.png");
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task DetailsDistinguishMissingPlacesFromNetworkFailure()
    {
        var page = await fixture.NewPageAsync();
        try
        {
            await page.GotoAsync($"{fixture.BffUrl}/places/no-such-place");
            await Microsoft.Playwright.Assertions.Expect(page.GetByRole(Microsoft.Playwright.AriaRole.Alert))
                .ToContainTextAsync("could not be found");

            await page.RouteAsync("**/api/places/charles-bridge", route => route.AbortAsync("failed"));
            await page.GotoAsync($"{fixture.BffUrl}/places/charles-bridge");
            await Microsoft.Playwright.Assertions.Expect(page.GetByRole(Microsoft.Playwright.AriaRole.Alert))
                .ToContainTextAsync("unavailable right now");
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    private static async Task CaptureScreenAsync(Microsoft.Playwright.IPage page, string fileName)
    {
        var directory = Environment.GetEnvironmentVariable("CITYPICKS_SCREENSHOT_DIRECTORY");
        if (directory is null) return;

        Directory.CreateDirectory(directory);
        await page.Locator("img").EvaluateAllAsync<bool>(
            "images => Promise.all(images.map(image => image.decode())).then(() => true)");
        await page.ScreenshotAsync(new() { Path = Path.Combine(directory, fileName), FullPage = true });
    }
}
