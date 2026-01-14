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

        handler.When(HttpMethod.Get, "*/assets*")
            .Respond("application/json", TestFixtures.ToJson(assets));

        // Act
        var result = await client.GetAssetsAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result[0].Id.Should().Be("asset-1");
        result[1].Id.Should().Be("asset-2");
    }

    [Fact]
    public async Task GetAssetsAsync_ReturnsEmptyList_WhenNoAssets()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();

        handler.When(HttpMethod.Get, "*/assets*")
            .Respond("application/json", "[]");

        // Act
        var result = await client.GetAssetsAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
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
