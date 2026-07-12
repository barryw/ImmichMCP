using System.Text.Json;
using FluentAssertions;
using RichardSzalay.MockHttp;
using ImmichMCP.Tests.Fixtures;
using ImmichMCP.Tools;

namespace ImmichMCP.Tests.Tools;

public class HealthToolsTests
{
    [Fact]
    public async Task GetCapabilities_UsesCurrentImmichFeatureContractAndEndpoints()
    {
        // Arrange: raw Immich v3 ServerAboutResponseDto and ServerFeaturesDto shapes.
        const string aboutJson = """
            {
              "version": "3.0.2",
              "versionUrl": "https://github.com/immich-app/immich/releases/tag/v3.0.2",
              "licensed": false
            }
            """;
        const string featuresJson = """
            {
              "configFile": true,
              "duplicateDetection": true,
              "email": true,
              "facialRecognition": true,
              "importFaces": true,
              "map": true,
              "oauth": true,
              "oauthAutoLaunch": true,
              "ocr": true,
              "passwordLogin": true,
              "realtimeTranscoding": true,
              "reverseGeocoding": true,
              "search": true,
              "sidecar": true,
              "smartSearch": true,
              "trash": true
            }
            """;

        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        handler.When(HttpMethod.Get, "*/server/about")
            .Respond("application/json", aboutJson);
        handler.When(HttpMethod.Get, "*/server/features")
            .Respond("application/json", featuresJson);

        // Act
        var response = await HealthTools.GetCapabilities(client);

        // Assert
        using var json = JsonDocument.Parse(response);
        var result = json.RootElement.GetProperty("result");
        var features = result.GetProperty("features");
        features.GetProperty("ImportFaces").GetBoolean().Should().BeTrue();
        features.GetProperty("Ocr").GetBoolean().Should().BeTrue();
        features.GetProperty("RealtimeTranscoding").GetBoolean().Should().BeTrue();
        features.GetProperty("OauthAutoLaunch").GetBoolean().Should().BeTrue();
        features.TryGetProperty("Import", out _).Should().BeFalse();

        result.GetProperty("endpoints")
            .GetProperty("assets")
            .GetProperty("list")
            .GetString()
            .Should().Be("/api/search/metadata");

        result.GetProperty("endpoints")
            .GetProperty("people")
            .GetProperty("assets")
            .GetString()
            .Should().Be("/api/search/metadata");
    }
}
