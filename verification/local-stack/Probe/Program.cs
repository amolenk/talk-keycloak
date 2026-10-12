using Microsoft.EntityFrameworkCore;
// Minimal API and one-shot initializer share a binary solely for this probe.
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.AspNetCore.Authentication.JwtBearer;
if (args.Contains("--initialize")) {
    Console.WriteLine($"initializer-start:{DateTimeOffset.UtcNow:O}");
    await Task.Delay(int.Parse(Environment.GetEnvironmentVariable("INIT_DELAY_MS") ?? "0"));
    if (Environment.GetEnvironmentVariable("INIT_FAIL") == "1") { Console.Error.WriteLine("intentional initializer failure"); Environment.ExitCode = 23; return; }
    var options = new DbContextOptionsBuilder<ProbeDb>(); DbOptions.Configure(options);
    await using var db = new ProbeDb(options.Options);
    await db.Database.MigrateAsync();
    Console.WriteLine($"initializer-complete:{DateTimeOffset.UtcNow:O};markers:{await db.SeedMarkers.CountAsync()}");
    return;
}
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<ProbeDb>(o => DbOptions.Configure(o));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o => {
    o.Authority = "https://localhost:8443/realms/city-picks";
    o.Audience = "city-picks-api";
    o.MapInboundClaims = false;
    o.TokenValidationParameters.RoleClaimType = "roles";
    o.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(5);
});
builder.Services.AddAuthorization(o => {
    o.AddPolicy("traveler", p => p.RequireRole("traveler"));
    o.AddPolicy("curator", p => p.RequireRole("curator"));
});
var app = builder.Build();
app.UseAuthentication(); app.UseAuthorization();
app.MapGet("/health", async (ProbeDb db, Microsoft.Extensions.Options.IOptionsMonitor<JwtBearerOptions> options) => {
    var c = await options.Get(JwtBearerDefaults.AuthenticationScheme).ConfigurationManager!.GetConfigurationAsync(default);
    return await db.SeedMarkers.CountAsync() == 1 && c.Issuer == "https://localhost:8443/realms/city-picks" ? Results.Ok(new { ready = true, clock = DateTimeOffset.UtcNow }) : Results.StatusCode(503);
});
app.MapGet("/diagnostics", async (Microsoft.Extensions.Options.IOptionsMonitor<JwtBearerOptions> options) => {
    var o = options.Get(JwtBearerDefaults.AuthenticationScheme);
    var c = await o.ConfigurationManager!.GetConfigurationAsync(default);
    return new { o.Authority, o.MetadataAddress, c.Issuer, keys = c.SigningKeys.Count };
});
app.MapGet("/api/identity", (System.Security.Claims.ClaimsPrincipal u) => new { issuer = u.FindFirst("iss")?.Value, subject = u.FindFirst("sub")?.Value, roles = u.FindAll("roles").Select(c => c.Value) }).RequireAuthorization();
app.MapGet("/api/traveler", () => Results.Ok()).RequireAuthorization("traveler");
app.MapGet("/api/curator", () => Results.Ok()).RequireAuthorization("curator");
app.MapGet("/api/state", async (ProbeDb db) => await db.Samples.OrderBy(x => x.Id).ToArrayAsync());
app.MapPost("/api/state/edit", async (ProbeDb db) => { var s = await db.Samples.SingleAsync(x => x.Id == 1); s.Value = "retained-curator-edit"; await db.SaveChangesAsync(); return Results.Ok(); });
app.Run();
public class Sample { public int Id { get; set; } public string Value { get; set; } = ""; }
public class SeedMarker { public int Id { get; set; } }
public class ProbeDb(DbContextOptions<ProbeDb> options) : DbContext(options) {
    public DbSet<Sample> Samples => Set<Sample>(); public DbSet<SeedMarker> SeedMarkers => Set<SeedMarker>();
}
public static class DbOptions {
    public static void Configure(DbContextOptionsBuilder o) => o.UseNpgsql(Environment.GetEnvironmentVariable("PROBE_DB") ?? "Host=localhost;Database=citypicks;Username=citypicks;Password=demo")
        .UseSeeding((context, _) => { var db = (ProbeDb)context; if (db.SeedMarkers.Any()) return; using var t = db.Database.BeginTransaction(); db.Samples.Add(new Sample { Id = 1, Value = "prepared-place" }); db.SeedMarkers.Add(new SeedMarker { Id = 1 }); db.SaveChanges(); t.Commit(); })
        .UseAsyncSeeding(async (context, _, ct) => { var db = (ProbeDb)context; if (await db.SeedMarkers.AnyAsync(ct)) return; await using var t = await db.Database.BeginTransactionAsync(ct); db.Samples.Add(new Sample { Id = 1, Value = "prepared-place" }); db.SeedMarkers.Add(new SeedMarker { Id = 1 }); await db.SaveChangesAsync(ct); await t.CommitAsync(ct); });
}
public class Factory : IDesignTimeDbContextFactory<ProbeDb> {
    public ProbeDb CreateDbContext(string[] args) { var o = new DbContextOptionsBuilder<ProbeDb>(); DbOptions.Configure(o); return new(o.Options); }
}
