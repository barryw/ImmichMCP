using System.Net;
using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using RichardSzalay.MockHttp;
using ImmichMCP.Client;
using ImmichMCP.Tests.Fixtures;
using ImmichMCP.Tools;

namespace ImmichMCP.Tests.Tools;

public class AssetDownloadToolTests
{
    private const string AssetId = "asset-1";

    private static void MockAssetGet(MockHttpMessageHandler handler, string originalFileName = "photo.jpg", string type = "IMAGE")
    {
        var asset = TestFixtures.CreateAsset(id: AssetId, type: type, originalFileName: originalFileName);
        handler.When(HttpMethod.Get, $"*/assets/{AssetId}")
            .Respond("application/json", TestFixtures.ToJson(asset));
    }

    private static JsonDocument ParseEnvelope(CallToolResult result) =>
        JsonDocument.Parse(result.Content.OfType<TextContentBlock>().First().Text);

    private static byte[] DecodeBase64Payload(IEnumerable<byte> payload) =>
        Convert.FromBase64String(System.Text.Encoding.ASCII.GetString(payload.ToArray()));

    [Fact]
    public async Task DownloadThumbnail_ReturnsInlineImage_WhenBase64Mode()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient(downloadMode: "base64");
        MockAssetGet(handler);
        var imageBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x01, 0x02, 0x03 };
        handler.When(HttpMethod.Get, $"*/assets/{AssetId}/thumbnail")
            .WithQueryString("size", "preview")
            .Respond("image/jpeg", new MemoryStream(imageBytes));

        // Act
        var result = await AssetTools.DownloadThumbnail(client, AssetId);

        // Assert
        using var json = ParseEnvelope(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("result").GetProperty("encoding").GetString().Should().Be("base64");

        var image = result.Content.OfType<ImageContentBlock>().Should().ContainSingle().Subject;
        image.MimeType.Should().Be("image/jpeg");
        DecodeBase64Payload(image.Data.ToArray()).Should().Equal(imageBytes);
    }

    [Fact]
    public async Task DownloadOriginal_ReturnsEmbeddedBlob_WhenBase64ModeAndNonImage()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient(downloadMode: "base64");
        MockAssetGet(handler, originalFileName: "clip.mp4", type: "VIDEO");
        var videoBytes = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 };
        handler.When(HttpMethod.Get, $"*/assets/{AssetId}/original")
            .Respond("video/mp4", new MemoryStream(videoBytes));

        // Act
        var result = await AssetTools.DownloadOriginal(client, AssetId);

        // Assert
        using var json = ParseEnvelope(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("result").GetProperty("mime_type").GetString().Should().Be("video/mp4");

        result.Content.OfType<ImageContentBlock>().Should().BeEmpty();
        var resource = result.Content.OfType<EmbeddedResourceBlock>().Should().ContainSingle().Subject;
        var blob = resource.Resource.Should().BeOfType<BlobResourceContents>().Subject;
        blob.MimeType.Should().Be("video/mp4");
        DecodeBase64Payload(blob.Blob.ToArray()).Should().Equal(videoBytes);
    }

    [Fact]
    public async Task DownloadOriginal_ReturnsUrlJsonOnly_WhenUrlMode()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        MockAssetGet(handler);

        // Act
        var result = await AssetTools.DownloadOriginal(client, AssetId);

        // Assert: URL mode behavior is unchanged — one text block, no binary content.
        result.Content.Should().ContainSingle().Which.Should().BeOfType<TextContentBlock>();
        using var json = ParseEnvelope(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("result").GetProperty("original_url").GetString()
            .Should().Be($"https://photos.example.com/api/assets/{AssetId}/original");
    }

    [Fact]
    public async Task DownloadThumbnail_ReturnsUrlJsonOnly_WhenUrlMode()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        MockAssetGet(handler);

        // Act
        var result = await AssetTools.DownloadThumbnail(client, AssetId);

        // Assert
        result.Content.Should().ContainSingle().Which.Should().BeOfType<TextContentBlock>();
        using var json = ParseEnvelope(result);
        json.RootElement.GetProperty("result").GetProperty("preview_url").GetString()
            .Should().Be($"https://photos.example.com/api/assets/{AssetId}/thumbnail?size=preview");
    }

    [Fact]
    public async Task DownloadOriginal_Throws_WhenUpstreamFails()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient(downloadMode: "base64");
        MockAssetGet(handler);
        handler.When(HttpMethod.Get, $"*/assets/{AssetId}/original")
            .Respond(HttpStatusCode.InternalServerError);

        // Act
        var act = () => AssetTools.DownloadOriginal(client, AssetId);

        // Assert
        await act.Should().ThrowAsync<ImmichApiException>();
    }

    [Fact]
    public async Task DownloadOriginal_ReturnsPayloadTooLargeError_WhenContentLengthExceedsCap()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient(downloadMode: "base64", maxInlineDownloadBytes: 4);
        MockAssetGet(handler);
        handler.When(HttpMethod.Get, $"*/assets/{AssetId}/original")
            .Respond("image/jpeg", new MemoryStream(new byte[64]));

        // Act
        var result = await AssetTools.DownloadOriginal(client, AssetId);

        // Assert: clear error instead of inline content, pointing at the URL fallback.
        result.Content.Should().ContainSingle().Which.Should().BeOfType<TextContentBlock>();
        using var json = ParseEnvelope(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        var error = json.RootElement.GetProperty("error");
        error.GetProperty("code").GetString().Should().Be("PAYLOAD_TOO_LARGE");
        error.GetProperty("details").GetProperty("max_inline_download_bytes").GetInt64().Should().Be(4);
        error.GetProperty("details").GetProperty("download_url").GetString()
            .Should().Be($"https://photos.example.com/api/assets/{AssetId}/original");
    }

    [Fact]
    public async Task DownloadThumbnail_ReturnsPayloadTooLargeError_WhenBodyExceedsCapWithoutContentLength()
    {
        // Arrange: chunked-style response (no Content-Length), so the cap must be
        // enforced by the bounded read rather than the header check.
        var (client, handler) = MockHttpClientFactory.CreateMockClient(downloadMode: "base64", maxInlineDownloadBytes: 4);
        MockAssetGet(handler);
        handler.When(HttpMethod.Get, $"*/assets/{AssetId}/thumbnail")
            .WithQueryString("size", "preview")
            .Respond(_ =>
            {
                var content = new ByteArrayContent(new byte[64]);
                content.Headers.ContentLength = null;
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            });

        // Act
        var result = await AssetTools.DownloadThumbnail(client, AssetId);

        // Assert
        result.Content.Should().ContainSingle().Which.Should().BeOfType<TextContentBlock>();
        using var json = ParseEnvelope(result);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("PAYLOAD_TOO_LARGE");
    }

    [Fact]
    public async Task DownloadOriginal_Throws_WhenAlreadyCanceled()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient(downloadMode: "base64");
        MockAssetGet(handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var act = () => AssetTools.DownloadOriginal(client, AssetId, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task DownloadThumbnail_Throws_WhenUpstreamFails()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient(downloadMode: "base64");
        MockAssetGet(handler);
        handler.When(HttpMethod.Get, $"*/assets/{AssetId}/thumbnail")
            .WithQueryString("size", "preview")
            .Respond(HttpStatusCode.InternalServerError);

        // Act
        var act = () => AssetTools.DownloadThumbnail(client, AssetId);

        // Assert
        await act.Should().ThrowAsync<ImmichApiException>();
    }

    [Fact]
    public async Task DownloadThumbnail_Throws_WhenAlreadyCanceled()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient(downloadMode: "base64");
        MockAssetGet(handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var act = () => AssetTools.DownloadThumbnail(client, AssetId, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
