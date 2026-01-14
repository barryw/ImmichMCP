using System.Text.Json.Serialization;

namespace ImmichMCP.Models.Search;

/// <summary>
/// Request for metadata search.
/// </summary>
public record MetadataSearchRequest
{
    [JsonPropertyName("page")]
    public int? Page { get; init; }

    [JsonPropertyName("size")]
    public int? Size { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("isFavorite")]
    public bool? IsFavorite { get; init; }

    [JsonPropertyName("isArchived")]
    public bool? IsArchived { get; init; }

    [JsonPropertyName("isTrashed")]
    public bool? IsTrashed { get; init; }

    [JsonPropertyName("isVisible")]
    public bool? IsVisible { get; init; }

    [JsonPropertyName("isMotion")]
    public bool? IsMotion { get; init; }

    [JsonPropertyName("isNotInAlbum")]
    public bool? IsNotInAlbum { get; init; }

    [JsonPropertyName("isOffline")]
    public bool? IsOffline { get; init; }

    [JsonPropertyName("withExif")]
    public bool? WithExif { get; init; }

    [JsonPropertyName("withPeople")]
    public bool? WithPeople { get; init; }

    [JsonPropertyName("takenAfter")]
    public DateTime? TakenAfter { get; init; }

    [JsonPropertyName("takenBefore")]
    public DateTime? TakenBefore { get; init; }

    [JsonPropertyName("updatedAfter")]
    public DateTime? UpdatedAfter { get; init; }

    [JsonPropertyName("updatedBefore")]
    public DateTime? UpdatedBefore { get; init; }

    [JsonPropertyName("trashedAfter")]
    public DateTime? TrashedAfter { get; init; }

    [JsonPropertyName("trashedBefore")]
    public DateTime? TrashedBefore { get; init; }

    [JsonPropertyName("city")]
    public string? City { get; init; }

    [JsonPropertyName("state")]
    public string? State { get; init; }

    [JsonPropertyName("country")]
    public string? Country { get; init; }

    [JsonPropertyName("make")]
    public string? Make { get; init; }

    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("lensModel")]
    public string? LensModel { get; init; }

    [JsonPropertyName("personIds")]
    public string[]? PersonIds { get; init; }

    [JsonPropertyName("originalFileName")]
    public string? OriginalFileName { get; init; }

    [JsonPropertyName("originalPath")]
    public string? OriginalPath { get; init; }

    [JsonPropertyName("deviceAssetId")]
    public string? DeviceAssetId { get; init; }

    [JsonPropertyName("deviceId")]
    public string? DeviceId { get; init; }

    [JsonPropertyName("libraryId")]
    public string? LibraryId { get; init; }

    [JsonPropertyName("checksum")]
    public string? Checksum { get; init; }

    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("order")]
    public string? Order { get; init; }
}

/// <summary>
/// Request for smart (ML/CLIP) search.
/// </summary>
public record SmartSearchRequest
{
    [JsonPropertyName("query")]
    public string Query { get; init; } = string.Empty;

    [JsonPropertyName("page")]
    public int? Page { get; init; }

    [JsonPropertyName("size")]
    public int? Size { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("isFavorite")]
    public bool? IsFavorite { get; init; }

    [JsonPropertyName("isArchived")]
    public bool? IsArchived { get; init; }

    [JsonPropertyName("isTrashed")]
    public bool? IsTrashed { get; init; }

    [JsonPropertyName("isVisible")]
    public bool? IsVisible { get; init; }

    [JsonPropertyName("city")]
    public string? City { get; init; }

    [JsonPropertyName("state")]
    public string? State { get; init; }

    [JsonPropertyName("country")]
    public string? Country { get; init; }

    [JsonPropertyName("make")]
    public string? Make { get; init; }

    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("takenAfter")]
    public DateTime? TakenAfter { get; init; }

    [JsonPropertyName("takenBefore")]
    public DateTime? TakenBefore { get; init; }

    [JsonPropertyName("personIds")]
    public string[]? PersonIds { get; init; }
}

/// <summary>
/// Explore data for discovery.
/// </summary>
public record ExploreData
{
    [JsonPropertyName("fieldName")]
    public string FieldName { get; init; } = string.Empty;

    [JsonPropertyName("items")]
    public List<ExploreItem> Items { get; init; } = [];
}

/// <summary>
/// Individual explore item.
/// </summary>
public record ExploreItem
{
    [JsonPropertyName("value")]
    public string Value { get; init; } = string.Empty;

    [JsonPropertyName("data")]
    public ExploreItemData Data { get; init; } = new();
}

/// <summary>
/// Explore item data.
/// </summary>
public record ExploreItemData
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("thumbhash")]
    public string? Thumbhash { get; init; }
}
