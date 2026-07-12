using System.Text.Json.Serialization;
using ImmichMCP.Utils;

namespace ImmichMCP.Models.Assets;

/// <summary>
/// Represents an asset (photo/video) in Immich.
/// </summary>
public record Asset
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("ownerId")]
    public string OwnerId { get; init; } = string.Empty;

    [JsonPropertyName("libraryId")]
    public string? LibraryId { get; init; }

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("originalPath")]
    public string OriginalPath { get; init; } = string.Empty;

    [JsonPropertyName("originalFileName")]
    public string OriginalFileName { get; init; } = string.Empty;

    [JsonPropertyName("originalMimeType")]
    public string? OriginalMimeType { get; init; }

    [JsonPropertyName("thumbhash")]
    public string? Thumbhash { get; init; }

    [JsonPropertyName("fileCreatedAt")]
    public DateTime FileCreatedAt { get; init; }

    [JsonPropertyName("fileModifiedAt")]
    public DateTime FileModifiedAt { get; init; }

    [JsonPropertyName("localDateTime")]
    public DateTime LocalDateTime { get; init; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; init; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; init; }

    [JsonPropertyName("isFavorite")]
    public bool IsFavorite { get; init; }

    [JsonPropertyName("isArchived")]
    public bool IsArchived { get; init; }

    [JsonPropertyName("isTrashed")]
    public bool IsTrashed { get; init; }

    [JsonPropertyName("isOffline")]
    public bool IsOffline { get; init; }

    [JsonPropertyName("hasMetadata")]
    public bool HasMetadata { get; init; }

    [JsonPropertyName("duration")]
    [JsonConverter(typeof(DurationMillisecondsJsonConverter))]
    public int? Duration { get; init; }

    [JsonPropertyName("visibility")]
    public string Visibility { get; init; } = "timeline";

    [JsonPropertyName("width")]
    public int? Width { get; init; }

    [JsonPropertyName("height")]
    public int? Height { get; init; }

    [JsonPropertyName("isEdited")]
    public bool IsEdited { get; init; }

    [JsonPropertyName("exifInfo")]
    public ExifInfo? ExifInfo { get; init; }

    [JsonPropertyName("livePhotoVideoId")]
    public string? LivePhotoVideoId { get; init; }

    [JsonPropertyName("people")]
    public List<PersonFace>? People { get; init; }

    [JsonPropertyName("checksum")]
    public string Checksum { get; init; } = string.Empty;

    [JsonPropertyName("stackCount")]
    public int? StackCount { get; init; }

    [JsonPropertyName("stack")]
    public AssetStack? Stack { get; init; }

    [JsonPropertyName("duplicateId")]
    public string? DuplicateId { get; init; }

    [JsonPropertyName("resized")]
    public bool Resized { get; init; }
}

/// <summary>
/// EXIF metadata for an asset.
/// </summary>
public record ExifInfo
{
    [JsonPropertyName("make")]
    public string? Make { get; init; }

    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("exifImageWidth")]
    public int? ExifImageWidth { get; init; }

    [JsonPropertyName("exifImageHeight")]
    public int? ExifImageHeight { get; init; }

    [JsonPropertyName("fileSizeInByte")]
    public long? FileSizeInByte { get; init; }

    [JsonPropertyName("orientation")]
    public string? Orientation { get; init; }

    [JsonPropertyName("dateTimeOriginal")]
    public DateTime? DateTimeOriginal { get; init; }

    [JsonPropertyName("modifyDate")]
    public DateTime? ModifyDate { get; init; }

    [JsonPropertyName("timeZone")]
    public string? TimeZone { get; init; }

    [JsonPropertyName("lensModel")]
    public string? LensModel { get; init; }

    [JsonPropertyName("fNumber")]
    public double? FNumber { get; init; }

    [JsonPropertyName("focalLength")]
    public double? FocalLength { get; init; }

    [JsonPropertyName("iso")]
    public int? Iso { get; init; }

    [JsonPropertyName("exposureTime")]
    public string? ExposureTime { get; init; }

    [JsonPropertyName("latitude")]
    public double? Latitude { get; init; }

    [JsonPropertyName("longitude")]
    public double? Longitude { get; init; }

    [JsonPropertyName("city")]
    public string? City { get; init; }

    [JsonPropertyName("state")]
    public string? State { get; init; }

    [JsonPropertyName("country")]
    public string? Country { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("projectionType")]
    public string? ProjectionType { get; init; }

    [JsonPropertyName("rating")]
    public int? Rating { get; init; }
}

