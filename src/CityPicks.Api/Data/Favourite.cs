namespace CityPicks.Api.Data;

public sealed class Favourite
{
    public required string Issuer { get; set; }
    public required string Subject { get; set; }
    public required string PlaceId { get; set; }
    public Place Place { get; set; } = null!;
}
