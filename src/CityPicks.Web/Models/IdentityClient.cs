using System.Net.Http.Json;

namespace CityPicks.Web.Models;

public sealed class IdentityClient(IHttpClientFactory clients)
{
    public async Task<IdentityView> GetOwnIdentityAsync() =>
        await clients.CreateClient("AuthenticatedApi").GetFromJsonAsync<IdentityView>("api/identity")
        ?? throw new HttpRequestException("The API returned no identity.");
}

public sealed record IdentityView(string? DisplayName, string? Username, string? Subject, string? Issuer,
    string? Email, string[] Roles, PermissionView Permissions);
public sealed record PermissionView(bool CanUseFavourites, bool CanManagePlaces);
