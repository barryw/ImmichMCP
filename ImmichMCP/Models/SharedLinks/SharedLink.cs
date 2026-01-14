using System.Text.Json.Serialization;
using ImmichMCP.Models.Assets;
using ImmichMCP.Models.Albums;

namespace ImmichMCP.Models.SharedLinks;

/// <summary>
/// Represents a shared link in Immich.
/// </summary>
public record SharedLink
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("key")]
    public string Key { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; init; }

    [JsonPropertyName("expiresAt")]
    public DateTime? ExpiresAt { get; init; }

    [JsonPropertyName("userId")]
    public string UserId { get; init; } = string.Empty;

    [JsonPropertyName("allowUpload")]
    public bool AllowUpload { get; init; }

    [JsonPropertyName("allowDownload")]
    public bool AllowDownload { get; init; }

    [JsonPropertyName("showMetadata")]
    public bool ShowMetadata { get; init; }

    [JsonPropertyName("password")]
    public string? Password { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("album")]
    public Album? Album { get; init; }

    [JsonPropertyName("assets")]
    public List<Asset>? Assets { get; init; }
}

/// <summary>
/// Request to create a shared link.
/// </summary>
public record SharedLinkCreateRequest
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "ALBUM";

    [JsonPropertyName("albumId")]
    public string? AlbumId { get; init; }

    [JsonPropertyName("assetIds")]
    public string[]? AssetIds { get; init; }

    [JsonPropertyName("expiresAt")]
    public DateTime? ExpiresAt { get; init; }

    [JsonPropertyName("allowUpload")]
    public bool? AllowUpload { get; init; }

    [JsonPropertyName("allowDownload")]
    public bool? AllowDownload { get; init; }

    [JsonPropertyName("showMetadata")]
    public bool? ShowMetadata { get; init; }

    [JsonPropertyName("password")]
    public string? Password { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

/// <summary>
/// Request to update a shared link.
/// </summary>
public record SharedLinkUpdateRequest
{
    [JsonPropertyName("expiresAt")]
    public DateTime? ExpiresAt { get; init; }

    [JsonPropertyName("allowUpload")]
    public bool? AllowUpload { get; init; }

    [JsonPropertyName("allowDownload")]
    public bool? AllowDownload { get; init; }

    [JsonPropertyName("showMetadata")]
    public bool? ShowMetadata { get; init; }

    [JsonPropertyName("password")]
    public string? Password { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("changeExpiryTime")]
    public bool? ChangeExpiryTime { get; init; }
}

/// <summary>
/// Lightweight shared link summary.
/// </summary>
public record SharedLinkSummary
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("key")]
    public string Key { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; init; }

    [JsonPropertyName("expiresAt")]
    public DateTime? ExpiresAt { get; init; }

    [JsonPropertyName("allowUpload")]
    public bool AllowUpload { get; init; }

    [JsonPropertyName("allowDownload")]
    public bool AllowDownload { get; init; }

    [JsonPropertyName("showMetadata")]
    public bool ShowMetadata { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("album_name")]
    public string? AlbumName { get; init; }

    [JsonPropertyName("asset_count")]
    public int AssetCount { get; init; }

    public static SharedLinkSummary FromSharedLink(SharedLink link, string baseUrl)
    {
        return new SharedLinkSummary
        {
            Id = link.Id,
            Key = link.Key,
            Type = link.Type,
            CreatedAt = link.CreatedAt,
            ExpiresAt = link.ExpiresAt,
            AllowUpload = link.AllowUpload,
            AllowDownload = link.AllowDownload,
            ShowMetadata = link.ShowMetadata,
            Description = link.Description,
            AlbumName = link.Album?.AlbumName,
            AssetCount = link.Assets?.Count ?? link.Album?.AssetCount ?? 0
        };
    }
}
