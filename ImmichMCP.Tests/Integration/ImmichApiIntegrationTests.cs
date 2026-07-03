using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using ImmichMCP.Models.Search;
using ImmichMCP.Tools;

namespace ImmichMCP.Tests.Integration;

[Trait("Category", "Integration")]
public class ImmichApiIntegrationTests
{
    [IntegrationFact]
    public async Task Ping_ReturnsExpectedImmichMajorVersion()
    {
        var settings = IntegrationTestSettings.Load();
        var client = settings.CreateClient();

        var (success, info, error) = await client.PingAsync();

        success.Should().BeTrue(error);
        info.Should().NotBeNull();
        info!.Version.Should().NotBeNullOrWhiteSpace();

        if (settings.ExpectedMajorVersion.HasValue)
        {
            var normalizedVersion = info.Version.TrimStart('v').Split('-', 2)[0];
            Version.TryParse(normalizedVersion, out var version).Should().BeTrue($"server version was {info.Version}");
            version!.Major.Should().Be(settings.ExpectedMajorVersion.Value);
        }
    }

    [IntegrationFact]
    public async Task MetadataSearch_ReturnsV3AssetShape()
    {
        var settings = IntegrationTestSettings.Load();
        var client = settings.CreateClient();

        var result = await client.SearchMetadataAsync(new MetadataSearchRequest
        {
            Size = 3,
            WithExif = true
        });

        result.Should().NotBeNull();
        result.Items.Should().HaveCountLessThanOrEqualTo(3);

        var asset = result.Items.FirstOrDefault();
        if (asset == null)
        {
            return;
        }

        asset.Id.Should().NotBeNullOrWhiteSpace();
        asset.Type.Should().NotBeNullOrWhiteSpace();
        asset.Visibility.Should().NotBeNullOrWhiteSpace();
    }

    [IntegrationFact]
    public async Task MetadataSearch_WithTakenAfterFilter_ReturnsResults()
    {
        // Regression: Immich v3 validates datetime fields with Zod, rejecting datetimes
        // without a timezone designator (HTTP 400), which the client swallowed into an
        // empty result. This exercises the real takenAfter path end-to-end.
        var settings = IntegrationTestSettings.Load();
        var client = settings.CreateClient();

        // Find the newest asset to derive a date window that is guaranteed to contain data.
        var newest = await client.SearchMetadataAsync(new MetadataSearchRequest { Size = 1, Order = "desc" });
        var anchor = newest.Items.FirstOrDefault();
        if (anchor == null)
        {
            return; // empty library — nothing to assert
        }

        // A window starting just before the newest asset must return at least that asset.
        var withinWindow = await client.SearchMetadataAsync(new MetadataSearchRequest
        {
            TakenAfter = anchor.FileCreatedAt.AddDays(-1),
            Order = "desc"
        });
        withinWindow.Items.Should().NotBeEmpty("takenAfter must be accepted by Immich v3, not rejected into an empty result");

        // A window far in the future must return nothing — proving the filter is applied, not ignored.
        var future = await client.SearchMetadataAsync(new MetadataSearchRequest
        {
            TakenAfter = anchor.FileCreatedAt.AddYears(50)
        });
        future.Items.Should().BeEmpty("takenAfter must actually constrain results");
    }

    [IntegrationFact]
    public async Task GetAssetsAsync_UsesV3SearchBackedListing()
    {
        var settings = IntegrationTestSettings.Load();
        var client = settings.CreateClient();

        var assets = await client.GetAssetsAsync(size: 3, isArchived: false);

        assets.Should().NotBeNull();
        assets.Should().HaveCountLessThanOrEqualTo(3);
        assets.Should().OnlyContain(asset => !string.IsNullOrWhiteSpace(asset.Id));
    }

    [IntegrationFact]
    public async Task AlbumsCanBeListedWithV3Filters()
    {
        var settings = IntegrationTestSettings.Load();
        var client = settings.CreateClient();

        var albums = await client.GetAlbumsAsync(shared: null, isOwned: null);

        albums.Should().NotBeNull();

        var album = albums.FirstOrDefault();
        if (album == null)
        {
            return;
        }

        album.Id.Should().NotBeNullOrWhiteSpace();
        album.AlbumName.Should().NotBeNull();
    }

    [MutationIntegrationFact]
    public async Task UploadAndDeleteAsset_WhenMutationTestsAreEnabled()
    {
        var settings = IntegrationTestSettings.Load();
        var client = settings.CreateClient();
        var uploadedAssetId = string.Empty;

        try
        {
            var fileName = $"immichmcp-integration-{Guid.NewGuid():N}.png";
            var asset = await client.UploadAssetAsync(
                OnePixelPng,
                fileName,
                DateTime.UtcNow,
                isFavorite: false,
                isArchived: true);

            asset.Should().NotBeNull();
            uploadedAssetId = asset!.Id;
            uploadedAssetId.Should().NotBeNullOrWhiteSpace();
            asset.OriginalFileName.Should().Be(fileName);
            asset.Visibility.Should().Be("archive");
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(uploadedAssetId))
            {
                var deleted = await client.DeleteAssetsAsync([uploadedAssetId], force: true);
                deleted.Should().BeTrue("the integration upload should be removed from Immich");
            }
        }
    }

    [MutationIntegrationFact]
    public async Task AuthorizeUpload_MintsTokenThatUploadsIntoAlbum_WithoutApiKey()
    {
        var settings = IntegrationTestSettings.Load();
        var client = settings.CreateClient();

        string? albumId = null, linkId = null, assetId = null;
        try
        {
            // 1. The tool mints an album + upload-only shared-link URL using the master key.
            var json = await AssetTools.AuthorizeUpload(client, albumName: $"mcp-auth-upload-{Guid.NewGuid():N}");
            using var doc = JsonDocument.Parse(json);
            doc.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue(json);
            var result = doc.RootElement.GetProperty("result");
            var uploadUrl = result.GetProperty("upload_url").GetString()!;
            albumId = result.GetProperty("album_id").GetString();
            linkId = result.GetProperty("shared_link_id").GetString();
            uploadUrl.Should().Contain("?key=");

            // 2. Upload with ONLY the token — a bare client, no x-api-key header.
            using var http = new HttpClient();
            using var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(OnePixelPng);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(file, "assetData", "auth-upload.png");
            var ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            form.Add(new StringContent(ts), "fileCreatedAt");
            form.Add(new StringContent(ts), "fileModifiedAt");
            form.Add(new StringContent("mcp-test-device"), "deviceId");
            form.Add(new StringContent($"auth-upload-{Guid.NewGuid():N}"), "deviceAssetId");

            var resp = await http.PostAsync(uploadUrl, form);
            resp.IsSuccessStatusCode.Should().BeTrue($"token-only upload should succeed, got {(int)resp.StatusCode}");
            using var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            assetId = body.RootElement.GetProperty("id").GetString();
            body.RootElement.GetProperty("status").GetString().Should().BeOneOf("created", "duplicate");

            // 3. It landed in the album the tool created.
            var album = await client.GetAlbumAsync(albumId!);
            album!.AssetCount.Should().BeGreaterThanOrEqualTo(1);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(assetId))
                await client.DeleteAssetsAsync([assetId], force: true);
            if (!string.IsNullOrWhiteSpace(linkId))
                await client.DeleteSharedLinkAsync(linkId);
            if (!string.IsNullOrWhiteSpace(albumId))
                await client.DeleteAlbumAsync(albumId);
        }
    }

    private static readonly byte[] OnePixelPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82
    ];
}
