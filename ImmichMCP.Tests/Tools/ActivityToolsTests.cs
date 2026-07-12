using System.Text.Json;
using FluentAssertions;
using RichardSzalay.MockHttp;
using ImmichMCP.Tests.Fixtures;
using ImmichMCP.Tools;

namespace ImmichMCP.Tests.Tools;

public class ActivityToolsTests
{
    [Fact]
    public async Task Statistics_ReturnsCommentAndLikeCounts()
    {
        // Arrange: raw Immich v3 ActivityStatisticsResponseDto shape.
        const string responseJson = """
            {
              "comments": 3,
              "likes": 4
            }
            """;

        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        handler.When(HttpMethod.Get, "*/activities/statistics*")
            .Respond("application/json", responseJson);

        // Act
        var response = await ActivityTools.Statistics(client, "album-1");

        // Assert
        using var json = JsonDocument.Parse(response);
        var result = json.RootElement.GetProperty("result");
        result.GetProperty("comments").GetInt32().Should().Be(3);
        result.GetProperty("likes").GetInt32().Should().Be(4);
    }
}
