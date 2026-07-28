using System.Net;
using System.Text.Json;
using FluentAssertions;
using RichardSzalay.MockHttp;
using ImmichMCP.Tests.Fixtures;
using ImmichMCP.Tools;

namespace ImmichMCP.Tests.Tools;

public class AssetToolsTests
{
    [Fact]
    public async Task DeleteAssets_Executes_WhenConfirmed()
    {
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        handler.Expect(HttpMethod.Delete, "*/assets")
            .Respond(HttpStatusCode.NoContent);

        using var result = JsonDocument.Parse(
            await AssetTools.DeleteAssets(client, "asset-1", confirm: true));

        result.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        result.RootElement.GetProperty("result").GetProperty("deleted")
            .GetBoolean().Should().BeTrue();
        handler.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task Delete_LegacyPositionalDryRun_DoesNotDelete()
    {
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
