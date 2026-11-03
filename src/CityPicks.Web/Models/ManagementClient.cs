using System.Net.Http.Json;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace CityPicks.Web.Models;

public sealed class ManagementClient(IHttpClientFactory clients, HttpClient publicClient)
{
    public async Task<string[]> GetImagesAsync() => await publicClient.GetFromJsonAsync<string[]>("api/place-images") ?? [];

    public Task<HttpResponseMessage> SaveAsync(string? id, PlaceEdit input) => id is null
        ? clients.CreateClient("AuthenticatedApi").PostAsJsonAsync("api/places", input)
        : clients.CreateClient("AuthenticatedApi").PutAsJsonAsync($"api/places/{Uri.EscapeDataString(id)}", input);

    public async Task<string> ProbeAsync(bool sendSession)
    {
        var places = await publicClient.GetFromJsonAsync<PlaceView[]>("api/places") ?? [];
        var place = places.FirstOrDefault() ?? throw new HttpRequestException("No existing place is available.");
        using var request = new HttpRequestMessage(HttpMethod.Put, $"api/places/{Uri.EscapeDataString(place.Id)}")
            { Content = JsonContent.Create(PlaceEdit.From(place)) };
        if (!sendSession) request.SetBrowserRequestCredentials(BrowserRequestCredentials.Omit);
        using var response = await (sendSession ? clients.CreateClient("AuthenticatedApi") : publicClient).SendAsync(request);
        return $"PUT /api/places/{place.Id} · Session {(sendSession ? "sent to BFF" : "omitted")} · HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
    }
}

public sealed class PlaceEdit
{
    [Required, StringLength(160)] public string Name { get; set; } = "";
    [Required, StringLength(80)] public string Category { get; set; } = "";
    [Required, StringLength(1000)] public string Description { get; set; } = "";
    [Required, StringLength(160)] public string Image { get; set; } = "";
    [Required, StringLength(240)] public string Address { get; set; } = "";

    public static PlaceEdit From(PlaceView place) => new()
    {
        Name = place.Name, Category = place.Category, Description = place.Description,
        Image = place.Image, Address = place.Address
    };
}
