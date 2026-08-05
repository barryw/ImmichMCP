using System.Net;
using System.Text.Json;
using FluentAssertions;
using RichardSzalay.MockHttp;
using ImmichMCP.Client;
using ImmichMCP.Tests.Fixtures;
using ImmichMCP.Tools;

namespace ImmichMCP.Tests.Tools;

/// <summary>
/// Covers AssetTools' mutating/upload methods (Upload, UploadFromPath, AuthorizeUpload, Update,
/// BulkUpdate, Delete). Download-only methods (DownloadOriginal/DownloadThumbnail) are covered in
/// AssetDownloadToolTests. Read-only methods (List/Get/GetExif/Statistics) are not covered here.
/// </summary>
public class AssetToolsTests
{
    [Fact]
    public async Task Upload_ReturnsUploadedAsset_WhenSuccessful()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        const string uploadResponseJson = """
            {
              "id": "asset-1",
              "status": "created"
            }
            """;
        var asset = TestFixtures.CreateAsset(id: "asset-1", originalFileName: "photo.jpg");

        handler.When(HttpMethod.Post, "*/assets")
            .Respond("application/json", uploadResponseJson);
        handler.When(HttpMethod.Get, "*/assets/asset-1")
            .Respond("application/json", TestFixtures.ToJson(asset));

