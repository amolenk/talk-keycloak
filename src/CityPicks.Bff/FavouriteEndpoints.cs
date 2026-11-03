using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using CityPicks.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CityPicks.Bff;

public sealed class FavouritesDbContext(DbContextOptions<FavouritesDbContext> options) : DbContext(options)
{
    public DbSet<Favourite> Favourites => Set<Favourite>();
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Favourite>()
        .HasKey(item => new { item.Issuer, item.Subject, item.PlaceId });
}

public sealed class Favourite
{
    public string Issuer { get; set; } = "";
    public string Subject { get; set; } = "";
    public string PlaceId { get; set; } = "";
}

public static class FavouriteEndpoints
{
    public static void MapFavourites(this WebApplication app)
    {
        var group = app.MapGroup("/api/favourites").RequireAuthorization("CanUseFavourites");
        group.AddEndpointFilter(async (context, next) =>
        {
            try { return await next(context); }
            catch (HttpRequestException) { return Results.Problem("The Places API is unavailable.", statusCode: 502); }
        });
        group.MapGet("", async (HttpContext context, FavouritesDbContext db, IHttpClientFactory clients, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var issuer = context.User.FindFirstValue("iss")!;
            var subject = context.User.FindFirstValue("sub")!;
            var ids = await db.Favourites.Where(f => f.Issuer == issuer && f.Subject == subject)
                .Select(f => f.PlaceId).ToArrayAsync(ct);
            var places = await clients.CreateClient("PublicPlaces").GetFromJsonAsync<PlaceView[]>("api/places", ct) ?? [];
            // Removed/unavailable places are omitted; the saved reference can still be deleted.
            return Results.Ok(places.Where(p => ids.Contains(p.Id)).OrderBy(p => p.Name));
        });
        group.MapPut("/{id}", async (string id, ClaimsPrincipal user, FavouritesDbContext db, IHttpClientFactory clients, CancellationToken ct) =>
        {
            using var place = await clients.CreateClient("PublicPlaces").GetAsync($"api/places/{Uri.EscapeDataString(id)}", ct);
            if (place.StatusCode == HttpStatusCode.NotFound) return Results.NotFound();
            place.EnsureSuccessStatusCode();
            var issuer = user.FindFirstValue("iss")!;
            var subject = user.FindFirstValue("sub")!;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Favourites" ("Issuer", "Subject", "PlaceId") VALUES ({issuer}, {subject}, {id})
                ON CONFLICT ("Issuer", "Subject", "PlaceId") DO NOTHING
                """, ct);
            return Results.NoContent();
        });
        group.MapDelete("/{id}", async (string id, ClaimsPrincipal user, FavouritesDbContext db, CancellationToken ct) =>
        {
            var issuer = user.FindFirstValue("iss")!;
            var subject = user.FindFirstValue("sub")!;
            await db.Favourites.Where(f => f.Issuer == issuer && f.Subject == subject && f.PlaceId == id).ExecuteDeleteAsync(ct);
            return Results.NoContent();
        });
    }
}
