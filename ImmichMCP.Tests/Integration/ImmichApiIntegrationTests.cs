using FluentAssertions;
using ImmichMCP.Models.Search;

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