        var fileContent = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 });

        // Act
        var response = await AssetTools.Upload(client, fileContent, "photo.jpg");

        // Assert
        using var json = JsonDocument.Parse(response);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        var result = json.RootElement.GetProperty("result");
        result.GetProperty("asset_id").GetString().Should().Be("asset-1");
        result.GetProperty("status").GetString().Should().Be("uploaded");
    }

    [Fact]
    public async Task Upload_ReturnsValidationError_WhenBase64Invalid()
    {
        // Arrange
        var (client, _) = MockHttpClientFactory.CreateMockClient();

        // Act
        var response = await AssetTools.Upload(client, "not-valid-base64!!", "photo.jpg");

        // Assert
        using var json = JsonDocument.Parse(response);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION");
    }

    [Fact]
    public async Task UploadFromPath_ReturnsValidationError_WhenPathNotAbsolute()
    {
        // Arrange
        var (client, _) = MockHttpClientFactory.CreateMockClient();

        // Act
        var response = await AssetTools.UploadFromPath(client, "relative/photo.jpg");

        // Assert
        using var json = JsonDocument.Parse(response);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION");
    }

    [Fact]
    public async Task UploadFromPath_ReturnsNotFoundError_WhenFileDoesNotExist()
    {
        // Arrange
        var (client, _) = MockHttpClientFactory.CreateMockClient();
        var missingPath = Path.Combine(Path.GetTempPath(), $"immich-mcp-test-{Guid.NewGuid()}.jpg");

        // Act
        var response = await AssetTools.UploadFromPath(client, missingPath);

        // Assert
        using var json = JsonDocument.Parse(response);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task AuthorizeUpload_ReturnsValidationError_WhenBothAlbumNameAndAlbumIdProvided()
    {
        // Arrange
        var (client, _) = MockHttpClientFactory.CreateMockClient();

        // Act
        var response = await AssetTools.AuthorizeUpload(client, albumName: "New Album", albumId: "album-1");

        // Assert
        using var json = JsonDocument.Parse(response);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION");
    }

    [Fact]
    public async Task AuthorizeUpload_ReturnsValidationError_WhenNeitherAlbumNameNorAlbumIdProvided()
    {
        // Arrange
        var (client, _) = MockHttpClientFactory.CreateMockClient();

        // Act
        var response = await AssetTools.AuthorizeUpload(client, albumName: null, albumId: null);

        // Assert
        using var json = JsonDocument.Parse(response);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION");
    }

    [Fact]
    public async Task AuthorizeUpload_ReturnsUploadUrl_WhenCreatingNewAlbum()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var album = TestFixtures.CreateAlbum(id: "album-1", albumName: "New Album");
        const string linkJson = """
            {
              "id": "link-1",
              "key": "upload-key",
              "type": "ALBUM",
              "allowUpload": true,
              "allowDownload": false,
              "showMetadata": true
            }
            """;

        handler.When(HttpMethod.Post, "*/albums")
            .Respond("application/json", TestFixtures.ToJson(album));

        string? sharedLinkRequestBody = null;
        handler.When(HttpMethod.Post, "*/shared-links")
            .With(req =>
            {
                sharedLinkRequestBody = req.Content!.ReadAsStringAsync().Result;
                return true;
            })
            .Respond("application/json", linkJson);

        // Act
        var response = await AssetTools.AuthorizeUpload(client, albumName: "New Album");

        // Assert
        using var json = JsonDocument.Parse(response);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        var result = json.RootElement.GetProperty("result");
        result.GetProperty("album_id").GetString().Should().Be("album-1");
        result.GetProperty("album_name").GetString().Should().Be("New Album");
        result.GetProperty("shared_link_id").GetString().Should().Be("link-1");
        result.GetProperty("upload_url").GetString().Should().Contain("key=upload-key");

        // The link must be upload-only: allowing download would let anyone with the URL read the album back.
        sharedLinkRequestBody.Should().NotBeNull();
        sharedLinkRequestBody.Should().Contain("\"allowUpload\":true");
        sharedLinkRequestBody.Should().Contain("\"allowDownload\":false");
        sharedLinkRequestBody.Should().Contain("\"albumId\":\"album-1\"");
        sharedLinkRequestBody.Should().Contain("\"expiresAt\"");
        sharedLinkRequestBody.Should().NotContain("\"expiresAt\":null");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100_000)]
    public async Task AuthorizeUpload_ClampsTtlMinutes_ToOneAndFourteenForty(int ttlMinutes)
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var album = TestFixtures.CreateAlbum(id: "album-1", albumName: "New Album");
        const string linkJson = """
            {
              "id": "link-1",
              "key": "upload-key",
              "type": "ALBUM",
              "allowUpload": true,
              "allowDownload": false
            }
            """;

        handler.When(HttpMethod.Post, "*/albums")
            .Respond("application/json", TestFixtures.ToJson(album));

        string? sharedLinkRequestBody = null;
        handler.When(HttpMethod.Post, "*/shared-links")
            .With(req =>
            {
                sharedLinkRequestBody = req.Content!.ReadAsStringAsync().Result;
                return true;
            })
            .Respond("application/json", linkJson);

        var before = DateTime.UtcNow;

        // Act
        await AssetTools.AuthorizeUpload(client, albumName: "New Album", ttlMinutes: ttlMinutes);

        // Assert: ttlMinutes is clamped to [1, 1440] regardless of how far out of range the input is.
        var expiresAt = JsonDocument.Parse(sharedLinkRequestBody!).RootElement.GetProperty("expiresAt").GetDateTime();
        var clampedTtl = Math.Clamp(ttlMinutes, 1, 1440);
        expiresAt.Should().BeCloseTo(before.AddMinutes(clampedTtl), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task AuthorizeUpload_ReturnsNotFoundError_WhenExistingAlbumIdMissing()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();

        handler.When(HttpMethod.Get, "*/albums/missing-album")
            .Respond(HttpStatusCode.NotFound);

        // Act
        var response = await AssetTools.AuthorizeUpload(client, albumId: "missing-album");

        // Assert
        using var json = JsonDocument.Parse(response);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Update_ReturnsUpdatedAsset_WhenSuccessful()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var asset = TestFixtures.CreateAsset(id: "asset-1", isFavorite: true);

        handler.When(HttpMethod.Put, "*/assets/asset-1")
            .Respond("application/json", TestFixtures.ToJson(asset));

        // Act
        var response = await AssetTools.Update(client, "asset-1", isFavorite: true);

        // Assert
        using var json = JsonDocument.Parse(response);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        var result = json.RootElement.GetProperty("result");
        result.GetProperty("id").GetString().Should().Be("asset-1");
        result.GetProperty("isFavorite").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Update_Throws_WhenUpstreamReturnsNotFound()
    {
        // Arrange: PUT surfaces non-success responses as exceptions (unlike GET, which
        // treats 404 as a legitimate "not found" and returns null), see ImmichClient.PutAsync.
        var (client, handler) = MockHttpClientFactory.CreateMockClient();

        handler.When(HttpMethod.Put, "*/assets/missing-asset")
            .Respond(HttpStatusCode.NotFound);

        // Act
        var act = () => AssetTools.Update(client, "missing-asset", isFavorite: true);

        // Assert
        await act.Should().ThrowAsync<ImmichApiException>();
    }

    [Fact]
    public async Task BulkUpdate_ReturnsValidationError_WhenAssetIdsEmpty()
    {
        // Arrange
        var (client, _) = MockHttpClientFactory.CreateMockClient();

        // Act
        var response = await AssetTools.BulkUpdate(client, "  ");

        // Assert
        using var json = JsonDocument.Parse(response);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION");
    }

    [Fact]
    public async Task BulkUpdate_DoesNotCallUpstream_WhenDryRunDefaultAndNotConfirmed()
    {
        // Arrange: default dryRun=true, confirm=false, the safety-critical default path.
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var bulkUpdateRequest = handler.When(HttpMethod.Put, "*/assets")
            .Respond(HttpStatusCode.OK);

        // Act
        var response = await AssetTools.BulkUpdate(client, "asset-1,asset-2", isFavorite: true);

        // Assert: dry-run envelope, not executed, and the mutating endpoint was never called.
        using var json = JsonDocument.Parse(response);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        var result = json.RootElement.GetProperty("result");
        result.GetProperty("executed").GetBoolean().Should().BeFalse();
        result.GetProperty("affected_ids").GetArrayLength().Should().Be(2);
        result.GetProperty("warnings").EnumerateArray().First().GetString().Should().Contain("dry run");

        handler.GetMatchCount(bulkUpdateRequest).Should().Be(0);
    }

    [Fact]
    public async Task BulkUpdate_DoesNotCallUpstream_WhenDryRunFalseButNotConfirmed()
    {
        // Arrange: confirm=false alone must still block execution even if dryRun is explicitly false.
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var bulkUpdateRequest = handler.When(HttpMethod.Put, "*/assets")
            .Respond(HttpStatusCode.OK);

        // Act
        var response = await AssetTools.BulkUpdate(client, "asset-1", isFavorite: true, dryRun: false, confirm: false);

        // Assert
        using var json = JsonDocument.Parse(response);
        json.RootElement.GetProperty("result").GetProperty("executed").GetBoolean().Should().BeFalse();
        handler.GetMatchCount(bulkUpdateRequest).Should().Be(0);
    }

    [Fact]
    public async Task BulkUpdate_DoesNotCallUpstream_WhenConfirmedButDryRunTrue()
    {
        // Arrange: dryRun defaults to true, so passing confirm=true alone must not be enough to execute.
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var bulkUpdateRequest = handler.When(HttpMethod.Put, "*/assets")
            .Respond(HttpStatusCode.OK);

        // Act
        var response = await AssetTools.BulkUpdate(client, "asset-1", isFavorite: true, confirm: true);

        // Assert
        using var json = JsonDocument.Parse(response);
        json.RootElement.GetProperty("result").GetProperty("executed").GetBoolean().Should().BeFalse();
        handler.GetMatchCount(bulkUpdateRequest).Should().Be(0);
    }

    [Fact]
    public async Task BulkUpdate_CallsUpstream_WhenDryRunFalseAndConfirmed()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        string? capturedRequestBody = null;
        var bulkUpdateRequest = handler.When(HttpMethod.Put, "*/assets")
            .With(req =>
            {
                capturedRequestBody = req.Content!.ReadAsStringAsync().Result;
                return true;
            })
            .Respond(HttpStatusCode.NoContent);

        // Act
        var response = await AssetTools.BulkUpdate(client, "asset-1,asset-2", isFavorite: true, dryRun: false, confirm: true);

        // Assert
        using var json = JsonDocument.Parse(response);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        var result = json.RootElement.GetProperty("result");
        result.GetProperty("executed").GetBoolean().Should().BeTrue();
        result.GetProperty("affected_ids").GetArrayLength().Should().Be(2);

        handler.GetMatchCount(bulkUpdateRequest).Should().Be(1);

        // The upstream request must carry exactly the parsed IDs, not e.g. an empty or unrelated set.
        capturedRequestBody.Should().NotBeNull();
        capturedRequestBody.Should().Contain("\"ids\":[\"asset-1\",\"asset-2\"]");
        capturedRequestBody.Should().Contain("\"isFavorite\":true");
    }

    [Fact]
    public async Task DeleteAssets_ReturnsValidationError_WhenAssetIdsEmpty()
    {
        // Arrange
        var (client, _) = MockHttpClientFactory.CreateMockClient();

        // Act
        var response = await AssetTools.DeleteAssets(client, " ");

        // Assert
        using var json = JsonDocument.Parse(response);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION");
    }

    [Fact]
    public async Task DeleteAssets_DoesNotCallUpstreamDelete_WhenNotConfirmed()
    {
        // Arrange: confirm defaults to false, the safety-critical default path.
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var asset = TestFixtures.CreateAsset(id: "asset-1", originalFileName: "photo.jpg");

        handler.When(HttpMethod.Get, "*/assets/asset-1")
            .Respond("application/json", TestFixtures.ToJson(asset));

        var deleteRequest = handler.When(HttpMethod.Delete, "*/assets")
            .Respond(HttpStatusCode.NoContent);

        // Act
        var response = await AssetTools.DeleteAssets(client, "asset-1");

        // Assert: confirmation-required envelope with a preview, and delete never invoked.
        using var json = JsonDocument.Parse(response);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("CONFIRMATION_REQUIRED");
        var details = json.RootElement.GetProperty("error").GetProperty("details");
        details.GetProperty("asset_count").GetInt32().Should().Be(1);
        details.GetProperty("preview").EnumerateArray().First().GetProperty("id").GetString().Should().Be("asset-1");

        handler.GetMatchCount(deleteRequest).Should().Be(0);
    }

    [Fact]
    public async Task DeleteAssets_DoesNotCallUpstreamDelete_WhenDryRunFalseButNotConfirmed()
    {
        // Arrange: confirm=false alone must still block deletion even if dryRun is explicitly false.
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var asset = TestFixtures.CreateAsset(id: "asset-1", originalFileName: "photo.jpg");

        handler.When(HttpMethod.Get, "*/assets/asset-1")
            .Respond("application/json", TestFixtures.ToJson(asset));

        var deleteRequest = handler.When(HttpMethod.Delete, "*/assets")
            .Respond(HttpStatusCode.NoContent);

        // Act
        var response = await AssetTools.DeleteAssets(client, "asset-1", confirm: false, dryRun: false);

        // Assert
        using var json = JsonDocument.Parse(response);
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("CONFIRMATION_REQUIRED");
        handler.GetMatchCount(deleteRequest).Should().Be(0);
    }

    [Fact]
    public async Task DeleteAssets_DoesNotCallUpstreamDelete_WhenStaleDryRunTrue()
    {
        // Arrange: confirm=true alone DOES delete. But a caller written against the old
        // two-switch contract that still sends dryRun=true must keep getting a preview
        // rather than a surprise deletion, so the deprecated argument wins over confirm.
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var asset = TestFixtures.CreateAsset(id: "asset-1", originalFileName: "photo.jpg");

        handler.When(HttpMethod.Get, "*/assets/asset-1")
            .Respond("application/json", TestFixtures.ToJson(asset));

        var deleteRequest = handler.When(HttpMethod.Delete, "*/assets")
            .Respond(HttpStatusCode.NoContent);

        // Act
        var response = await AssetTools.DeleteAssets(client, "asset-1", confirm: true, dryRun: true);

        // Assert
        using var json = JsonDocument.Parse(response);
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("CONFIRMATION_REQUIRED");
        handler.GetMatchCount(deleteRequest).Should().Be(0);
    }

    [Fact]
    public async Task DeleteAssets_CallsUpstreamDelete_WhenConfirmed()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        string? capturedRequestBody = null;
        var deleteRequest = handler.When(HttpMethod.Delete, "*/assets")
            .With(req =>
            {
                capturedRequestBody = req.Content!.ReadAsStringAsync().Result;
                return true;
            })
            .Respond(HttpStatusCode.NoContent);

        // Act
        var response = await AssetTools.DeleteAssets(client, "asset-1,asset-2", confirm: true);

        // Assert
        using var json = JsonDocument.Parse(response);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        var result = json.RootElement.GetProperty("result");
        result.GetProperty("deleted").GetBoolean().Should().BeTrue();
        result.GetProperty("asset_count").GetInt32().Should().Be(2);

        handler.GetMatchCount(deleteRequest).Should().Be(1);

        // The upstream request must carry exactly the parsed IDs and force must default to false
        // (i.e. go through trash), never bypass it unless the caller asked for it.
        capturedRequestBody.Should().NotBeNull();
        capturedRequestBody.Should().Contain("\"ids\":[\"asset-1\",\"asset-2\"]");
        capturedRequestBody.Should().Contain("\"force\":false");
    }

    [Fact]
    public async Task DeleteAssets_PassesForceFlag_WhenSet()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        string? capturedRequestBody = null;
        var deleteRequest = handler.When(HttpMethod.Delete, "*/assets")
            .With(req =>
            {
                capturedRequestBody = req.Content!.ReadAsStringAsync().Result;
                return true;
            })
            .Respond(HttpStatusCode.NoContent);

        // Act
        var response = await AssetTools.DeleteAssets(client, "asset-1", force: true, confirm: true);

        // Assert
        using var json = JsonDocument.Parse(response);
        json.RootElement.GetProperty("result").GetProperty("force").GetBoolean().Should().BeTrue();
        capturedRequestBody.Should().Contain("\"force\":true");
    }

    [Fact]
    public async Task Delete_LegacyPositionalDryRun_DoesNotDelete()
    {
        // The obsolete Delete overload keeps the old positional argument order, so an existing
        // caller passing (ids, force, dryRun) is not silently reinterpreted as confirming.
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var asset = TestFixtures.CreateAsset(id: "asset-1");
        handler.Expect(HttpMethod.Get, "*/assets/asset-1")
            .Respond("application/json", TestFixtures.ToJson(asset));

#pragma warning disable CS0618
        using var result = JsonDocument.Parse(
            await AssetTools.Delete(client, "asset-1", true, true));
#pragma warning restore CS0618

        result.RootElement.GetProperty("error").GetProperty("code")
            .GetString().Should().Be("CONFIRMATION_REQUIRED");
        handler.VerifyNoOutstandingExpectation();
    }
}
