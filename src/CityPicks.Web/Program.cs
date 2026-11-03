using CityPicks.Web;
using CityPicks.Web.Authentication;
using CityPicks.Web.Models;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components.Authorization;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
builder.Services.AddSingleton(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<CityPicksClient>();
builder.Services.AddAuthorizationCore();
// One instance per WASM application, also shared with IHttpClientFactory's handler scope.
builder.Services.AddSingleton<BffAuthenticationStateProvider>();
builder.Services.AddSingleton<AuthenticationStateProvider>(services => services.GetRequiredService<BffAuthenticationStateProvider>());
builder.Services.AddTransient<BffApiHandler>();
builder.Services.AddHttpClient("AuthenticatedApi", client => client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress))
    .AddHttpMessageHandler<BffApiHandler>();
builder.Services.AddScoped<IdentityClient>();
builder.Services.AddScoped<FavouritesClient>();
builder.Services.AddScoped<ManagementClient>();
await builder.Build().RunAsync();
