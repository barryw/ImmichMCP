namespace ImmichMCP.Configuration;

/// <summary>
/// Configuration options for connecting to the Immich API.
/// </summary>
public class ImmichOptions
{
    /// <summary>
    /// Base URL of the Immich instance (e.g., https://photos.example.com).
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// API key for authentication.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Maximum page size for paginated requests.
    /// </summary>
    public int MaxPageSize { get; set; } = 100;

    /// <summary>
    /// Download mode: "url" returns URLs, "base64" returns encoded content.
    /// </summary>
    public string DownloadMode { get; set; } = "url";

    /// <summary>
    /// Maximum asset size (in bytes) returned inline when DownloadMode is "base64".
    /// </summary>
    public long MaxInlineDownloadBytes { get; set; } = 25 * 1024 * 1024;
}
