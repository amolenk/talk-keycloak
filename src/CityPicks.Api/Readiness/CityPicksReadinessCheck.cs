using System.Net.Http.Json;
using CityPicks.Api.Data;
using CityPicks.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CityPicks.Api.Readiness;

public sealed class CityPicksReadinessCheck(
    CityPicksDbContext db,
    CityCatalog city,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!await db.SeedMarkers.AnyAsync(cancellationToken))
        {
            return HealthCheckResult.Unhealthy("The application seed marker is missing.");
        }

        if (city.Places.Count == 0 || city.ImageFiles.Count == 0)
        {
            return HealthCheckResult.Unhealthy("Selected city content or images are unavailable.");
        }

        var issuer = configuration["CityPicks:RealmIssuer"];
        if (string.IsNullOrWhiteSpace(issuer))
        {
            return HealthCheckResult.Unhealthy("CityPicks:RealmIssuer is not configured.");
        }

        try
        {
            var discovery = await httpClientFactory.CreateClient("realm-discovery")
                .GetFromJsonAsync<DiscoveryDocument>(
                    $"{issuer.TrimEnd('/')}/.well-known/openid-configuration",
                    cancellationToken);
            return discovery?.Issuer == issuer
                ? HealthCheckResult.Healthy("Database, selected city assets, and Keycloak realm are ready.")
                : HealthCheckResult.Unhealthy($"Realm discovery issuer did not match '{issuer}'.");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return HealthCheckResult.Unhealthy("Keycloak realm discovery is not available.", exception);
        }
    }

    private sealed record DiscoveryDocument(string Issuer);
}
