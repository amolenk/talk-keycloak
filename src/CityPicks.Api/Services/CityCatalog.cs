using System.Text.Json;
using CityPicks.Api.Data;
using StbImageSharp;

namespace CityPicks.Api.Services;

public sealed class CityCatalog
{
    private CityCatalog(CityEdition edition, IReadOnlyList<Place> places, string imageDirectory)
    {
        Edition = edition;
        Places = places;
        ImageDirectory = imageDirectory;
        ImageFiles = places.Select(place => place.Image).Distinct(StringComparer.Ordinal).ToArray();
    }

    public CityEdition Edition { get; }
    public IReadOnlyList<Place> Places { get; }
    public string ImageDirectory { get; }
    public IReadOnlyList<string> ImageFiles { get; }

    public static CityCatalog Load(string directory)
    {
        var fullDirectory = Path.GetFullPath(directory);
        var cityPath = Path.Combine(fullDirectory, "city.json");
        var placesPath = Path.Combine(fullDirectory, "places.json");

        if (!Directory.Exists(fullDirectory) || !File.Exists(cityPath) || !File.Exists(placesPath))
        {
            throw new InvalidDataException(
                $"City content directory '{fullDirectory}' must contain city.json and places.json.");
        }

        try
        {
            var edition = JsonSerializer.Deserialize<CityEdition>(File.ReadAllText(cityPath), JsonOptions)
                ?? throw new InvalidDataException("city.json must contain a city object.");
            var places = JsonSerializer.Deserialize<List<Place>>(File.ReadAllText(placesPath), JsonOptions)
                ?? throw new InvalidDataException("places.json must contain a place array.");
            ValidateEdition(edition);
            ValidatePlaces(places, fullDirectory);
            return new CityCatalog(edition, places, Path.Combine(fullDirectory, "images"));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"City content in '{fullDirectory}' is not valid JSON: {exception.Message}", exception);
        }
    }

    private static void ValidateEdition(CityEdition edition)
    {
        if (string.IsNullOrWhiteSpace(edition.Name) || string.IsNullOrWhiteSpace(edition.Introduction))
        {
            throw new InvalidDataException("city.json requires nonblank name and introduction fields.");
        }
        ValidateLength(edition.Name, ContentLimits.CityName, "city name");
    }

    private static void ValidatePlaces(IReadOnlyCollection<Place> places, string directory)
    {
        if (places.Count == 0)
        {
            throw new InvalidDataException("places.json must include at least one prepared place.");
        }

        if (places.Any(place => place is null))
        {
            throw new InvalidDataException("places.json must contain place objects, not null entries.");
        }

        var duplicateId = places.GroupBy(place => place.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateId is not null)
        {
            throw new InvalidDataException($"Place identifier '{duplicateId.Key}' appears more than once.");
        }

        foreach (var place in places)
        {
            if (string.IsNullOrWhiteSpace(place.Id)
                || string.IsNullOrWhiteSpace(place.Name)
                || string.IsNullOrWhiteSpace(place.Category)
                || string.IsNullOrWhiteSpace(place.Description)
                || string.IsNullOrWhiteSpace(place.Address))
            {
                throw new InvalidDataException("Each place requires a stable id and nonblank name, category, description, and address.");
            }

            ValidateLength(place.Id, ContentLimits.PlaceId, $"Place '{place.Id}' id");
            if (!place.Id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
                throw new InvalidDataException($"Place '{place.Id}' requires a URL-safe id using ASCII letters, digits, '-' or '_'.");
            ValidateLength(place.Name, ContentLimits.PlaceName, $"Place '{place.Id}' name");
            ValidateLength(place.Category, ContentLimits.Category, $"Place '{place.Id}' category");
            ValidateLength(place.Description, ContentLimits.Description, $"Place '{place.Id}' description");
            ValidateLength(place.Address, ContentLimits.Address, $"Place '{place.Id}' address");

            if (Path.GetFileName(place.Image) != place.Image ||
                !string.Equals(Path.GetExtension(place.Image), ".jpg", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Place '{place.Id}' must reference a local .jpg file name, not a path.");
            }

            ValidateLength(place.Image, ContentLimits.ImageFileName, $"Place '{place.Id}' image filename");

            var imagePath = Path.Combine(directory, "images", place.Image);
            if (!File.Exists(imagePath))
            {
                throw new InvalidDataException($"Place '{place.Id}' references missing image '{imagePath}'.");
            }

            using var image = File.OpenRead(imagePath);
            var isCompleteJpeg = image.Length >= 4
                && image.ReadByte() == 0xff
                && image.ReadByte() == 0xd8;
            if (isCompleteJpeg)
            {
                image.Seek(-2, SeekOrigin.End);
                isCompleteJpeg = image.ReadByte() == 0xff && image.ReadByte() == 0xd9;
            }

            if (!isCompleteJpeg)
            {
                throw new InvalidDataException($"Place '{place.Id}' image '{imagePath}' is not a complete JPEG file.");
            }

            try
            {
                image.Position = 0;
                ImageResult.FromStream(image);
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidDataException(
                    $"Place '{place.Id}' image '{imagePath}' is not a complete JPEG file: decoding failed.", exception);
            }
        }
    }

    private static void ValidateLength(string value, int maximum, string field)
    {
        if (value.Length > maximum)
            throw new InvalidDataException($"{field} must be at most {maximum} characters.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
}
