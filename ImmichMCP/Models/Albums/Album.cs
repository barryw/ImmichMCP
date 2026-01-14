using System.Text.Json.Serialization;
using ImmichMCP.Models.Assets;

namespace ImmichMCP.Models.Albums;

/// <summary>
/// Represents an album in Immich.
/// </summary>
public record Album
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("ownerId")]
    public string OwnerId { get; init; } = string.Empty;

    [JsonPropertyName("albumName")]
    public string AlbumName { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; init; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; init; }

    [JsonPropertyName("albumThumbnailAssetId")]
    public string? AlbumThumbnailAssetId { get; init; }

    [JsonPropertyName("shared")]
    public bool Shared { get; init; }

    [JsonPropertyName("hasSharedLink")]
    public bool HasSharedLink { get; init; }

    [JsonPropertyName("startDate")]
    public DateTime? StartDate { get; init; }

    [JsonPropertyName("endDate")]
    public DateTime? EndDate { get; init; }

    [JsonPropertyName("assets")]
    public List<Asset>? Assets { get; init; }

    [JsonPropertyName("assetCount")]
    public int AssetCount { get; init; }

    [JsonPropertyName("owner")]
    public AlbumOwner? Owner { get; init; }

    [JsonPropertyName("sharedUsers")]
    public List<AlbumUser>? SharedUsers { get; init; }

    [JsonPropertyName("isActivityEnabled")]
    public bool IsActivityEnabled { get; init; }

    [JsonPropertyName("order")]
    public string? Order { get; init; }

    [JsonPropertyName("lastModifiedAssetTimestamp")]
    public DateTime? LastModifiedAssetTimestamp { get; init; }
}

/// <summary>
/// Album owner information.
/// </summary>
public record AlbumOwner
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("profileImagePath")]
    public string ProfileImagePath { get; init; } = string.Empty;
}

/// <summary>
/// Album user (shared with) information.
/// </summary>
public record AlbumUser
{
    [JsonPropertyName("user")]
    public AlbumOwner User { get; init; } = new();

    [JsonPropertyName("role")]
    public string Role { get; init; } = string.Empty;
}

/// <summary>
/// Request to create an album.
/// </summary>
public record AlbumCreateRequest
{
    [JsonPropertyName("albumName")]
    public string AlbumName { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("assetIds")]
    public string[]? AssetIds { get; init; }

    [JsonPropertyName("sharedWithUserIds")]
    public string[]? SharedWithUserIds { get; init; }
}

/// <summary>
/// Request to update an album.
/// </summary>
public record AlbumUpdateRequest
{
    [JsonPropertyName("albumName")]
    public string? AlbumName { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("albumThumbnailAssetId")]
    public string? AlbumThumbnailAssetId { get; init; }

    [JsonPropertyName("isActivityEnabled")]
    public bool? IsActivityEnabled { get; init; }

    [JsonPropertyName("order")]
    public string? Order { get; init; }
}

/// <summary>
/// Album statistics.
/// </summary>
public record AlbumStatistics
{
    [JsonPropertyName("owned")]
    public int Owned { get; init; }

    [JsonPropertyName("shared")]
    public int Shared { get; init; }

    [JsonPropertyName("notShared")]
    public int NotShared { get; init; }
}

/// <summary>
/// Lightweight album summary.
/// </summary>
public record AlbumSummary
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("albumName")]
    public string AlbumName { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("assetCount")]
    public int AssetCount { get; init; }

    [JsonPropertyName("shared")]
    public bool Shared { get; init; }

    [JsonPropertyName("startDate")]
    public DateTime? StartDate { get; init; }

    [JsonPropertyName("endDate")]
    public DateTime? EndDate { get; init; }

    [JsonPropertyName("albumThumbnailAssetId")]
    public string? AlbumThumbnailAssetId { get; init; }

    public static AlbumSummary FromAlbum(Album album)
    {
        return new AlbumSummary
        {
            Id = album.Id,
            AlbumName = album.AlbumName,
            Description = album.Description,
            AssetCount = album.AssetCount,
            Shared = album.Shared,
            StartDate = album.StartDate,
            EndDate = album.EndDate,
            AlbumThumbnailAssetId = album.AlbumThumbnailAssetId
        };
    }
}
