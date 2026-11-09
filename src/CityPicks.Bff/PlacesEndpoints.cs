using System.Net.Http.Json;
using CityPicks.Web.Models;

namespace CityPicks.Bff;

public static class PlacesEndpoints
{
    public static void MapPlaces(this WebApplication app)
    {
        foreach (var route in new[] { "/api/city", "/api/places", "/api/places/{id}", "/api/place-images", "/images/{fileName}" })
            app.MapGet(route, (HttpContext context, IHttpClientFactory clients) => ForwardAsync(context, clients, false));
        app.MapGet("/api/identity", GetIdentityAsync).RequireAuthorization();
        app.MapPost("/api/places", (HttpContext context, IHttpClientFactory clients) => ForwardAsync(context, clients, true)).RequireAuthorization();
        app.MapPut("/api/places/{id}", (HttpContext context, IHttpClientFactory clients) => ForwardAsync(context, clients, true)).RequireAuthorization();
    }

    private static async Task<IResult> GetIdentityAsync(HttpContext context, IHttpClientFactory clients)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            using var response = await clients.CreateClient("UserPlaces").GetAsync("api/identity", context.RequestAborted);
            if (!response.IsSuccessStatusCode) return Results.StatusCode((int)response.StatusCode);
            var identity = await response.Content.ReadFromJsonAsync<IdentityView>(context.RequestAborted);
            if (identity is null) return Results.StatusCode(502);
            // Places owns management permission; favourites permission follows the BFF session used for enforcement.
            return Results.Ok(identity with { Permissions = new PermissionView(context.User.IsInRole("visitor"), identity.Permissions.CanManagePlaces) });
        }
        catch (HttpRequestException) { return Results.StatusCode(502); }
    }

    private static async Task ForwardAsync(HttpContext context, IHttpClientFactory clients, bool authenticated)
    {
        // Fixed destination and explicit routes. Never forward browser cookies or Authorization headers.
        using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method),
            context.Request.Path.Value!.TrimStart('/') + context.Request.QueryString);
        if (context.Request.Method is "POST" or "PUT")
        {
            request.Content = new StreamContent(context.Request.Body);
            if (context.Request.ContentType is { } contentType)
                request.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
        }
        try
        {
            using var response = await clients.CreateClient(authenticated ? "UserPlaces" : "PublicPlaces")
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
            context.Response.StatusCode = (int)response.StatusCode;
            context.Response.ContentType = response.Content.Headers.ContentType?.ToString();
            if (response.Headers.Location is { IsAbsoluteUri: false } location
                && location.OriginalString.StartsWith("/api/places/", StringComparison.Ordinal))
                context.Response.Headers.Location = location.OriginalString;
            if (authenticated) context.Response.Headers.CacheControl = "no-store";
            await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
        }
        catch (HttpRequestException)
        {
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
        }
    }
}
