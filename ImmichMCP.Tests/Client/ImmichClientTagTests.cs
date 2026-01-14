using System.Net;
using FluentAssertions;
using RichardSzalay.MockHttp;
using ImmichMCP.Tests.Fixtures;

namespace ImmichMCP.Tests.Client;

public class ImmichClientTagTests
{
    [Fact]
    public async Task GetTagsAsync_ReturnsTags_WhenSuccessful()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var tags = new[]
        {
            TestFixtures.CreateTag(id: "tag-1", name: "Vacation"),
            TestFixtures.CreateTag(id: "tag-2", name: "Family")
        };

        handler.When(HttpMethod.Get, "*/tags")
            .Respond("application/json", TestFixtures.ToJson(tags));

        // Act
        var result = await client.GetTagsAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result![0].Name.Should().Be("Vacation");
    }

    [Fact]
    public async Task GetTagAsync_ReturnsTag_WhenFound()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var tagId = "test-tag-id";
        var tag = TestFixtures.CreateTag(id: tagId, name: "Test Tag");

        handler.When(HttpMethod.Get, $"*/tags/{tagId}")
            .Respond("application/json", TestFixtures.ToJson(tag));

        // Act
        var result = await client.GetTagAsync(tagId);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(tagId);
        result.Name.Should().Be("Test Tag");
    }

    [Fact]
    public async Task GetTagAsync_ReturnsNull_WhenNotFound()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();

        handler.When(HttpMethod.Get, "*/tags/non-existent")
            .Respond(HttpStatusCode.NotFound);

        // Act
        var result = await client.GetTagAsync("non-existent");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteTagAsync_ReturnsTrue_WhenSuccessful()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var tagId = "test-tag-id";

        handler.When(HttpMethod.Delete, $"*/tags/{tagId}")
            .Respond(HttpStatusCode.NoContent);

        // Act
        var result = await client.DeleteTagAsync(tagId);

        // Assert
        result.Should().BeTrue();
    }
}
