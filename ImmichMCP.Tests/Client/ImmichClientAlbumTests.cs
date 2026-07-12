using System.Net;
using FluentAssertions;
using RichardSzalay.MockHttp;
using ImmichMCP.Client;
using ImmichMCP.Tests.Fixtures;

namespace ImmichMCP.Tests.Client;

public class ImmichClientAlbumTests
{
    [Fact]
    public async Task GetAlbumAsync_Throws_OnServerError_NotSwallowedAsNotFound()
    {
        // A 404 is a legitimate "not found" (returns null); any OTHER error must surface,
        // not be swallowed into null and mislabelled as NOT_FOUND.
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        handler.When(HttpMethod.Get, "*/albums/boom")
            .Respond(HttpStatusCode.InternalServerError, "text/plain", "kaboom");

        var act = () => client.GetAlbumAsync("boom");

        var ex = await act.Should().ThrowAsync<ImmichApiException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task GetAlbumsAsync_ReturnsAlbums_WhenSuccessful()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var albums = new[]
        {
            TestFixtures.CreateAlbum(id: "album-1", albumName: "Vacation 2024"),
            TestFixtures.CreateAlbum(id: "album-2", albumName: "Family Photos")
        };

        handler.When(HttpMethod.Get, "*/albums*")
            .Respond("application/json", TestFixtures.ToJson(albums));

        // Act
        var result = await client.GetAlbumsAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result![0].AlbumName.Should().Be("Vacation 2024");
    }

    [Fact]
    public async Task GetAlbumAsync_ReturnsAlbum_WhenFound()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var albumId = "test-album-id";
        var album = TestFixtures.CreateAlbum(id: albumId);

        handler.When(HttpMethod.Get, $"*/albums/{albumId}")
            .Respond("application/json", TestFixtures.ToJson(album));

        // Act
        var result = await client.GetAlbumAsync(albumId);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(albumId);
    }

    [Fact]
    public async Task GetAlbumAsync_MapsContributorAssetCount_FromImmichContract()
    {
        // Arrange
        const string responseJson = """
            {
              "id": "album-1",
              "contributorCounts": [
                {
                  "userId": "user-1",
                  "assetCount": 7
                }
              ]
            }
            """;

        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        handler.When(HttpMethod.Get, "*/albums/album-1")
            .Respond("application/json", responseJson);

        // Act
        var result = await client.GetAlbumAsync("album-1");

        // Assert
        result.Should().NotBeNull();
        result!.ContributorCounts.Should().ContainSingle();
        result.ContributorCounts![0].AssetCount.Should().Be(7);
    }

    [Fact]
    public async Task GetAlbumAsync_ReturnsNull_WhenNotFound()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();

        handler.When(HttpMethod.Get, "*/albums/non-existent")
            .Respond(HttpStatusCode.NotFound);

        // Act
        var result = await client.GetAlbumAsync("non-existent");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAlbumAsync_ReturnsTrue_WhenSuccessful()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var albumId = "test-album-id";

        handler.When(HttpMethod.Delete, $"*/albums/{albumId}")
            .Respond(HttpStatusCode.NoContent);

        // Act
        var result = await client.DeleteAlbumAsync(albumId);

        // Assert
        result.Should().BeTrue();
    }
}
