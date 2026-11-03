using CityPicks.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace CityPicks.Api.Data;

public sealed class CityPicksDbContext(DbContextOptions<CityPicksDbContext> options) : DbContext(options)
{
    public DbSet<Place> Places => Set<Place>();
    public DbSet<Favourite> Favourites => Set<Favourite>();
    public DbSet<SeedMarker> SeedMarkers => Set<SeedMarker>();

    public static void Configure(
        DbContextOptionsBuilder options,
        string connectionString,
        string cityDirectory)
    {
        options.UseNpgsql(connectionString)
            .UseSeeding((context, _) => DatabaseInitializer.Seed(
                (CityPicksDbContext)context,
                CityCatalog.Load(cityDirectory)))
            .UseAsyncSeeding((context, _, cancellationToken) => DatabaseInitializer.SeedAsync(
                (CityPicksDbContext)context,
                CityCatalog.Load(cityDirectory),
                cancellationToken));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Place>(entity =>
        {
            entity.HasKey(place => place.Id);
            entity.Property(place => place.Id).HasMaxLength(ContentLimits.PlaceId);
            entity.Property(place => place.Name).HasMaxLength(ContentLimits.PlaceName).IsRequired();
            entity.Property(place => place.Category).HasMaxLength(ContentLimits.Category).IsRequired();
            entity.Property(place => place.Description).HasMaxLength(ContentLimits.Description).IsRequired();
            entity.Property(place => place.Image).HasMaxLength(ContentLimits.ImageFileName).IsRequired();
            entity.Property(place => place.Address).HasMaxLength(ContentLimits.Address).IsRequired();
        });

        modelBuilder.Entity<Favourite>(entity =>
        {
            entity.HasKey(favourite => new { favourite.Issuer, favourite.Subject, favourite.PlaceId });
            entity.Property(favourite => favourite.PlaceId).HasMaxLength(ContentLimits.PlaceId);
            entity.HasOne(favourite => favourite.Place).WithMany().HasForeignKey(favourite => favourite.PlaceId);
        });

        modelBuilder.Entity<SeedMarker>(entity =>
        {
            entity.HasKey(marker => marker.Id);
            entity.Property(marker => marker.City).HasMaxLength(ContentLimits.CityName).IsRequired();
        });
    }
}
