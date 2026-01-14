using System.Text.Json;
using ImmichMCP.Models.Assets;
using ImmichMCP.Models.Albums;
using ImmichMCP.Models.People;
using ImmichMCP.Models.Tags;

namespace ImmichMCP.Tests.Fixtures;

public static class TestFixtures
{
    public static Asset CreateAsset(
        string? id = null,
        string type = "IMAGE",
        string originalFileName = "test.jpg",
        bool isFavorite = false,
        bool isArchived = false)
    {
        return new Asset
        {
            Id = id ?? Guid.NewGuid().ToString(),
            DeviceAssetId = "test-device-asset",
            OwnerId = Guid.NewGuid().ToString(),
            DeviceId = "test-device",
            Type = type,
            OriginalPath = $"/uploads/{originalFileName}",
            OriginalFileName = originalFileName,
            OriginalMimeType = type == "IMAGE" ? "image/jpeg" : "video/mp4",
            FileCreatedAt = DateTime.UtcNow.AddDays(-1),
            FileModifiedAt = DateTime.UtcNow.AddDays(-1),
            LocalDateTime = DateTime.UtcNow.AddDays(-1),
            UpdatedAt = DateTime.UtcNow,
            IsFavorite = isFavorite,
            IsArchived = isArchived,
            IsTrashed = false,
            IsOffline = false,
            HasMetadata = true,
            Duration = "0:00:00.00000",
            Checksum = "abc123",
            Resized = true,
            ExifInfo = new ExifInfo
            {
                Make = "Canon",
                Model = "EOS R5",
                ExifImageWidth = 8192,
                ExifImageHeight = 5464,
                FileSizeInByte = 10485760,
                City = "New York",
                Country = "USA"
            }
        };
    }

    public static Album CreateAlbum(
        string? id = null,
        string albumName = "Test Album",
        int assetCount = 0,
        bool shared = false)
    {
        return new Album
        {
            Id = id ?? Guid.NewGuid().ToString(),
            OwnerId = Guid.NewGuid().ToString(),
            AlbumName = albumName,
            Description = "Test album description",
            CreatedAt = DateTime.UtcNow.AddDays(-7),
            UpdatedAt = DateTime.UtcNow,
            Shared = shared,
            HasSharedLink = false,
            AssetCount = assetCount,
            IsActivityEnabled = true
        };
    }

    public static Person CreatePerson(
        string? id = null,
        string name = "Test Person",
        bool isHidden = false)
    {
        return new Person
        {
            Id = id ?? Guid.NewGuid().ToString(),
            Name = name,
            ThumbnailPath = "/thumbnails/person.jpg",
            IsHidden = isHidden,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public static Tag CreateTag(
        string? id = null,
        string name = "Test Tag")
    {
        return new Tag
        {
            Id = id ?? Guid.NewGuid().ToString(),
            Name = name,
            Value = name.ToLowerInvariant(),
            CreatedAt = DateTime.UtcNow.AddDays(-7),
            UpdatedAt = DateTime.UtcNow
        };
    }

    public static string ToJson<T>(T obj) => JsonSerializer.Serialize(obj, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    });
}
