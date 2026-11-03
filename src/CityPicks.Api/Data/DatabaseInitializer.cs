using CityPicks.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace CityPicks.Api.Data;

public static class DatabaseInitializer
{
    public static async Task RunAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__citypicks")
            ?? throw new InvalidOperationException("ConnectionStrings__citypicks is required for initialization.");
        var cityDirectory = Environment.GetEnvironmentVariable("CityPicks__CityDirectory")
            ?? throw new InvalidOperationException("CityPicks__CityDirectory is required for initialization.");
        var catalog = CityCatalog.Load(cityDirectory);

        var optionsBuilder = new DbContextOptionsBuilder<CityPicksDbContext>();
        CityPicksDbContext.Configure(optionsBuilder, connectionString, cityDirectory);
        var options = optionsBuilder.Options;
        await using var db = new CityPicksDbContext(options);
        await db.Database.MigrateAsync();
        Console.WriteLine($"City Picks initialized with {catalog.Places.Count} {catalog.Edition.Name} places.");
    }

    public static void Seed(CityPicksDbContext db, CityCatalog catalog)
    {
        if (db.SeedMarkers.Any())
        {
            ValidateExistingEdition(db.SeedMarkers.Single(), catalog);
            return;
        }

        using var transaction = db.Database.BeginTransaction();
        db.Places.AddRange(catalog.Places.Select(Copy));
        db.SeedMarkers.Add(new SeedMarker
        {
            Id = 1,
            City = catalog.Edition.Name,
            CompletedAt = DateTimeOffset.UtcNow
        });
        db.SaveChanges();
        transaction.Commit();
    }

    public static async Task SeedAsync(
        CityPicksDbContext db,
        CityCatalog catalog,
        CancellationToken cancellationToken)
    {
        var marker = await db.SeedMarkers.SingleOrDefaultAsync(cancellationToken);
        if (marker is not null)
        {
            ValidateExistingEdition(marker, catalog);
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.Places.AddRange(catalog.Places.Select(Copy));
        db.SeedMarkers.Add(new SeedMarker
        {
            Id = 1,
            City = catalog.Edition.Name,
            CompletedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static Place Copy(Place place) => new()
    {
        Id = place.Id,
        Name = place.Name,
        Category = place.Category,
        Description = place.Description,
        Image = place.Image,
        Address = place.Address
    };

    private static void ValidateExistingEdition(SeedMarker marker, CityCatalog catalog)
    {
        if (!string.Equals(marker.City, catalog.Edition.Name, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Application database contains city '{marker.City}' but selected content is '{catalog.Edition.Name}'. " +
                "Between conferences, wipe both application and Keycloak state before changing city editions; " +
                "browser sessions and credential-provider cleanup are separate boundaries.");
        }
    }
}
