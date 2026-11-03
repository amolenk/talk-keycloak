using CityPicks.Api.Services;
using Xunit;

namespace CityPicks.IntegrationTests;

public sealed class CityCatalogValidationTests
{
    [Theory]
    [InlineData("prague", "Prague", "charles-bridge,lennon-wall,old-town-square,prague-castle")]
    [InlineData("stockholm", "Stockholm", "city-hall,dramaten,skeppsbron,stortorget")]
    public void ShippedEditionsHaveStableRecommendationsAndLocalPhotographs(string directory, string name, string ids)
    {
        var catalog = CityCatalog.Load(Path.GetFullPath($"../../../../../content/cities/{directory}", AppContext.BaseDirectory));
        Assert.Equal(name, catalog.Edition.Name);
        Assert.Equal(ids.Split(','), catalog.Places.Select(place => place.Id).Order().ToArray());
        Assert.Equal(4, catalog.ImageFiles.Count);
    }

    [Theory]
    [InlineData("malformed", "not valid JSON")]
    [InlineData("blank-name", "nonblank")]
    [InlineData("duplicate-id", "appears more than once")]
    [InlineData("missing-image", "missing image")]
    [InlineData("null-image", "local .jpg")]
    [InlineData("long-description", "at most 1000")]
    [InlineData("slash-id", "URL-safe")]
    [InlineData("query-id", "URL-safe")]
    [InlineData("fragment-id", "URL-safe")]
    public void InvalidPreparedContentProducesAnActionableError(string problem, string expectedMessage)
    {
        var directory = Directory.CreateTempSubdirectory("city-picks-content-");
        try
        {
            var source = Path.GetFullPath("../../../../../content/cities/prague", AppContext.BaseDirectory);
            File.Copy(Path.Combine(source, "city.json"), Path.Combine(directory.FullName, "city.json"));
            Directory.CreateDirectory(Path.Combine(directory.FullName, "images"));
            foreach (var image in Directory.GetFiles(Path.Combine(source, "images")))
                File.Copy(image, Path.Combine(directory.FullName, "images", Path.GetFileName(image)));

            var places = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(source, "places.json")))!;
            switch (problem)
            {
                case "blank-name": places[0]!["name"] = " "; break;
                case "duplicate-id": places[1]!["id"] = "charles-bridge"; break;
                case "missing-image": places[0]!["image"] = "unprepared.jpg"; break;
                case "null-image": places[0]!["image"] = null; break;
                case "long-description": places[0]!["description"] = new string('x', 1001); break;
                case "slash-id": places[0]!["id"] = "bridge/details"; break;
                case "query-id": places[0]!["id"] = "bridge?details"; break;
                case "fragment-id": places[0]!["id"] = "bridge#details"; break;
            }
            File.WriteAllText(Path.Combine(directory.FullName, "places.json"),
                problem == "malformed" ? "{" : places.ToJsonString());

            var error = Assert.Throws<InvalidDataException>(() => CityCatalog.Load(directory.FullName));

            Assert.Contains(expectedMessage, error.Message, StringComparison.Ordinal);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void NullPlaceProducesAnActionableContentError()
    {
        var directory = Directory.CreateTempSubdirectory("city-picks-null-");
        try
        {
            File.WriteAllText(Path.Combine(directory.FullName, "city.json"),
                "{\"name\":\"Prague\",\"introduction\":\"A city introduction.\"}");
            File.WriteAllText(Path.Combine(directory.FullName, "places.json"), "[null]");

            var error = Assert.Throws<InvalidDataException>(() => CityCatalog.Load(directory.FullName));

            Assert.Contains("places.json", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void MissingSelectedCityDirectoryProducesAnActionableError()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"city-picks-missing-{Guid.NewGuid():N}");

        var error = Assert.Throws<InvalidDataException>(() => CityCatalog.Load(directory));

        Assert.Contains(directory, error.Message, StringComparison.Ordinal);
        Assert.Contains("city.json and places.json", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CorruptReferencedImageIsRejectedBeforeTheApiStarts(bool hasJpegMarkers)
    {
        var directory = Directory.CreateTempSubdirectory("city-picks-invalid-");
        try
        {
            Directory.CreateDirectory(Path.Combine(directory.FullName, "images"));
            File.WriteAllText(Path.Combine(directory.FullName, "city.json"),
                "{\"name\":\"Prague\",\"introduction\":\"A city introduction.\"}");
            File.WriteAllText(Path.Combine(directory.FullName, "places.json"), """
                [{
                  "id":"bridge",
                  "name":"Bridge",
                  "category":"Landmark",
                  "description":"A short description.",
                  "image":"broken.jpg",
                  "address":"Prague"
                }]
                """);
            File.WriteAllBytes(Path.Combine(directory.FullName, "images", "broken.jpg"),
                hasJpegMarkers ? [0xff, 0xd8, 0xff, 0xd9] : [0x01, 0x02, 0x03, 0x04]);

            var error = Assert.Throws<InvalidDataException>(() => CityCatalog.Load(directory.FullName));

            Assert.Contains("not a complete JPEG", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
