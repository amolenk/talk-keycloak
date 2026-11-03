using System.Net.Http.Json;

namespace CityPicks.Web.Models;

public sealed class CityPicksClient(HttpClient http)
{
    public Task<CityView?> GetCityAsync(CancellationToken cancellationToken = default) =>
        http.GetFromJsonAsync<CityView>("api/city", cancellationToken);

    public async Task<IReadOnlyList<PlaceView>> GetPlacesAsync(CancellationToken cancellationToken = default) =>
        await http.GetFromJsonAsync<PlaceView[]>("api/places", cancellationToken) ?? [];

    public Task<PlaceView?> GetPlaceAsync(string id, CancellationToken cancellationToken = default) =>
        http.GetFromJsonAsync<PlaceView>($"api/places/{Uri.EscapeDataString(id)}", cancellationToken);
}
