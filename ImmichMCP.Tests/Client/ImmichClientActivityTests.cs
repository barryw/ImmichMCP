using System.Net;
using FluentAssertions;
using RichardSzalay.MockHttp;
using ImmichMCP.Models.Activities;
using ImmichMCP.Tests.Fixtures;

namespace ImmichMCP.Tests.Client;

public class ImmichClientActivityTests
{
    private static Activity CreateActivity(string? id = null, string type = "comment", string? comment = null)
    {
        return new Activity
        {
            Id = id ?? Guid.NewGuid().ToString(),
            Type = type,
            Comment = comment ?? (type == "comment" ? "Test comment" : null),
            CreatedAt = DateTime.UtcNow,
            User = new ActivityUser
            {
                Id = Guid.NewGuid().ToString(),
                Email = "test@example.com",
                Name = "Test User"
            }
        };
    }

    [Fact]
    public async Task GetActivitiesAsync_ReturnsActivities_WhenSuccessful()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var activities = new[]
        {
            CreateActivity(id: "activity-1", type: "comment", comment: "Nice photo!"),
            CreateActivity(id: "activity-2", type: "like")
        };

        handler.When(HttpMethod.Get, "*/activities*")
            .Respond("application/json", TestFixtures.ToJson(activities));

        // Act
        var result = await client.GetActivitiesAsync("album-123");

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task DeleteActivityAsync_ReturnsTrue_WhenSuccessful()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var activityId = "test-activity-id";

        handler.When(HttpMethod.Delete, $"*/activities/{activityId}")
            .Respond(HttpStatusCode.NoContent);

        // Act
        var result = await client.DeleteActivityAsync(activityId);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task GetActivityStatisticsAsync_ReturnsStatistics()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var stats = new { comments = 10 };

        handler.When(HttpMethod.Get, "*/activities/statistics*")
            .Respond("application/json", TestFixtures.ToJson(stats));

        // Act
        var result = await client.GetActivityStatisticsAsync("album-123");

        // Assert
        result.Should().NotBeNull();
    }
}
