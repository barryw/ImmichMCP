using System.Net;
using FluentAssertions;
using RichardSzalay.MockHttp;
using ImmichMCP.Tests.Fixtures;

namespace ImmichMCP.Tests.Client;

public class ImmichClientHealthTests
{
    [Fact]
    public async Task PingAsync_ReturnsSuccess_WhenServerResponds()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var serverInfo = new
        {
            version = "1.99.0",
            versionUrl = "https://github.com/immich-app/immich/releases/tag/v1.99.0"
        };

        handler.When(HttpMethod.Get, "*/server/about")
            .Respond("application/json", TestFixtures.ToJson(serverInfo));

        // Act
        var (success, info, error) = await client.PingAsync();

        // Assert
        success.Should().BeTrue();
        error.Should().BeNull();
    }

    [Fact]
    public async Task PingAsync_ReturnsFalse_WhenServerReturnsError()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();

        handler.When(HttpMethod.Get, "*/server/about")
            .Respond(HttpStatusCode.InternalServerError);

        // Act
        var (success, info, error) = await client.PingAsync();

        // Assert
        success.Should().BeFalse();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetFeaturesAsync_ReturnsFeatures_WhenSuccessful()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var features = new
        {
            smartSearch = true,
            facialRecognition = true,
            map = true,
            trash = true,
            oauth = false,
            oauthAutoLaunch = false,
            passwordLogin = true
        };

        handler.When(HttpMethod.Get, "*/server/features")
            .Respond("application/json", TestFixtures.ToJson(features));

        // Act
        var (success, result, error) = await client.GetFeaturesAsync();

        // Assert
        success.Should().BeTrue();
        result.Should().NotBeNull();
    }
}
