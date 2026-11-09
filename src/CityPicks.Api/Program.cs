using CityPicks.Api.Data;
using CityPicks.Api.Readiness;
using CityPicks.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

if (args.Contains("--initialize", StringComparer.Ordinal))
{
    await DatabaseInitializer.RunAsync();
    return;
}

var builder = WebApplication.CreateBuilder(args);
// OIDC callback query strings contain authorization codes; exclude request URLs
// from diagnostic output while retaining warnings and application/authentication logs.
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
var cityDirectory = builder.Configuration["CityPicks:CityDirectory"]
    ?? Path.GetFullPath("../../content/cities/prague");
var realmIssuer = builder.Configuration["CityPicks:RealmIssuer"];
var catalog = CityCatalog.Load(cityDirectory);
if (args.Contains("--validate-content", StringComparer.Ordinal))
{
    Console.WriteLine($"Validated {catalog.Places.Count} {catalog.Edition.Name} places and local photographs.");
    return;
}

builder.Services.AddSingleton(catalog);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.Authority = realmIssuer;
    options.Audience = "places-api";
    options.MapInboundClaims = false;
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = context =>
        {
            if (string.IsNullOrWhiteSpace(context.Principal?.FindFirstValue("sub")))
                context.Fail("The access token has no subject.");
            return Task.CompletedTask;
        }
    };
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = realmIssuer,
        ValidAudience = "places-api",
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateIssuerSigningKey = true,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero,
        NameClaimType = "preferred_username",
        RoleClaimType = "roles"
    };
});
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("DenyManagement", policy => policy.RequireAuthenticatedUser().RequireAssertion(_ => false));
    options.AddPolicy("CanManagePlaces", policy => policy.RequireAuthenticatedUser().RequireRole("curator"));
});
builder.Services.AddDbContext<CityPicksDbContext>(options =>
    CityPicksDbContext.Configure(
        options,
        builder.Configuration.GetConnectionString("citypicks")!,
        cityDirectory));
builder.Services.AddHttpClient("realm-discovery", client =>
    client.Timeout = TimeSpan.FromSeconds(3));
builder.Services.AddHealthChecks()
    .AddCheck<CityPicksReadinessCheck>("city-picks-ready");

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapGet("/api/identity", async (ClaimsPrincipal user, IAuthorizationService authorization) => Results.Ok(new
{
    displayName = user.FindFirstValue("name"),
    username = user.FindFirstValue("preferred_username"),
    subject = user.FindFirstValue("sub"),
    issuer = user.FindFirstValue("iss"),
    email = user.FindFirstValue("email"),
    roles = user.FindAll("roles").Select(claim => claim.Value).Order().ToArray(),
    permissions = new { canManagePlaces = (await authorization.AuthorizeAsync(user, PlaceEndpoints.ManagementPolicy)).Succeeded }
})).RequireAuthorization();
app.MapPlaceManagement();
app.MapGet("/api/city", (CityCatalog city) => Results.Ok(city.Edition));
app.MapGet("/api/places", async (CityPicksDbContext db, CancellationToken cancellationToken) =>
    Results.Ok(await db.Places.OrderBy(place => place.Name).ToArrayAsync(cancellationToken)));
app.MapGet("/api/places/{id}", async (string id, CityPicksDbContext db, CancellationToken cancellationToken) =>
{
    var place = await db.Places.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    return place is null ? Results.NotFound() : Results.Ok(place);
});
app.MapGet("/images/{fileName}", (string fileName, CityCatalog city) =>
{
    if (!city.ImageFiles.Contains(fileName, StringComparer.Ordinal))
    {
        return Results.NotFound();
    }

    var imagePath = Path.Combine(city.ImageDirectory, fileName);
    return Results.File(imagePath, contentType: "image/jpeg", enableRangeProcessing: true);
});

await app.RunAsync();

public partial class Program;
