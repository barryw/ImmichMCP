using System.Text.Json;
using FluentAssertions;
using RichardSzalay.MockHttp;
using ImmichMCP.Client;
using ImmichMCP.Tests.Fixtures;
using ImmichMCP.Tools;

namespace ImmichMCP.Tests.Tools;

public class TagToolsTests
{
    [Fact]
    public async Task Create_ReturnsTag_WhenSuccessful()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var tag = TestFixtures.CreateTag(id: "tag-1", name: "Sunsets");

        handler.When(HttpMethod.Post, "*/tags")
            .Respond("application/json", TestFixtures.ToJson(tag));

        // Act
        var response = await TagTools.Create(client, name: "Sunsets");

        // Assert
        using var json = JsonDocument.Parse(response);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("result").GetProperty("name").GetString().Should().Be("Sunsets");
    }

    [Fact]
    public async Task Create_ReturnsValidationError_WhenNameMissing()
    {
        // Arrange
        var (client, _) = MockHttpClientFactory.CreateMockClient();

        // Act
        var response = await TagTools.Create(client, name: " ");

        // Assert
        using var json = JsonDocument.Parse(response);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION");
    }

    [Fact]
    public async Task Update_ReturnsUpdatedTag_WhenSuccessful()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var tag = TestFixtures.CreateTag(id: "tag-1", name: "Sunsets");

        handler.When(HttpMethod.Put, "*/tags/tag-1")
            .Respond("application/json", TestFixtures.ToJson(tag));

        // Act
        var response = await TagTools.Update(client, "tag-1", color: "#ff0000");

        // Assert
        using var json = JsonDocument.Parse(response);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("result").GetProperty("id").GetString().Should().Be("tag-1");
    }

    [Fact]
    public async Task Update_Throws_WhenUpstreamReturnsNotFound()
    {
        // Arrange: PUT surfaces non-success responses as exceptions (unlike GET, which
        // treats 404 as a legitimate "not found" and returns null), see ImmichClient.PutAsync.
        var (client, handler) = MockHttpClientFactory.CreateMockClient();

        handler.When(HttpMethod.Put, "*/tags/missing-tag")
            .Respond(System.Net.HttpStatusCode.NotFound);

        // Act
        var act = () => TagTools.Update(client, "missing-tag", color: "#ff0000");

        // Assert
        await act.Should().ThrowAsync<ImmichApiException>();
    }

    [Fact]
    public async Task TagAssets_ReturnsTaggedCount_WhenSuccessful()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        // Both elements need the same anonymous-type shape or the array has no common type.
        var results = new[]
        {
            new { id = "asset-1", success = true, errorMessage = (string?)null },
            new { id = "asset-2", success = false, errorMessage = (string?)"already tagged" }
        };

        handler.When(HttpMethod.Put, "*/tags/tag-1/assets")
            .Respond("application/json", TestFixtures.ToJson(results));

        // Act
        var response = await TagTools.TagAssets(client, "tag-1", "asset-1,asset-2");

        // Assert
        using var json = JsonDocument.Parse(response);
        var result = json.RootElement.GetProperty("result");
        result.GetProperty("tag_id").GetString().Should().Be("tag-1");
        result.GetProperty("tagged").GetInt32().Should().Be(1);
        result.GetProperty("failed").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task TagAssets_ReturnsValidationError_WhenAssetIdsEmpty()
    {
        // Arrange
        var (client, _) = MockHttpClientFactory.CreateMockClient();

        // Act
        var response = await TagTools.TagAssets(client, "tag-1", "  ");

        // Assert
        using var json = JsonDocument.Parse(response);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION");
    }

    [Fact]
    public async Task UntagAssets_ReturnsUntaggedCount_WhenSuccessful()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var results = new[]
        {
            new { id = "asset-1", success = true }
        };

        handler.When(HttpMethod.Delete, "*/tags/tag-1/assets")
            .Respond("application/json", TestFixtures.ToJson(results));

        // Act
        var response = await TagTools.UntagAssets(client, "tag-1", "asset-1");

        // Assert
        using var json = JsonDocument.Parse(response);
        var result = json.RootElement.GetProperty("result");
        result.GetProperty("tag_id").GetString().Should().Be("tag-1");
        result.GetProperty("untagged").GetInt32().Should().Be(1);
        result.GetProperty("failed").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task UntagAssets_ReturnsValidationError_WhenAssetIdsEmpty()
    {
        // Arrange
        var (client, _) = MockHttpClientFactory.CreateMockClient();

        // Act
        var response = await TagTools.UntagAssets(client, "tag-1", "");

        // Assert
        using var json = JsonDocument.Parse(response);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION");
    }

    [Fact]
    public async Task Delete_ReturnsConfirmationRequired_AndDoesNotCallDelete_WhenConfirmFalse()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var tag = TestFixtures.CreateTag(id: "tag-1", name: "Sunsets");

        handler.When(HttpMethod.Get, "*/tags/tag-1")
            .Respond("application/json", TestFixtures.ToJson(tag));

        var deleteRequest = handler.When(HttpMethod.Delete, "*/tags/tag-1")
            .Respond(System.Net.HttpStatusCode.NoContent);

        // Act
        var response = await TagTools.Delete(client, "tag-1", confirm: false);

        // Assert
        using var json = JsonDocument.Parse(response);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("CONFIRMATION_REQUIRED");
        var details = json.RootElement.GetProperty("error").GetProperty("details");
        details.GetProperty("tag_id").GetString().Should().Be("tag-1");
        details.GetProperty("name").GetString().Should().Be("Sunsets");

        handler.GetMatchCount(deleteRequest).Should().Be(0);
    }

    [Fact]
    public async Task Delete_ReturnsNotFoundError_WhenTagMissingAndConfirmFalse()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();

        handler.When(HttpMethod.Get, "*/tags/missing-tag")
            .Respond(System.Net.HttpStatusCode.NotFound);

        // Act
        var response = await TagTools.Delete(client, "missing-tag", confirm: false);

        // Assert
        using var json = JsonDocument.Parse(response);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Delete_DeletesTag_WhenConfirmTrue()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();

        var deleteRequest = handler.When(HttpMethod.Delete, "*/tags/tag-1")
            .Respond(System.Net.HttpStatusCode.NoContent);

        // Act
        var response = await TagTools.Delete(client, "tag-1", confirm: true);

        // Assert
        using var json = JsonDocument.Parse(response);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("result").GetProperty("deleted").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("result").GetProperty("tag_id").GetString().Should().Be("tag-1");

        handler.GetMatchCount(deleteRequest).Should().Be(1);
    }
}
