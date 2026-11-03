using System.Net;

namespace CityPicks.Web.Authentication;

public sealed class BffApiHandler(BffAuthenticationStateProvider authentication) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        await authentication.GetAuthenticationStateAsync();
        if (authentication.DiscoveryError is not null) throw new HttpRequestException(authentication.DiscoveryError);
        if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head)
            request.Headers.Add("X-CSRF-TOKEN", authentication.CsrfToken);
        var response = await base.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            authentication.SessionEnded();
            throw new BffSessionEndedException();
        }
        return response;
    }
}

public sealed class BffSessionEndedException() : HttpRequestException("Your BFF session has ended.", null, HttpStatusCode.Unauthorized);
