using System.Text.Json.Serialization;

namespace ImmichMCP.Models.Activities;

/// <summary>
/// Represents an activity (comment or like) in Immich.
/// </summary>
public record Activity
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; init; }

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("comment")]
    public string? Comment { get; init; }

    [JsonPropertyName("assetId")]
    public string? AssetId { get; init; }

    [JsonPropertyName("user")]
    public ActivityUser User { get; init; } = new();
}

/// <summary>
/// User information for an activity.
/// </summary>
public record ActivityUser
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
/// Request to create an activity.
/// </summary>
public record ActivityCreateRequest
{
    [JsonPropertyName("albumId")]
    public string AlbumId { get; init; } = string.Empty;

    [JsonPropertyName("assetId")]
    public string? AssetId { get; init; }

    [JsonPropertyName("type")]
    public string Type { get; init; } = "comment";

    [JsonPropertyName("comment")]
    public string? Comment { get; init; }
}

/// <summary>
/// Activity statistics.
/// </summary>
public record ActivityStatistics
{
    [JsonPropertyName("comments")]
    public int Comments { get; init; }
}

/// <summary>
/// Lightweight activity summary.
/// </summary>
public record ActivitySummary
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; init; }

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("comment")]
    public string? Comment { get; init; }

    [JsonPropertyName("assetId")]
    public string? AssetId { get; init; }

    [JsonPropertyName("userName")]
    public string UserName { get; init; } = string.Empty;

    public static ActivitySummary FromActivity(Activity activity)
    {
        return new ActivitySummary
        {
            Id = activity.Id,
            CreatedAt = activity.CreatedAt,
            Type = activity.Type,
            Comment = activity.Comment,
            AssetId = activity.AssetId,
            UserName = activity.User.Name
        };
    }
}
