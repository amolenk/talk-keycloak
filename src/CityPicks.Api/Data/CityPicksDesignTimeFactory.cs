using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CityPicks.Api.Data;

public sealed class CityPicksDesignTimeFactory : IDesignTimeDbContextFactory<CityPicksDbContext>
{
    public CityPicksDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__citypicks")
            ?? "Host=localhost;Database=citypicks;Username=citypicks;Password=demo";
        var cityDirectory = Environment.GetEnvironmentVariable("CityPicks__CityDirectory")
            ?? Path.GetFullPath("../../content/cities/prague");
        var optionsBuilder = new DbContextOptionsBuilder<CityPicksDbContext>();
        CityPicksDbContext.Configure(optionsBuilder, connectionString, cityDirectory);
        var options = optionsBuilder.Options;
        return new CityPicksDbContext(options);
    }
}
