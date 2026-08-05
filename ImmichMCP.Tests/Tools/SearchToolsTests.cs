using System.Net;
using System.Text.Json;
using FluentAssertions;
using RichardSzalay.MockHttp;
using ImmichMCP.Tools;
using ImmichMCP.Tests.Fixtures;

namespace ImmichMCP.Tests.Tools;

public class SearchToolsTests
{
    [Fact]
    public async Task MetadataSearch_SetsWithExifTrue_InRequest()
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

        // Act
        var result = await SearchTools.MetadataSearch(client);

        // Assert
        capturedRequestBody.Should().NotBeNull();
        capturedRequestBody.Should().Contain("\"withExif\":true",
            "MetadataSearch should always request EXIF data from the Immich API");
    }

    [Fact]
    public async Task MetadataSearch_ReturnsExifData_InResults()
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

        handler.When(HttpMethod.Post, "*/search/metadata")
            .Respond("application/json", TestFixtures.ToJson(searchResult));

        // Act
        var result = await SearchTools.MetadataSearch(client);

        // Assert - the result should contain EXIF fields from the fixture (Canon EOS R5)
        result.Should().Contain("Canon");
        result.Should().Contain("EOS R5");
    }

    [Fact]
    public async Task OcrSearch_SendsOcrField_AndOmitsQuery()
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

        // Act
        var result = await SearchTools.OcrSearch(client, text: "factuur 2026");

        // Assert
        capturedRequestBody.Should().NotBeNull();
        capturedRequestBody.Should().Contain("\"ocr\":\"factuur 2026\"",
            "OcrSearch must forward the text under the 'ocr' field");
        capturedRequestBody.Should().NotContain("\"query\"",
            "OcrSearch must use the metadata endpoint, which has no CLIP query");
    }

    [Fact]
    public async Task OcrSearch_ReturnsValidationError_WhenTextEmpty()
    {
        // Arrange
        var (client, _) = MockHttpClientFactory.CreateMockClient();

        // Act
        var result = await SearchTools.OcrSearch(client, text: "  ");

        // Assert
        result.Should().Contain("OCR search text is required");
    }

    [Fact]
    public async Task MetadataSearch_ClampsSizeAndReflectsClampedValue_InMeta()
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

        string? capturedRequestBody = null;
        handler.When(HttpMethod.Post, "*/search/metadata")
            .With(req =>
            {
                capturedRequestBody = req.Content!.ReadAsStringAsync().Result;
                return true;
            })
            .Respond("application/json", TestFixtures.ToJson(searchResult));

        // Act
        var result = await SearchTools.MetadataSearch(client, size: 0);

        // Assert: the size sent upstream and the size echoed in meta must agree,
        // otherwise callers doing pagination math see a page_size that doesn't match what was requested.
        capturedRequestBody.Should().Contain("\"size\":1");

        using var json = JsonDocument.Parse(result);
        var meta = json.RootElement.GetProperty("meta");
        meta.GetProperty("page_size").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task SmartSearch_ClampsSizeAndReflectsClampedValue_InMeta()
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

        string? capturedRequestBody = null;
        handler.When(HttpMethod.Post, "*/search/smart")
            .With(req =>
            {
                capturedRequestBody = req.Content!.ReadAsStringAsync().Result;
                return true;
            })
            .Respond("application/json", TestFixtures.ToJson(searchResult));

        // Act
        var result = await SearchTools.SmartSearch(client, query: "sunset", size: 100000);

        // Assert
        capturedRequestBody.Should().Contain("\"size\":100");

        using var json = JsonDocument.Parse(result);
        var meta = json.RootElement.GetProperty("meta");
        meta.GetProperty("page_size").GetInt32().Should().Be(100);
    }
}
