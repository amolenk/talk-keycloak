using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace CityPicks.Web.Authentication;

public sealed class BffAuthenticationStateProvider(HttpClient client) : AuthenticationStateProvider
{
    private Task<AuthenticationState>? _state;
    public string? CsrfToken { get; private set; }
    public string? DiscoveryError { get; private set; }
    public override Task<AuthenticationState> GetAuthenticationStateAsync() => _state ??= LoadAsync();

    private async Task<AuthenticationState> LoadAsync()
    {
        try
        {
            var session = await client.GetFromJsonAsync<BffSession>("auth/user")
                ?? throw new HttpRequestException("The BFF returned no session information.");
            DiscoveryError = null;
            CsrfToken = session.CsrfToken;
            return new AuthenticationState(new ClaimsPrincipal(session.Authenticated
                ? new ClaimsIdentity(session.Claims.Select(c => new Claim(c.Type, c.Value)), "BFF", "preferred_username", "roles")
                : new ClaimsIdentity()));
        }
        catch (HttpRequestException)
        {
            DiscoveryError = "Your session could not be checked. Please retry.";
            CsrfToken = null;
            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        }
    }

    public Task RetryAsync()
    {
        _state = LoadAsync();
        NotifyAuthenticationStateChanged(_state);
        return _state;
    }

    public void SessionEnded()
    {
        CsrfToken = null;
        DiscoveryError = null;
        _state = Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
        NotifyAuthenticationStateChanged(_state);
    }
}

public sealed record BffSession(bool Authenticated, BffClaim[] Claims, string CsrfToken);
public sealed record BffClaim(string Type, string Value);
