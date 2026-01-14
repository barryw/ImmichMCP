using System.Text.Json.Serialization;

namespace ImmichMCP.Models.People;

/// <summary>
/// Represents a person (face cluster) in Immich.
/// </summary>
public record Person
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("birthDate")]
    public DateOnly? BirthDate { get; init; }

    [JsonPropertyName("thumbnailPath")]
    public string ThumbnailPath { get; init; } = string.Empty;

    [JsonPropertyName("isHidden")]
    public bool IsHidden { get; init; }

    [JsonPropertyName("updatedAt")]
    public DateTime? UpdatedAt { get; init; }
}

/// <summary>
/// Response containing list of people.
/// </summary>
public record PeopleResponse
{
    [JsonPropertyName("people")]
    public List<PersonWithFaces> People { get; init; } = [];

    [JsonPropertyName("total")]
    public int Total { get; init; }

    [JsonPropertyName("visible")]
    public int Visible { get; init; }

    [JsonPropertyName("hidden")]
    public int Hidden { get; init; }
}

/// <summary>
/// Person with face count.
/// </summary>
public record PersonWithFaces : Person
{
    [JsonPropertyName("faces")]
    public List<FaceInfo>? Faces { get; init; }
}

/// <summary>
/// Face information.
/// </summary>
public record FaceInfo
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("imageHeight")]
    public int ImageHeight { get; init; }

    [JsonPropertyName("imageWidth")]
    public int ImageWidth { get; init; }

    [JsonPropertyName("boundingBoxX1")]
    public int BoundingBoxX1 { get; init; }

    [JsonPropertyName("boundingBoxX2")]
    public int BoundingBoxX2 { get; init; }

    [JsonPropertyName("boundingBoxY1")]
    public int BoundingBoxY1 { get; init; }

    [JsonPropertyName("boundingBoxY2")]
    public int BoundingBoxY2 { get; init; }
}

/// <summary>
/// Request to update a person.
/// </summary>
public record PersonUpdateRequest
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("birthDate")]
    public DateOnly? BirthDate { get; init; }

    [JsonPropertyName("isHidden")]
    public bool? IsHidden { get; init; }

    [JsonPropertyName("featureFaceAssetId")]
    public string? FeatureFaceAssetId { get; init; }
}

/// <summary>
/// Person statistics.
/// </summary>
public record PersonStatistics
{
    [JsonPropertyName("assets")]
    public int Assets { get; init; }
}

/// <summary>
/// Lightweight person summary.
/// </summary>
public record PersonSummary
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("birthDate")]
    public DateOnly? BirthDate { get; init; }

    [JsonPropertyName("isHidden")]
    public bool IsHidden { get; init; }

    [JsonPropertyName("thumbnailPath")]
    public string ThumbnailPath { get; init; } = string.Empty;

    public static PersonSummary FromPerson(Person person)
    {
        return new PersonSummary
        {
            Id = person.Id,
            Name = person.Name,
            BirthDate = person.BirthDate,
            IsHidden = person.IsHidden,
            ThumbnailPath = person.ThumbnailPath
        };
    }
}
