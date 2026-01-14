using Microsoft.Extensions.Options;
using ImmichMCP.Configuration;

namespace ImmichMCP.Client;

/// <summary>
/// HTTP message handler that adds the Immich API key to requests.
/// </summary>
public class ImmichAuthHandler : DelegatingHandler
{
    private readonly ImmichOptions _options;

    public ImmichAuthHandler(IOptions<ImmichOptions> options)
    {
        _options = options.Value;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_options.ApiKey))
        {
            request.Headers.Add("x-api-key", _options.ApiKey);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
