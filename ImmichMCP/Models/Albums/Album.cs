using System.Text.Json.Serialization;

namespace ImmichMCP.Models.Albums;

/// <summary>
/// Represents an album in Immich.
/// </summary>
public record Album
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

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

    [JsonPropertyName("assetCount")]
    public int AssetCount { get; init; }

    [JsonPropertyName("albumUsers")]
    public List<AlbumUser>? AlbumUsers { get; init; }

    [JsonPropertyName("contributorCounts")]
    public List<AlbumContributorCount>? ContributorCounts { get; init; }

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

    [JsonPropertyName("albumUsers")]
    public AlbumUserCreateRequest[]? AlbumUsers { get; init; }
}

/// <summary>
/// User to share an album with when creating an album.
/// </summary>
public record AlbumUserCreateRequest
{
    [JsonPropertyName("userId")]
    public string UserId { get; init; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; init; } = "viewer";
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
/// Per-user contribution count in an album.
/// </summary>
public record AlbumContributorCount
{
    [JsonPropertyName("userId")]
    public string UserId { get; init; } = string.Empty;

    [JsonPropertyName("assetCount")]
    public int AssetCount { get; init; }
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
