using System.Net;
using FluentAssertions;
using RichardSzalay.MockHttp;
using ImmichMCP.Client;
using ImmichMCP.Models.Search;
using ImmichMCP.Tests.Fixtures;
using static ImmichMCP.Utils.ParsingHelpers;

namespace ImmichMCP.Tests.Client;

public class ImmichClientSearchTests
{
    [Fact]
    public async Task SearchMetadataAsync_ReturnsResults_WhenSuccessful()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var searchResult = new
        {
            assets = new
            {
                total = 10,
                count = 2,
                items = new[]
                {
                    TestFixtures.CreateAsset(id: "asset-1"),
                    TestFixtures.CreateAsset(id: "asset-2")
                },
                nextPage = (string?)null
            }
        };

        handler.When(HttpMethod.Post, "*/search/metadata")
            .Respond("application/json", TestFixtures.ToJson(searchResult));

        // Act
        var result = await client.SearchMetadataAsync(new MetadataSearchRequest { Type = "IMAGE" });

        // Assert
        result.Should().NotBeNull();
        result.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task SearchMetadataAsync_ReturnsEmpty_WhenNoResults()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var searchResult = new
        {
            assets = new
            {
                total = 0,
                count = 0,
                items = Array.Empty<object>(),
                nextPage = (string?)null
            }
        };

        handler.When(HttpMethod.Post, "*/search/metadata")
            .Respond("application/json", TestFixtures.ToJson(searchResult));

        // Act
        var result = await client.SearchMetadataAsync(new MetadataSearchRequest());

        // Assert
        result.Should().NotBeNull();
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchMetadataAsync_SendsWithExifTrue_InRequestBody()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var searchResult = new
        {
            assets = new
            {
                total = 1,
                count = 1,
                items = new[] { TestFixtures.CreateAsset(id: "asset-1") },
                nextPage = (string?)null
            }
        };

        string? capturedRequestBody = null;
        handler.When(HttpMethod.Post, "*/search/metadata")
            .With(req =>
            {
                capturedRequestBody = req.Content!.ReadAsStringAsync().Result;
                return true;
            })
            .Respond("application/json", TestFixtures.ToJson(searchResult));

        var request = new MetadataSearchRequest { WithExif = true };

        // Act
        await client.SearchMetadataAsync(request);

        // Assert
        capturedRequestBody.Should().NotBeNull();
        capturedRequestBody.Should().Contain("\"withExif\":true");
    }

    [Fact]
    public async Task SearchMetadataAsync_SerializesDates_WithTimezoneSuffix_ForImmichV3()
    {
        // Immich v3 validates datetime fields with Zod (z.iso.datetime), which REQUIRES
        // a timezone designator (Z or +/-hh:mm). A bare "2026-06-02T00:00:00" is rejected
        // with HTTP 400, which the client would swallow into an empty result set.
        // See: POST /search/metadata returns 400 "expected ISO 8601 datetime string".
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var searchResult = new
        {
            assets = new { total = 0, count = 0, items = Array.Empty<object>(), nextPage = (string?)null }
        };

        string? capturedRequestBody = null;
        handler.When(HttpMethod.Post, "*/search/metadata")
            .With(req =>
            {
                capturedRequestBody = req.Content!.ReadAsStringAsync().Result;
                return true;
            })
            .Respond("application/json", TestFixtures.ToJson(searchResult));

        // ParseDate("2026-06-02") yields a Kind=Unspecified DateTime, which System.Text.Json
        // serializes with no timezone suffix unless a converter enforces one.
        var request = new MetadataSearchRequest
        {
            TakenAfter = ParseDate("2026-06-02"),
            TakenBefore = ParseDate("2026-07-02")
        };

        // Act
        await client.SearchMetadataAsync(request);

        // Assert: every serialized datetime must end in Z or an offset (Immich v3 requirement).
        capturedRequestBody.Should().NotBeNull();
        capturedRequestBody.Should().MatchRegex("\"takenAfter\":\"[^\"]+(Z|[+-]\\d\\d:\\d\\d)\"");
        capturedRequestBody.Should().MatchRegex("\"takenBefore\":\"[^\"]+(Z|[+-]\\d\\d:\\d\\d)\"");
    }

    [Fact]
    public async Task SearchMetadataAsync_Throws_OnUpstreamError_InsteadOfEmptyResult()
    {
        // The core bug: an upstream failure (e.g. a 400 from Immich v3 rejecting a filter)
        // must NOT be swallowed into a successful-looking empty result. It must surface.
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        handler.When(HttpMethod.Post, "*/search/metadata")
            .Respond(HttpStatusCode.BadRequest, "application/json",
                "{\"message\":\"Validation failed\",\"errors\":[{\"path\":[\"takenAfter\"]}]}");

        var act = () => client.SearchMetadataAsync(new MetadataSearchRequest());

        var ex = await act.Should().ThrowAsync<ImmichApiException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ex.Which.ResponseBody.Should().Contain("Validation failed");
    }

    [Fact]
    public async Task SmartSearchAsync_ReturnsResults_WhenSuccessful()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var searchResult = new
        {
            assets = new
            {
                total = 5,
                count = 5,
                items = new[]
                {
                    TestFixtures.CreateAsset(id: "asset-1"),
                    TestFixtures.CreateAsset(id: "asset-2")
                },
                nextPage = (string?)null
            }
        };

        handler.When(HttpMethod.Post, "*/search/smart")
            .Respond("application/json", TestFixtures.ToJson(searchResult));

        // Act
        var result = await client.SearchSmartAsync(new SmartSearchRequest { Query = "sunset on beach" });

        // Assert
        result.Should().NotBeNull();
        result.Items.Should().HaveCount(2);
    }
}
