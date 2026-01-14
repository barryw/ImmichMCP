using System.Text.Json.Serialization;

namespace ImmichMCP.Models.Common;

/// <summary>
/// Standard response envelope for all MCP tool results.
/// </summary>
/// <typeparam name="T">The type of the result payload.</typeparam>
public record McpResponse<T>
{
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    [JsonPropertyName("result")]
    public T? Result { get; init; }

    [JsonPropertyName("meta")]
    public McpMeta Meta { get; init; } = new();

    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; init; } = [];

    public static McpResponse<T> Success(T result, McpMeta? meta = null, List<string>? warnings = null) => new()
    {
        Ok = true,
        Result = result,
        Meta = meta ?? new McpMeta(),
        Warnings = warnings ?? []
    };

    public static McpResponse<T> Failure(McpError error, McpMeta? meta = null) => new()
    {
        Ok = false,
        Result = default,
        Meta = meta ?? new McpMeta(),
        Warnings = []
    };
}

/// <summary>
/// Error response wrapper.
/// </summary>
public record McpErrorResponse
{
    [JsonPropertyName("ok")]
    public bool Ok => false;

    [JsonPropertyName("error")]
    public required McpError Error { get; init; }

    [JsonPropertyName("meta")]
    public McpMeta Meta { get; init; } = new();

    public static McpErrorResponse Create(string code, string message, object? details = null, McpMeta? meta = null) => new()
    {
        Error = new McpError
        {
            Code = code,
            Message = message,
            Details = details
        },
        Meta = meta ?? new McpMeta()
    };
}

/// <summary>
/// Error information.
/// </summary>
public record McpError
{
    [JsonPropertyName("code")]
    public required string Code { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("details")]
    public object? Details { get; init; }
}

/// <summary>
/// Metadata included with all responses.
/// </summary>
public record McpMeta
{
    [JsonPropertyName("request_id")]
    public string RequestId { get; init; } = Guid.NewGuid().ToString();

    [JsonPropertyName("page")]
    public int? Page { get; init; }

    [JsonPropertyName("page_size")]
    public int? PageSize { get; init; }

    [JsonPropertyName("total")]
    public int? Total { get; init; }

    [JsonPropertyName("next")]
    public string? Next { get; init; }

    [JsonPropertyName("immich_base_url")]
    public string ImmichBaseUrl { get; init; } = string.Empty;
}

/// <summary>
/// Standard error codes used across all tools.
/// </summary>
public static class ErrorCodes
{
    public const string AuthFailed = "AUTH_FAILED";
    public const string NotFound = "NOT_FOUND";
    public const string Validation = "VALIDATION";
    public const string UpstreamError = "UPSTREAM_ERROR";
    public const string RateLimit = "RATE_LIMIT";
    public const string Unknown = "UNKNOWN";
    public const string ConfirmationRequired = "CONFIRMATION_REQUIRED";
}
