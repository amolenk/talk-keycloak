using System.Net.Http.Json;

namespace CityPicks.Web.Models;

public sealed class FavouritesClient(IHttpClientFactory clients)
{
    public const string MissingTraveler = "Favourites require the traveler role. You're signed in, but this account doesn't have that permission. If your roles have changed, sign out and sign in again.";
    public const string SessionEnded = "Your session has ended. Sign out and sign in again to use favourites.";

    public async Task<PlaceView[]> GetAsync() =>
        await clients.CreateClient("AuthenticatedApi").GetFromJsonAsync<PlaceView[]>("api/favourites")
        ?? throw new HttpRequestException("The API returned no favourites.");

    public async Task SaveAsync(string placeId)
    {
        using var response = await clients.CreateClient("AuthenticatedApi").PutAsync($"api/favourites/{Uri.EscapeDataString(placeId)}", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task RemoveAsync(string placeId)
    {
        using var response = await clients.CreateClient("AuthenticatedApi").DeleteAsync($"api/favourites/{Uri.EscapeDataString(placeId)}");
        response.EnsureSuccessStatusCode();
    }
}