/// <summary>
/// Person face information.
/// </summary>
public record PersonFace
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("birthDate")]
    public DateOnly? BirthDate { get; init; }

    [JsonPropertyName("thumbnailPath")]
    public string? ThumbnailPath { get; init; }

    [JsonPropertyName("isHidden")]
    public bool IsHidden { get; init; }
}

/// <summary>
/// Asset stack information.
/// </summary>
public record AssetStack
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("primaryAssetId")]
    public string PrimaryAssetId { get; init; } = string.Empty;

    [JsonPropertyName("assetCount")]
    public int AssetCount { get; init; }
}

/// <summary>
/// Asset statistics.
/// </summary>
public record AssetStatistics
{
    [JsonPropertyName("images")]
    public int Images { get; init; }

    [JsonPropertyName("videos")]
    public int Videos { get; init; }

    [JsonPropertyName("total")]
    public int Total { get; init; }
}

/// <summary>
/// Response returned when an asset upload is accepted.
/// </summary>
public record AssetMediaResponse
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;
}

/// <summary>
/// Request to update an asset.
/// </summary>
public record AssetUpdateRequest
{
    [JsonPropertyName("isFavorite")]
    public bool? IsFavorite { get; init; }

    [JsonPropertyName("visibility")]
    public string? Visibility { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("dateTimeOriginal")]
    public DateTime? DateTimeOriginal { get; init; }

    [JsonPropertyName("latitude")]
    public double? Latitude { get; init; }

    [JsonPropertyName("longitude")]
    public double? Longitude { get; init; }

    [JsonPropertyName("rating")]
    public int? Rating { get; init; }
}

/// <summary>
/// Request to bulk update assets.
/// </summary>
public record AssetBulkUpdateRequest
{
    [JsonPropertyName("ids")]
    public string[] Ids { get; init; } = [];

    [JsonPropertyName("isFavorite")]
    public bool? IsFavorite { get; init; }

    [JsonPropertyName("visibility")]
    public string? Visibility { get; init; }

    [JsonPropertyName("dateTimeOriginal")]
    public DateTime? DateTimeOriginal { get; init; }

    [JsonPropertyName("latitude")]
    public double? Latitude { get; init; }

    [JsonPropertyName("longitude")]
    public double? Longitude { get; init; }

    [JsonPropertyName("duplicateId")]
    public string? DuplicateId { get; init; }

    [JsonPropertyName("rating")]
    public int? Rating { get; init; }
}

/// <summary>
/// Asset download information.
/// </summary>
public record AssetDownloadInfo
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("original_file_name")]
    public string? OriginalFileName { get; init; }

    [JsonPropertyName("original_url")]
    public string OriginalUrl { get; init; } = string.Empty;

    [JsonPropertyName("thumbnail_url")]
    public string? ThumbnailUrl { get; init; }

    [JsonPropertyName("preview_url")]
    public string? PreviewUrl { get; init; }
}

/// <summary>
/// Lightweight asset summary for search results.
/// </summary>
public record AssetSummary
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("originalFileName")]
    public string OriginalFileName { get; init; } = string.Empty;

    [JsonPropertyName("fileCreatedAt")]
    public DateTime FileCreatedAt { get; init; }

    [JsonPropertyName("localDateTime")]
    public DateTime LocalDateTime { get; init; }

    [JsonPropertyName("isFavorite")]
    public bool IsFavorite { get; init; }

    [JsonPropertyName("isArchived")]
    public bool IsArchived { get; init; }

    [JsonPropertyName("duration")]
    public int? Duration { get; init; }

    [JsonPropertyName("visibility")]
    public string Visibility { get; init; } = "timeline";

    [JsonPropertyName("city")]
    public string? City { get; init; }

    [JsonPropertyName("country")]
    public string? Country { get; init; }

    [JsonPropertyName("make")]
    public string? Make { get; init; }

    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("thumbhash")]
    public string? Thumbhash { get; init; }

    public static AssetSummary FromAsset(Asset asset)
    {
        return new AssetSummary
        {
            Id = asset.Id,
            Type = asset.Type,
            OriginalFileName = asset.OriginalFileName,
            FileCreatedAt = asset.FileCreatedAt,
            LocalDateTime = asset.LocalDateTime,
            IsFavorite = asset.IsFavorite,
            IsArchived = asset.IsArchived,
            Duration = asset.Duration,
            Visibility = asset.Visibility,
            City = asset.ExifInfo?.City,
            Country = asset.ExifInfo?.Country,
            Make = asset.ExifInfo?.Make,
            Model = asset.ExifInfo?.Model,
            Thumbhash = asset.Thumbhash
        };
    }
}
