using System.Net;
using FluentAssertions;
using RichardSzalay.MockHttp;
using ImmichMCP.Models.SharedLinks;
using ImmichMCP.Tests.Fixtures;

namespace ImmichMCP.Tests.Client;

public class ImmichClientSharedLinkTests
{
    private static SharedLink CreateSharedLink(string? id = null, string type = "ALBUM")
    {
        return new SharedLink
        {
            Id = id ?? Guid.NewGuid().ToString(),
            Key = "share-key-123",
            Type = type,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            AllowUpload = false,
            AllowDownload = true,
            ShowMetadata = true,
            CreatedAt = DateTime.UtcNow
        };
    }

    [Fact]
    public async Task GetSharedLinksAsync_ReturnsSharedLinks_WhenSuccessful()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var sharedLinks = new[]
        {
            CreateSharedLink(id: "link-1"),
            CreateSharedLink(id: "link-2")
        };

        handler.When(HttpMethod.Get, "*/shared-links")
            .Respond("application/json", TestFixtures.ToJson(sharedLinks));

        // Act
        var result = await client.GetSharedLinksAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetSharedLinkAsync_ReturnsSharedLink_WhenFound()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var linkId = "test-link-id";
        var sharedLink = CreateSharedLink(id: linkId);

        handler.When(HttpMethod.Get, $"*/shared-links/{linkId}")
            .Respond("application/json", TestFixtures.ToJson(sharedLink));

        // Act
        var result = await client.GetSharedLinkAsync(linkId);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(linkId);
    }

    [Fact]
    public async Task GetSharedLinkAsync_ReturnsNull_WhenNotFound()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();

        handler.When(HttpMethod.Get, "*/shared-links/non-existent")
            .Respond(HttpStatusCode.NotFound);

        // Act
        var result = await client.GetSharedLinkAsync("non-existent");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteSharedLinkAsync_ReturnsTrue_WhenSuccessful()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var linkId = "test-link-id";

        handler.When(HttpMethod.Delete, $"*/shared-links/{linkId}")
            .Respond(HttpStatusCode.NoContent);

        // Act
        var result = await client.DeleteSharedLinkAsync(linkId);

        // Assert
        result.Should().BeTrue();
    }
}
