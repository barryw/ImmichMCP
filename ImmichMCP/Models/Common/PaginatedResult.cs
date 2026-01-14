using System.Text.Json.Serialization;

namespace ImmichMCP.Models.Common;

/// <summary>
/// Represents a paginated result from the Immich API.
/// </summary>
/// <typeparam name="T">The type of items in the results.</typeparam>
public record PaginatedResult<T>
{
    [JsonPropertyName("items")]
    public List<T> Items { get; init; } = [];

    [JsonPropertyName("total")]
    public int Total { get; init; }

    [JsonPropertyName("nextPage")]
    public string? NextPage { get; init; }

    [JsonPropertyName("hasNextPage")]
    public bool HasNextPage { get; init; }
}

/// <summary>
/// Search result wrapper from Immich API.
/// </summary>
/// <typeparam name="T">The type of items in the results.</typeparam>
public record SearchResult<T>
{
    [JsonPropertyName("assets")]
    public SearchAssetResult<T> Assets { get; init; } = new();

    [JsonPropertyName("albums")]
    public SearchAlbumResult Albums { get; init; } = new();
}

/// <summary>
/// Asset search results.
/// </summary>
/// <typeparam name="T">The type of asset items.</typeparam>
public record SearchAssetResult<T>
{
    [JsonPropertyName("items")]
    public List<T> Items { get; init; } = [];

    [JsonPropertyName("total")]
    public int Total { get; init; }

    [JsonPropertyName("nextPage")]
    public string? NextPage { get; init; }
}

/// <summary>
/// Album search results.
/// </summary>
public record SearchAlbumResult
{
    [JsonPropertyName("items")]
    public List<object> Items { get; init; } = [];

    [JsonPropertyName("total")]
    public int Total { get; init; }
}

/// <summary>
/// Bulk operation result.
/// </summary>
public record BulkOperationResult
{
    [JsonPropertyName("affected_ids")]
    public string[] AffectedIds { get; init; } = [];

    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; init; } = [];

    [JsonPropertyName("executed")]
    public bool Executed { get; init; }
}
