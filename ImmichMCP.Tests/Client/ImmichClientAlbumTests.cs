using System.Net;
using FluentAssertions;
using RichardSzalay.MockHttp;
using ImmichMCP.Tests.Fixtures;

namespace ImmichMCP.Tests.Client;

public class ImmichClientAlbumTests
{
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
