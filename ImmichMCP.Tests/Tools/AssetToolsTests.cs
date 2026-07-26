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
    public async Task Delete_Executes_WhenConfirmed()
    {
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        handler.Expect(HttpMethod.Delete, "*/assets")
            .Respond(HttpStatusCode.NoContent);

        using var result = JsonDocument.Parse(
            await AssetTools.Delete(client, "asset-1", confirm: true));

        result.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        result.RootElement.GetProperty("result").GetProperty("deleted")
            .GetBoolean().Should().BeTrue();
        handler.VerifyNoOutstandingExpectation();
    }
}
