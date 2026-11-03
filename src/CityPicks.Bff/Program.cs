using System.Security.Claims;
using CityPicks.Bff;
using Duende.AccessTokenManagement.OpenIdConnect;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();
// Authorization codes and tokens must not appear in request/client diagnostics.
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
var issuer = builder.Configuration["CityPicks:RealmIssuer"]
    ?? throw new InvalidOperationException("CityPicks:RealmIssuer is required; the AppHost provides it.");
builder.Services.AddDataProtection().SetApplicationName("CityPicks.Bff");
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = "cookie";
    options.DefaultChallengeScheme = "oidc"; // Duende uses this scheme to discover the token endpoint/client.
})
.AddCookie("cookie", options =>
{
    options.Cookie.Name = "__Host-city-picks";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    options.SlidingExpiration = false;
    options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
    options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
    options.Events.OnSigningOut = async context => await context.HttpContext.RevokeRefreshTokenAsync();
})
.AddOpenIdConnect("oidc", options =>
{
    options.Authority = issuer;
    options.ClientId = "city-picks-bff";
    options.ClientSecret = builder.Configuration["CityPicks:ClientSecret"]
        ?? throw new InvalidOperationException("CityPicks:ClientSecret is required.");
    options.SignInScheme = "cookie";
    options.ResponseType = "code";
    options.ResponseMode = "query";
    options.UsePkce = true;
    options.PushedAuthorizationBehavior = PushedAuthorizationBehavior.Disable;
    options.MapInboundClaims = false;
    options.ClaimActions.Remove("iss"); // Keep the validated issuer for stable (issuer, subject) ownership.
    options.Scope.Add("profile");
    options.Scope.Add("email");
    // Keycloak issues refresh tokens for the normal online session; no offline_access needed.
    options.SaveTokens = true; // Demo: Data Protection encrypts the ticket, including tokens, in the cookie.
    options.TokenValidationParameters.NameClaimType = "preferred_username";
    options.TokenValidationParameters.RoleClaimType = "roles";
    options.Events.OnTokenValidated = context =>
    {
        if (string.IsNullOrWhiteSpace(context.Principal?.FindFirstValue("sub"))) context.Fail("Missing subject.");
        return Task.CompletedTask;
    };
});
builder.Services.AddAuthorization(options =>
{
    // Cookie challenges on browser API endpoints produce 401, never an OIDC redirect in fetch.
    options.DefaultPolicy = new AuthorizationPolicyBuilder("cookie").RequireAuthenticatedUser().Build();
    options.AddPolicy("CanUseFavourites", policy => policy.AddAuthenticationSchemes("cookie")
        .RequireAuthenticatedUser().RequireRole("traveler"));
});
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "__Host-city-picks-csrf";
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
});
builder.Services.AddOpenIdConnectAccessTokenManagement();
var placesAddress = new Uri(builder.Configuration["CityPicks:PlacesApi"]
    ?? throw new InvalidOperationException("CityPicks:PlacesApi is required."));
builder.Services.AddHttpClient("PublicPlaces", client => client.BaseAddress = placesAddress)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false });
builder.Services.AddUserAccessTokenHttpClient("UserPlaces", configureClient: client => client.BaseAddress = placesAddress)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false });
builder.Services.AddDbContext<FavouritesDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("favourites")));
builder.Services.AddHealthChecks();

var app = builder.Build();
await using (var scope = app.Services.CreateAsyncScope())
    await scope.ServiceProvider.GetRequiredService<FavouritesDbContext>().Database.EnsureCreatedAsync();
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
// Validate all unsafe browser requests before executing local endpoints or forwarding to Places.
app.Use(async (context, next) =>
{
    if (context.Request.Method is not ("GET" or "HEAD" or "OPTIONS"))
    {
        try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException) { context.Response.StatusCode = 400; return; }
    }
    await next(context);
});
app.MapHealthChecks("/health");
app.MapGet("/auth/login", (string? returnUrl) => Results.Challenge(
    new AuthenticationProperties { RedirectUri = LocalReturnUrl(returnUrl) }, ["oidc"]));
app.MapPost("/auth/logout", () => Results.SignOut(
    new AuthenticationProperties { RedirectUri = "/" }, ["cookie", "oidc"])).RequireAuthorization();
app.MapGet("/auth/user", (HttpContext context, IAntiforgery antiforgery) =>
{
    context.Response.Headers.CacheControl = "no-store";
    var csrf = antiforgery.GetAndStoreTokens(context).RequestToken;
    var authenticated = context.User.Identity?.IsAuthenticated == true;
    // An explicit allowlist exposes UI identity, never the authentication ticket or its tokens.
    var types = new[] { "sub", "iss", "name", "preferred_username", "email", "roles" };
    return Results.Ok(new
    {
        authenticated,
        claims = authenticated ? context.User.Claims.Where(c => types.Contains(c.Type))
            .Select(c => new { type = c.Type, value = c.Value }).ToArray() : [],
        csrfToken = csrf
    });
});
app.MapPlaces();
app.MapFavourites();
// Unknown API/auth routes must not return the SPA HTML with a misleading 200.
app.Map("/api/{**path}", () => Results.NotFound());
app.Map("/auth/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html");
await app.RunAsync();

static string LocalReturnUrl(string? value) =>
    !string.IsNullOrEmpty(value) && value.StartsWith('/') && !value.StartsWith("//")
    && !value.Contains('\\') && !value.Any(char.IsControl) ? value : "/";

public partial class Program;
