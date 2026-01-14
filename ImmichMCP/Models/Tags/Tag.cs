using System.Text.Json.Serialization;

namespace ImmichMCP.Models.Tags;

/// <summary>
/// Represents a tag in Immich.
/// </summary>
public record Tag
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; init; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; init; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; init; }

    [JsonPropertyName("color")]
    public string? Color { get; init; }
}

/// <summary>
/// Request to create a tag.
/// </summary>
public record TagCreateRequest
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("color")]
    public string? Color { get; init; }
}

/// <summary>
/// Request to update a tag.
/// </summary>
public record TagUpdateRequest
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("color")]
    public string? Color { get; init; }
}

/// <summary>
/// Lightweight tag summary.
/// </summary>
public record TagSummary
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; init; } = string.Empty;

    [JsonPropertyName("color")]
    public string? Color { get; init; }

    public static TagSummary FromTag(Tag tag)
    {
        return new TagSummary
        {
            Id = tag.Id,
            Name = tag.Name,
            Value = tag.Value,
            Color = tag.Color
        };
    }
}
