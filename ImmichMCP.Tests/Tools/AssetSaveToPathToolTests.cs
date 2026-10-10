using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using RichardSzalay.MockHttp;
using ImmichMCP.Tests.Fixtures;
using ImmichMCP.Tools;

namespace ImmichMCP.Tests.Tools;

public class AssetSaveToPathToolTests : IDisposable
{
    private const string Id1 = "11111111-aaaa-4bbb-8ccc-000000000001";
    private const string Id2 = "22222222-aaaa-4bbb-8ccc-000000000002";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"immich-save-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string Dir(string name = "out") => Path.Combine(_root, name);

    private static string Sha1(byte[] bytes) => Convert.ToBase64String(SHA1.HashData(bytes));

    private static Func<HttpRequestMessage, HttpResponseMessage> Serve(byte[] bytes, string mimeType) => _ =>
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    };

    private static void MockAsset(MockHttpMessageHandler handler, string id, string originalFileName, string checksum = "abc123")
    {
        var asset = TestFixtures.CreateAsset(id: id, originalFileName: originalFileName, checksum: checksum);
        handler.When(HttpMethod.Get, $"*/assets/{id}").Respond("application/json", TestFixtures.ToJson(asset));
    }

    private static MockedRequest MockOriginal(MockHttpMessageHandler handler, string id, byte[] bytes, string mimeType = "image/heic") =>
        handler.When(HttpMethod.Get, $"*/assets/{id}/original").Respond(Serve(bytes, mimeType));

    private static JsonElement Root(string json) => JsonDocument.Parse(json).RootElement;

    private static JsonElement File(JsonElement root, int index) =>
        root.GetProperty("result").GetProperty("files")[index];

    [Fact]
    public async Task SaveToPath_SavesOriginalUnderOriginalName_AndVerifiesChecksum()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        MockAsset(handler, Id1, "IMG_4229.HEIC", Sha1(bytes));
        MockOriginal(handler, Id1, bytes);

        // Act
        var root = Root(await AssetTools.SaveToPath(client, Id1, Dir()));

        // Assert
        root.GetProperty("ok").GetBoolean().Should().BeTrue();
        root.GetProperty("result").GetProperty("saved").GetInt32().Should().Be(1);
        var file = File(root, 0);
        file.GetProperty("status").GetString().Should().Be("saved");
        file.GetProperty("file").GetString().Should().Be("IMG_4229.HEIC");
        file.GetProperty("bytes").GetInt64().Should().Be(bytes.Length);
        file.GetProperty("checksum_verified").GetBoolean().Should().BeTrue();
        (await System.IO.File.ReadAllBytesAsync(Path.Combine(Dir(), "IMG_4229.HEIC"))).Should().Equal(bytes);
        Directory.GetFiles(Dir(), "*.part").Should().BeEmpty();
    }

    [Fact]
    public async Task SaveToPath_SkipsIdenticalExistingFile_WithoutDownloading()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var bytes = new byte[] { 9, 8, 7 };
        MockAsset(handler, Id1, "IMG_1.HEIC", Sha1(bytes));
        var original = MockOriginal(handler, Id1, bytes);
        Directory.CreateDirectory(Dir());
        await System.IO.File.WriteAllBytesAsync(Path.Combine(Dir(), "IMG_1.HEIC"), bytes);

        // Act
        var root = Root(await AssetTools.SaveToPath(client, Id1, Dir()));

        // Assert
        root.GetProperty("ok").GetBoolean().Should().BeTrue();
        root.GetProperty("result").GetProperty("existing").GetInt32().Should().Be(1);
        File(root, 0).GetProperty("status").GetString().Should().Be("exists");
        handler.GetMatchCount(original).Should().Be(0);
    }

    [Fact]
    public async Task SaveToPath_UsesIdSuffix_WhenADifferentFileAlreadyHasTheName()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var bytes = new byte[] { 1, 1, 1 };
        MockAsset(handler, Id1, "IMG_1.HEIC", Sha1(bytes));
        MockOriginal(handler, Id1, bytes);
        Directory.CreateDirectory(Dir());
        var foreign = Path.Combine(Dir(), "IMG_1.HEIC");
        await System.IO.File.WriteAllBytesAsync(foreign, new byte[] { 42 });

        // Act
        var root = Root(await AssetTools.SaveToPath(client, Id1, Dir()));

        // Assert: the unrelated file is untouched, the asset lands next to it
        File(root, 0).GetProperty("file").GetString().Should().Be("IMG_1_11111111.HEIC");
        (await System.IO.File.ReadAllBytesAsync(foreign)).Should().Equal(new byte[] { 42 });
        (await System.IO.File.ReadAllBytesAsync(Path.Combine(Dir(), "IMG_1_11111111.HEIC"))).Should().Equal(bytes);
    }

    [Fact]
    public async Task SaveToPath_GivesAssetsSharingAFileNameInOneBatch_DistinctFiles()
    {
        // Arrange: two phones produced IMG_4229.HEIC
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var first = new byte[] { 1 };
        var second = new byte[] { 2, 2 };
        MockAsset(handler, Id1, "IMG_4229.HEIC", Sha1(first));
        MockAsset(handler, Id2, "IMG_4229.HEIC", Sha1(second));
        MockOriginal(handler, Id1, first);
        MockOriginal(handler, Id2, second);

        // Act
        var root = Root(await AssetTools.SaveToPath(client, $"{Id1},{Id2}", Dir()));

        // Assert
        root.GetProperty("result").GetProperty("saved").GetInt32().Should().Be(2);
        File(root, 0).GetProperty("file").GetString().Should().Be("IMG_4229.HEIC");
        File(root, 1).GetProperty("file").GetString().Should().Be("IMG_4229_22222222.HEIC");
        (await System.IO.File.ReadAllBytesAsync(Path.Combine(Dir(), "IMG_4229.HEIC"))).Should().Equal(first);
        (await System.IO.File.ReadAllBytesAsync(Path.Combine(Dir(), "IMG_4229_22222222.HEIC"))).Should().Equal(second);
    }

    [Fact]
    public async Task SaveToPath_SavesPreview_WithIdAndSizeInName_AndSkipsItOnRerun()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        MockAsset(handler, Id1, "IMG_4229.HEIC");
        var preview = handler.When(HttpMethod.Get, $"*/assets/{Id1}/thumbnail")
            .WithQueryString("size", "preview")
            .Respond(Serve(jpeg, "image/jpeg"));

        // Act
        var first = Root(await AssetTools.SaveToPath(client, Id1, Dir(), size: "preview"));
        var second = Root(await AssetTools.SaveToPath(client, Id1, Dir(), size: "preview"));

        // Assert
        var file = File(first, 0);
        file.GetProperty("file").GetString().Should().Be("IMG_4229_11111111_preview.jpg");
        file.TryGetProperty("checksum_verified", out _).Should().BeFalse("rendered images have no Immich checksum");
        (await System.IO.File.ReadAllBytesAsync(Path.Combine(Dir(), "IMG_4229_11111111_preview.jpg"))).Should().Equal(jpeg);
        File(second, 0).GetProperty("status").GetString().Should().Be("exists");
        handler.GetMatchCount(preview).Should().Be(1);
    }

    [Fact]
    public async Task SaveToPath_ReportsChecksumMismatch()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        MockAsset(handler, Id1, "IMG_1.HEIC", checksum: Sha1(new byte[] { 0 }));
        MockOriginal(handler, Id1, new byte[] { 1, 2 });

        // Act
        var root = Root(await AssetTools.SaveToPath(client, Id1, Dir()));

        // Assert
        File(root, 0).GetProperty("status").GetString().Should().Be("saved");
        File(root, 0).GetProperty("checksum_verified").GetBoolean().Should().BeFalse();
    }

    [Theory]
    [InlineData("../../evil.sh", "evil.sh")]
    [InlineData("..\\..\\evil.sh", "evil.sh")]
    [InlineData("..", Id1)]
    public async Task SaveToPath_KeepsCraftedFileNamesInsideTheDirectory(string originalFileName, string expected)
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        MockAsset(handler, Id1, originalFileName);
        MockOriginal(handler, Id1, new byte[] { 1 }, "application/octet-stream");

        // Act
        var root = Root(await AssetTools.SaveToPath(client, Id1, Dir()));

        // Assert
        File(root, 0).GetProperty("file").GetString().Should().Be(expected);
        System.IO.File.Exists(Path.Combine(Dir(), expected)).Should().BeTrue();
        Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Should().ContainSingle();
    }

    [Fact]
    public async Task SaveToPath_LeavesNoPartialFile_WhenTheDownloadFails()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        MockAsset(handler, Id1, "IMG_1.HEIC");
        handler.When(HttpMethod.Get, $"*/assets/{Id1}/original").Respond(HttpStatusCode.InternalServerError);

        // Act
        var root = Root(await AssetTools.SaveToPath(client, Id1, Dir()));

        // Assert: nothing saved at all is reported as an error, with the per-file details
        root.GetProperty("ok").GetBoolean().Should().BeFalse();
        var error = root.GetProperty("error");
        error.GetProperty("code").GetString().Should().Be("UPSTREAM_ERROR");
        error.GetProperty("details").GetProperty("files")[0].GetProperty("status").GetString().Should().Be("failed");
        Directory.GetFiles(Dir()).Should().BeEmpty();
    }

    [Fact]
    public async Task SaveToPath_ReportsNotFound_WhenNoAssetExists()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        handler.When(HttpMethod.Get, $"*/assets/{Id1}").Respond(HttpStatusCode.NotFound);

        // Act
        var root = Root(await AssetTools.SaveToPath(client, Id1, Dir()));

        // Assert
        root.GetProperty("error").GetProperty("code").GetString().Should().Be("NOT_FOUND");
        root.GetProperty("error").GetProperty("details").GetProperty("files")[0].GetProperty("status").GetString()
            .Should().Be("not_found");
    }

    [Theory]
    [InlineData("relative/dir", "original")]
    [InlineData("", "original")]
    public async Task SaveToPath_RejectsNonAbsoluteDirectories(string directory, string size)
    {
        var (client, _) = MockHttpClientFactory.CreateMockClient();

        var root = Root(await AssetTools.SaveToPath(client, Id1, directory, size));

        root.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION");
    }

    [Fact]
    public async Task SaveToPath_IsRejected_WhenSavingIsDisabled()
    {
        var (client, _) = MockHttpClientFactory.CreateMockClient(saveToPathEnabled: false);

        var root = Root(await AssetTools.SaveToPath(client, Id1, Dir()));

        root.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION");
        Directory.Exists(Dir()).Should().BeFalse();
    }

    [Fact]
    public async Task SaveToPath_RejectsUnknownSize()
    {
        var (client, _) = MockHttpClientFactory.CreateMockClient();

        var root = Root(await AssetTools.SaveToPath(client, Id1, Dir(), size: "huge"));

        root.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION");
        Directory.Exists(Dir()).Should().BeFalse();
    }

    [Fact]
    public async Task SaveToPath_HonoursSaveRootDirectory()
    {
        // Arrange
        var saveRoot = Path.Combine(_root, "allowed");
        var (client, handler) = MockHttpClientFactory.CreateMockClient(saveRootDirectory: saveRoot);
        MockAsset(handler, Id1, "IMG_1.HEIC");
        MockOriginal(handler, Id1, new byte[] { 1 });

        // Act
        var outside = Root(await AssetTools.SaveToPath(client, Id1, Path.Combine(saveRoot, "..", "elsewhere")));
        var sibling = Root(await AssetTools.SaveToPath(client, Id1, saveRoot + "-evil"));
        var inside = Root(await AssetTools.SaveToPath(client, Id1, Path.Combine(saveRoot, "trip")));

        // Assert
        outside.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION");
        sibling.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION");
        inside.GetProperty("ok").GetBoolean().Should().BeTrue();
        System.IO.File.Exists(Path.Combine(saveRoot, "trip", "IMG_1.HEIC")).Should().BeTrue();
        Directory.Exists(Path.Combine(_root, "elsewhere")).Should().BeFalse();
    }
}
