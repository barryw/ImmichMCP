using System.Net;
using System.Text.Json;
using FluentAssertions;
using RichardSzalay.MockHttp;
using ImmichMCP.Client;
using ImmichMCP.Tests.Fixtures;
using ImmichMCP.Tools;

namespace ImmichMCP.Tests.Tools;

public class SharedLinkToolsTests
{
    private const string LinkJson = """
        {
          "id": "link-1",
          "key": "abc123key",
          "type": "ALBUM",
          "createdAt": "2026-01-01T00:00:00Z",
          "expiresAt": null,
          "userId": "user-1",
          "allowUpload": false,
          "allowDownload": true,
          "showMetadata": true
        }
        """;

    [Fact]
    public async Task Create_ReturnsSharedLink_WhenAlbumTypeSuccessful()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();

        handler.When(HttpMethod.Post, "*/shared-links")
            .Respond("application/json", LinkJson);

        // Act
        var response = await SharedLinkTools.Create(client, type: "ALBUM", albumId: "album-1");

        // Assert
        using var json = JsonDocument.Parse(response);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        var result = json.RootElement.GetProperty("result");
        result.GetProperty("id").GetString().Should().Be("link-1");
        result.GetProperty("key").GetString().Should().Be("abc123key");
        result.GetProperty("share_url").GetString().Should().Be("https://photos.example.com/share/abc123key");
    }

    [Fact]
    public async Task Create_ReturnsValidationError_WhenAlbumTypeMissingAlbumId()
    {
        // Arrange
        var (client, _) = MockHttpClientFactory.CreateMockClient();

        // Act
        var response = await SharedLinkTools.Create(client, type: "ALBUM", albumId: null);

        // Assert
        using var json = JsonDocument.Parse(response);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION");
    }

    [Fact]
    public async Task Create_ReturnsValidationError_WhenIndividualTypeMissingAssetIds()
    {
        // Arrange
        var (client, _) = MockHttpClientFactory.CreateMockClient();

        // Act
        var response = await SharedLinkTools.Create(client, type: "INDIVIDUAL", assetIds: null);

        // Assert
        using var json = JsonDocument.Parse(response);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION");
    }

    [Fact]
    public async Task Update_ReturnsUpdatedSharedLink_WhenSuccessful()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();

        handler.When(HttpMethod.Patch, "*/shared-links/link-1")
            .Respond("application/json", LinkJson);

        // Act
        var response = await SharedLinkTools.Update(client, "link-1", allowDownload: true);

        // Assert
        using var json = JsonDocument.Parse(response);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("result").GetProperty("id").GetString().Should().Be("link-1");
    }

    [Fact]
    public async Task Update_Throws_WhenUpstreamReturnsNotFound()
    {
        // Arrange: PATCH surfaces non-success responses as exceptions (unlike GET, which
        // treats 404 as a legitimate "not found" and returns null), see ImmichClient.PatchAsync.
        var (client, handler) = MockHttpClientFactory.CreateMockClient();

        handler.When(HttpMethod.Patch, "*/shared-links/missing-link")
            .Respond(HttpStatusCode.NotFound);

        // Act
        var act = () => SharedLinkTools.Update(client, "missing-link", allowDownload: true);

        // Assert
        await act.Should().ThrowAsync<ImmichApiException>();
    }

    [Fact]
    public async Task Delete_ReturnsConfirmationRequired_AndDoesNotCallDelete_WhenConfirmFalse()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();

        handler.When(HttpMethod.Get, "*/shared-links/link-1")
            .Respond("application/json", LinkJson);

        var deleteRequest = handler.When(HttpMethod.Delete, "*/shared-links/link-1")
            .Respond(HttpStatusCode.NoContent);

        // Act
        var response = await SharedLinkTools.Delete(client, "link-1", confirm: false);

        // Assert
        using var json = JsonDocument.Parse(response);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("CONFIRMATION_REQUIRED");
        var details = json.RootElement.GetProperty("error").GetProperty("details");
        details.GetProperty("link_id").GetString().Should().Be("link-1");
        details.GetProperty("key").GetString().Should().Be("abc123key");

        handler.GetMatchCount(deleteRequest).Should().Be(0);
    }

    [Fact]
    public async Task Delete_ReturnsNotFoundError_WhenSharedLinkMissingAndConfirmFalse()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();

        handler.When(HttpMethod.Get, "*/shared-links/missing-link")
            .Respond(HttpStatusCode.NotFound);

        // Act
        var response = await SharedLinkTools.Delete(client, "missing-link", confirm: false);

        // Assert
        using var json = JsonDocument.Parse(response);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Delete_DeletesSharedLink_WhenConfirmTrue()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();

        var deleteRequest = handler.When(HttpMethod.Delete, "*/shared-links/link-1")
            .Respond(HttpStatusCode.NoContent);

        // Act
        var response = await SharedLinkTools.Delete(client, "link-1", confirm: true);

        // Assert
        using var json = JsonDocument.Parse(response);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("result").GetProperty("deleted").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("result").GetProperty("link_id").GetString().Should().Be("link-1");

        handler.GetMatchCount(deleteRequest).Should().Be(1);
    }
}
