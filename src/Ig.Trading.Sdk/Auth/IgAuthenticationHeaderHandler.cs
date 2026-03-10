using Ig.Trading.Sdk.Configuration;
using Microsoft.Extensions.Options;

namespace Ig.Trading.Sdk.Auth;

public sealed class IgAuthenticationHeaderHandler : DelegatingHandler
{
    private readonly IgClientOptions _options;
    private readonly IIgSessionStore _sessionStore;

    public IgAuthenticationHeaderHandler(IOptions<IgClientOptions> options, IIgSessionStore sessionStore)
    {
        _options = options.Value;
        _sessionStore = sessionStore;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Remove("X-IG-API-KEY");
        request.Headers.TryAddWithoutValidation("X-IG-API-KEY", _options.ApiKey);

        var session = _sessionStore.Current;
        if (!string.IsNullOrWhiteSpace(session.Cst))
        {
            request.Headers.Remove("CST");
            request.Headers.TryAddWithoutValidation("CST", session.Cst);
        }

        if (!string.IsNullOrWhiteSpace(session.SecurityToken))
        {
            request.Headers.Remove("X-SECURITY-TOKEN");
            request.Headers.TryAddWithoutValidation("X-SECURITY-TOKEN", session.SecurityToken);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
