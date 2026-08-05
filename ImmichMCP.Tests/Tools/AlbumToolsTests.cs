using System.Text.Json;
using FluentAssertions;
using RichardSzalay.MockHttp;
using ImmichMCP.Client;
using ImmichMCP.Tests.Fixtures;
using ImmichMCP.Tools;

namespace ImmichMCP.Tests.Tools;

public class AlbumToolsTests
{
    [Fact]
    public async Task Create_ReturnsAlbum_WhenSuccessful()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var album = TestFixtures.CreateAlbum(id: "album-1", albumName: "Vacation 2026");

        handler.When(HttpMethod.Post, "*/albums")
            .Respond("application/json", TestFixtures.ToJson(album));

        // Act
        var response = await AlbumTools.Create(client, albumName: "Vacation 2026");

        // Assert
        using var json = JsonDocument.Parse(response);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("result").GetProperty("albumName").GetString().Should().Be("Vacation 2026");
    }

    [Fact]
    public async Task Create_ReturnsValidationError_WhenAlbumNameMissing()
    {
        // Arrange
        var (client, _) = MockHttpClientFactory.CreateMockClient();

        // Act
        var response = await AlbumTools.Create(client, albumName: "  ");

        // Assert
        using var json = JsonDocument.Parse(response);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION");
    }

    [Fact]
    public async Task Update_ReturnsUpdatedAlbum_WhenSuccessful()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var album = TestFixtures.CreateAlbum(id: "album-1", albumName: "Renamed Album");

        handler.When(HttpMethod.Patch, "*/albums/album-1")
            .Respond("application/json", TestFixtures.ToJson(album));

        // Act
        var response = await AlbumTools.Update(client, "album-1", albumName: "Renamed Album");

        // Assert
        using var json = JsonDocument.Parse(response);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("result").GetProperty("albumName").GetString().Should().Be("Renamed Album");
    }

    [Fact]
    public async Task Update_Throws_WhenUpstreamReturnsNotFound()
    {
        // Arrange: PATCH surfaces non-success responses as exceptions (unlike GET, which
        // treats 404 as a legitimate "not found" and returns null), see ImmichClient.PatchAsync.
        var (client, handler) = MockHttpClientFactory.CreateMockClient();

        handler.When(HttpMethod.Patch, "*/albums/missing-album")
            .Respond(System.Net.HttpStatusCode.NotFound);

        // Act
        var act = () => AlbumTools.Update(client, "missing-album", albumName: "New Name");

        // Assert
        await act.Should().ThrowAsync<ImmichApiException>();
    }

    [Fact]
    public async Task AddAssets_ReturnsAddedCount_WhenSuccessful()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        // Both elements need the same anonymous-type shape or the array has no common type.
        var results = new[]
        {
            new { id = "asset-1", success = true, errorMessage = (string?)null },
            new { id = "asset-2", success = false, errorMessage = (string?)"duplicate" }
        };

        handler.When(HttpMethod.Put, "*/albums/album-1/assets")
            .Respond("application/json", TestFixtures.ToJson(results));

        // Act
        var response = await AlbumTools.AddAssets(client, "album-1", "asset-1,asset-2");

        // Assert
        using var json = JsonDocument.Parse(response);
        var result = json.RootElement.GetProperty("result");
        result.GetProperty("album_id").GetString().Should().Be("album-1");
        result.GetProperty("added").GetInt32().Should().Be(1);
        result.GetProperty("failed").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task AddAssets_ReturnsValidationError_WhenAssetIdsEmpty()
    {
        // Arrange
        var (client, _) = MockHttpClientFactory.CreateMockClient();

        // Act
        var response = await AlbumTools.AddAssets(client, "album-1", "  ");

        // Assert
        using var json = JsonDocument.Parse(response);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION");
    }

    [Fact]
    public async Task RemoveAssets_ReturnsRemovedCount_WhenSuccessful()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var results = new[]
        {
            new { id = "asset-1", success = true }
        };

        handler.When(HttpMethod.Delete, "*/albums/album-1/assets")
            .Respond("application/json", TestFixtures.ToJson(results));

        // Act
        var response = await AlbumTools.RemoveAssets(client, "album-1", "asset-1");

        // Assert
        using var json = JsonDocument.Parse(response);
        var result = json.RootElement.GetProperty("result");
        result.GetProperty("album_id").GetString().Should().Be("album-1");
        result.GetProperty("removed").GetInt32().Should().Be(1);
        result.GetProperty("failed").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task RemoveAssets_ReturnsValidationError_WhenAssetIdsEmpty()
    {
        // Arrange
        var (client, _) = MockHttpClientFactory.CreateMockClient();

        // Act
        var response = await AlbumTools.RemoveAssets(client, "album-1", "");

        // Assert
        using var json = JsonDocument.Parse(response);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION");
    }

    [Fact]
    public async Task Delete_ReturnsConfirmationRequired_AndDoesNotCallDelete_WhenConfirmFalse()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var album = TestFixtures.CreateAlbum(id: "album-1", albumName: "To Delete", assetCount: 12);

        handler.When(HttpMethod.Get, "*/albums/album-1")
            .Respond("application/json", TestFixtures.ToJson(album));

        var deleteRequest = handler.When(HttpMethod.Delete, "*/albums/album-1")
            .Respond(System.Net.HttpStatusCode.NoContent);

        // Act
        var response = await AlbumTools.Delete(client, "album-1", confirm: false);

        // Assert: dry run envelope, and the destructive endpoint is never hit.
        using var json = JsonDocument.Parse(response);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("CONFIRMATION_REQUIRED");
        var details = json.RootElement.GetProperty("error").GetProperty("details");
        details.GetProperty("album_id").GetString().Should().Be("album-1");
        details.GetProperty("asset_count").GetInt32().Should().Be(12);

        handler.GetMatchCount(deleteRequest).Should().Be(0);
    }

    [Fact]
    public async Task Delete_ReturnsNotFoundError_WhenAlbumMissingAndConfirmFalse()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();

        handler.When(HttpMethod.Get, "*/albums/missing-album")
            .Respond(System.Net.HttpStatusCode.NotFound);

        // Act
        var response = await AlbumTools.Delete(client, "missing-album", confirm: false);

        // Assert
        using var json = JsonDocument.Parse(response);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Delete_DeletesAlbum_WhenConfirmTrue()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();

        var deleteRequest = handler.When(HttpMethod.Delete, "*/albums/album-1")
            .Respond(System.Net.HttpStatusCode.NoContent);

        // Act
        var response = await AlbumTools.Delete(client, "album-1", confirm: true);

        // Assert
        using var json = JsonDocument.Parse(response);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("result").GetProperty("deleted").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("result").GetProperty("album_id").GetString().Should().Be("album-1");

        handler.GetMatchCount(deleteRequest).Should().Be(1);
    }
}
