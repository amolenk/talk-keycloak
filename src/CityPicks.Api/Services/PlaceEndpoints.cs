using CityPicks.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace CityPicks.Api.Services;

public static class PlaceEndpoints
{
    // Live authorization edit: replace "DenyManagement" with "CanManagePlaces".
    // Both mutations and the permission view use this exact policy.
    public const string ManagementPolicy = "DenyManagement";

    public static void MapPlaceManagement(this WebApplication app)
    {
        app.MapGet("/api/place-images", (CityCatalog city) => Results.Ok(city.ImageFiles));
        app.MapPost("/api/places", async (PlaceInput input, CityCatalog city, CityPicksDbContext db, CancellationToken ct) =>
        {
            var errors = Validate(input, city);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var place = new Place { Id = Guid.NewGuid().ToString("N"), Name = "", Category = "", Description = "", Image = "", Address = "" };
            Apply(input, place);
            db.Places.Add(place);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/places/{place.Id}", place);
        }).RequireAuthorization(ManagementPolicy);
        app.MapPut("/api/places/{id}", async (string id, PlaceInput input, CityCatalog city, CityPicksDbContext db, CancellationToken ct) =>
        {
            var errors = Validate(input, city);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var place = await db.Places.SingleOrDefaultAsync(place => place.Id == id, ct);
            if (place is null) return Results.NotFound();
            Apply(input, place);
            await db.SaveChangesAsync(ct);
            return Results.Ok(place);
        }).RequireAuthorization(ManagementPolicy);
    }

    private static Dictionary<string, string[]> Validate(PlaceInput input, CityCatalog city)
    {
        var errors = new Dictionary<string, string[]>();
        foreach (var (field, value, limit) in new[]
        {
            ("Name", input.Name, ContentLimits.PlaceName), ("Category", input.Category, ContentLimits.Category),
            ("Description", input.Description, ContentLimits.Description), ("Address", input.Address, ContentLimits.Address)
        })
        {
            if (string.IsNullOrWhiteSpace(value)) errors[field] = [$"{field} is required."];
            else if (value.Length > limit) errors[field] = [$"{field} must be at most {limit} characters."];
        }
        if (input.Image is null || !city.ImageFiles.Contains(input.Image, StringComparer.Ordinal))
            errors["Image"] = ["Choose an image from the prepared local catalogue."];
        return errors;
    }

    private static void Apply(PlaceInput input, Place place)
    {
        place.Name = input.Name!.Trim();
        place.Category = input.Category!.Trim();
        place.Description = input.Description!.Trim();
        place.Address = input.Address!.Trim();
        place.Image = input.Image!;
    }
}

public sealed record PlaceInput(string? Name, string? Category, string? Description, string? Image, string? Address);
