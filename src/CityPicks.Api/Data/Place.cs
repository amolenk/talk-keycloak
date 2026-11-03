namespace CityPicks.Api.Data;

public sealed class Place
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string Category { get; set; }
    public required string Description { get; set; }
    public required string Image { get; set; }
    public required string Address { get; set; }
}

public sealed class CityEdition
{
    public required string Name { get; set; }
    public required string Introduction { get; set; }
}

public sealed class SeedMarker
{
    public int Id { get; set; }
    public required string City { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
}
