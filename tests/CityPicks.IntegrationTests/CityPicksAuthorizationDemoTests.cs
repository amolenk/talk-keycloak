using System.Net.Http.Json;
using CityPicks.Web.Models;
using Xunit;

namespace CityPicks.IntegrationTests;

[Collection(CityPicksStackCollection.Name)]
public sealed class CityPicksAuthorizationDemoTests(CityPicksStackFixture fixture)
{
    // The authorization contract after enabling CanManagePlaces in demo 2.
    [Theory]
    [InlineData(null, 401)]
    [InlineData("traveler", 403)]
    [InlineData("curator", 200)]
    public async Task UpdatingAPlaceRequiresACuratorSession(string? username, int expectedStatus)
    {
        var page = await fixture.NewPageAsync();
        try
        {
            var sessionToken = username is null ? null
                : await fixture.SignInAndGetApiTestTokenAsync(page, username);

            const string path = "/api/places/charles-bridge";
            var place = (await fixture.Bff.GetFromJsonAsync<PlaceView>(path))!;
            using var request = new HttpRequestMessage(HttpMethod.Put, path)
            {
                // Submit the existing values so this test leaves the place unchanged.
                Content = JsonContent.Create(PlaceEdit.From(place))
            };

            // SendBffAsync uses the signed-in session cookie and CSRF header, not a bearer.
            using var response = await fixture.SendBffAsync(request, sessionToken);

            Assert.Equal(expectedStatus, (int)response.StatusCode);
        }
        finally { await page.Context.CloseAsync(); }
    }
}
