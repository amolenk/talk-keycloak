namespace CityPicks.Web.Models;

public sealed record PlaceView(
    string Id,
    string Name,
    string Category,
    string Description,
    string Image,
    string Address);

public sealed record CityView(string Name, string Introduction);
