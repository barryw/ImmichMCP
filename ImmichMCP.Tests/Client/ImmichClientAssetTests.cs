using System.Net;
using FluentAssertions;
using RichardSzalay.MockHttp;
using ImmichMCP.Tests.Fixtures;

namespace ImmichMCP.Tests.Client;

public class ImmichClientAssetTests
{
    [Fact]
    public async Task GetAssetsAsync_ReturnsAssets_WhenSuccessful()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var assets = new[]
        {
            TestFixtures.CreateAsset(id: "asset-1", originalFileName: "photo1.jpg"),
            TestFixtures.CreateAsset(id: "asset-2", originalFileName: "photo2.jpg")
        };
        var searchResult = new
        {
            assets = new
            {
                total = 2,
                count = 2,
                items = assets,
                nextPage = (string?)null
            }
        };

        handler.When(HttpMethod.Post, "*/search/metadata")
            .Respond("application/json", TestFixtures.ToJson(searchResult));

        // Act
        var result = await client.GetAssetsAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result[0].Id.Should().Be("asset-1");
        result[1].Id.Should().Be("asset-2");
    }

    [Fact]
    public async Task GetAssetsAsync_DeserializesDuration_WhenLegacyImmichReturnsTimespanString()
    {
        // Arrange
        const string responseJson = """
            {
              "assets": {
                "total": 1,
                "count": 1,
                "items": [
                  {
                    "id": "asset-1",
                    "duration": "0:00:01.23400"
                  }
                ],
                "nextPage": null
              }
            }
            """;

        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        handler.When(HttpMethod.Post, "*/search/metadata")
            .Respond("application/json", responseJson);

        // Act
        var result = await client.GetAssetsAsync();

        // Assert
        result.Should().ContainSingle();
        result[0].Duration.Should().Be(1234);
    }

    [Fact]
    public async Task GetAssetsAsync_DeserializesDuration_WhenImmichV3ReturnsMilliseconds()
    {
        // Arrange
        const string responseJson = """
            {
              "assets": {
                "total": 1,
                "count": 1,
                "items": [
                  {
                    "id": "asset-1",
                    "duration": 1234
                  }
                ],
                "nextPage": null
              }
            }
            """;

        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        handler.When(HttpMethod.Post, "*/search/metadata")
            .Respond("application/json", responseJson);

        // Act
        var result = await client.GetAssetsAsync();

        // Assert
        result.Should().ContainSingle();
        result[0].Duration.Should().Be(1234);
    }

    [Fact]
    public async Task GetAssetsAsync_ReturnsEmptyList_WhenNoAssets()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var searchResult = new
        {
            assets = new
            {
                total = 0,
                count = 0,
                items = Array.Empty<object>(),
                nextPage = (string?)null
            }
        };

        handler.When(HttpMethod.Post, "*/search/metadata")
            .Respond("application/json", TestFixtures.ToJson(searchResult));

        // Act
        var result = await client.GetAssetsAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAssetsAsync_SendsV3MetadataSearchRequest()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var searchResult = new
        {
            assets = new
            {
                total = 0,
                count = 0,
                items = Array.Empty<object>(),
                nextPage = (string?)null
            }
        };

        string? capturedRequestBody = null;
        handler.When(HttpMethod.Post, "*/search/metadata")
            .With(req =>
            {
                capturedRequestBody = req.Content!.ReadAsStringAsync().Result;
                return true;
            })
            .Respond("application/json", TestFixtures.ToJson(searchResult));

        // Act
        await client.GetAssetsAsync(size: 10, isArchived: true);

        // Assert
        capturedRequestBody.Should().NotBeNull();
        capturedRequestBody.Should().Contain("\"size\":10");
        capturedRequestBody.Should().Contain("\"visibility\":\"archive\"");
        capturedRequestBody.Should().Contain("\"withExif\":true");
        capturedRequestBody.Should().NotContain("\"isArchived\"");
    }

    [Fact]
    public async Task GetAssetAsync_ReturnsAsset_WhenFound()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var assetId = "test-asset-id";
        var asset = TestFixtures.CreateAsset(id: assetId);

        handler.When(HttpMethod.Get, $"*/assets/{assetId}")
            .Respond("application/json", TestFixtures.ToJson(asset));

        // Act
        var result = await client.GetAssetAsync(assetId);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(assetId);
    }

    [Fact]
    public async Task GetAssetAsync_ReturnsNull_WhenNotFound()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();

        handler.When(HttpMethod.Get, "*/assets/non-existent")
            .Respond(HttpStatusCode.NotFound);

        // Act
        var result = await client.GetAssetAsync("non-existent");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAssetStatisticsAsync_ReturnsStatistics()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var stats = new
        {
            images = 1000,
            videos = 200,
            total = 1200
        };

        handler.When(HttpMethod.Get, "*/assets/statistics")
            .Respond("application/json", TestFixtures.ToJson(stats));

        // Act
        var result = await client.GetAssetStatisticsAsync();

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task UploadAssetAsync_FetchesFullAsset_AfterMediaResponse()
    {
        // Arrange
        const string uploadResponseJson = """
            {
              "id": "asset-1",
              "status": "created"
            }
            """;
        const string assetResponseJson = """
            {
              "id": "asset-1",
              "type": "IMAGE",
              "originalFileName": "photo.jpg",
              "visibility": "archive",
              "duration": 1234
            }
            """;

        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        handler.When(HttpMethod.Post, "*/assets")
            .Respond("application/json", uploadResponseJson);
        handler.When(HttpMethod.Get, "*/assets/asset-1")
            .Respond("application/json", assetResponseJson);

        // Act
        var result = await client.UploadAssetAsync(
            [1, 2, 3],
            "photo.jpg",
            DateTime.UtcNow,
            isArchived: true);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be("asset-1");
        result.OriginalFileName.Should().Be("photo.jpg");
        result.Type.Should().Be("IMAGE");
        result.Visibility.Should().Be("archive");
    }

    [Fact]
    public async Task DeleteAssetsAsync_ReturnsTrue_WhenSuccessful()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();

        handler.When(HttpMethod.Delete, "*/assets")
            .Respond(HttpStatusCode.NoContent);

        // Act
        var result = await client.DeleteAssetsAsync(new[] { "asset-1", "asset-2" });

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void GetAssetDownloadInfo_ReturnsUrls()
    {
        // Arrange
        var (client, _) = MockHttpClientFactory.CreateMockClient("https://photos.example.com");
        var assetId = "test-asset-id";

        // Act
        var result = client.GetAssetDownloadInfo(assetId, "test.jpg");

        // Assert
        result.Should().NotBeNull();
        result.OriginalUrl.Should().Contain(assetId);
        result.ThumbnailUrl.Should().Contain(assetId);
    }
}
