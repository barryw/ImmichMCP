using System.Net;
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
        handler.When(HttpMethod.Post, "*/search/smart")
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
            "OcrSearch should not send a CLIP query");
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
}
