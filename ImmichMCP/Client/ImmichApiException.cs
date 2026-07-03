using System.Net;

namespace ImmichMCP.Client;

/// <summary>
/// Thrown when the Immich API returns a non-success status (other than a legitimate
/// 404 not-found on a get-by-id). Carries enough context for the MCP boundary to
/// surface a spec-compliant tool execution error (a result with isError: true) instead
/// of silently swallowing the failure into an empty or default value.
/// </summary>
public sealed class ImmichApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public string Method { get; }
    public string Path { get; }
    public string? ResponseBody { get; }

    public ImmichApiException(HttpStatusCode statusCode, string method, string path, string? responseBody)
        : base($"Immich API {method} {path} failed with HTTP {(int)statusCode}: {Summarize(responseBody)}")
    {
        StatusCode = statusCode;
        Method = method;
        Path = path;
        ResponseBody = responseBody;
    }

    /// <summary>
    /// Builds an exception from a failed response, capturing its body for diagnostics.
    /// </summary>
    public static async Task<ImmichApiException> FromResponseAsync(
        HttpResponseMessage response, string method, string path, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return new ImmichApiException(response.StatusCode, method, path, body);
    }

    private static string Summarize(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "(no response body)";

        return body.Length > 500 ? body[..500] + "…" : body;
    }
}
