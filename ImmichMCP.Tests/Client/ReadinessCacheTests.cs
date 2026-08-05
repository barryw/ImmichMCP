using System.Net;
using FluentAssertions;
using RichardSzalay.MockHttp;
using ImmichMCP.Tests.Fixtures;

namespace ImmichMCP.Tests.Client;

/// <summary>
/// Exercises the internal readiness cache used by the <c>/health/ready</c> endpoint
/// in <c>Program.cs</c>. It is visible here via <c>InternalsVisibleTo</c>.
/// </summary>
public class ReadinessCacheTests
{
    [Fact]
    public async Task GetOrRefreshAsync_ReturnsReady_WhenPingSucceeds()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var serverInfo = new { version = "1.99.0" };

        handler.When(HttpMethod.Get, "*/server/about")
            .Respond("application/json", TestFixtures.ToJson(serverInfo));

        var cache = new ReadinessCache();

        // Act
        var (success, version, error) = await cache.GetOrRefreshAsync(client, CancellationToken.None);

        // Assert
        success.Should().BeTrue();
        version.Should().Be("1.99.0");
        error.Should().BeNull();
    }

    [Fact]
    public async Task GetOrRefreshAsync_ReturnsNotReady_WhenPingFails()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();

        handler.When(HttpMethod.Get, "*/server/about")
            .Respond(HttpStatusCode.ServiceUnavailable);

        var cache = new ReadinessCache();

        // Act
        var (success, version, error) = await cache.GetOrRefreshAsync(client, CancellationToken.None);

        // Assert
        success.Should().BeFalse();
        version.Should().BeNull();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetOrRefreshAsync_ReturnsCachedResult_WithoutRepingingWithinCacheWindow()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var serverInfo = new { version = "1.99.0" };

        var mockedRequest = handler.When(HttpMethod.Get, "*/server/about")
            .Respond("application/json", TestFixtures.ToJson(serverInfo));

        var cache = new ReadinessCache();

        // Act: call twice in quick succession (well within the 10s cache window)
        var first = await cache.GetOrRefreshAsync(client, CancellationToken.None);
        var second = await cache.GetOrRefreshAsync(client, CancellationToken.None);

        // Assert: only the first call actually reached Immich
        first.Success.Should().BeTrue();
        second.Should().Be(first);
        handler.GetMatchCount(mockedRequest).Should().Be(1);
    }

    [Fact]
    public async Task GetOrRefreshAsync_SharesInFlightRefresh_AcrossConcurrentCallers()
    {
        // Arrange: hold the upstream response open so several callers overlap a single refresh.
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var serverInfo = new { version = "1.99.0" };
        var release = new TaskCompletionSource();

        var mockedRequest = handler.When(HttpMethod.Get, "*/server/about")
            .Respond(async (HttpRequestMessage _) =>
            {
                await release.Task;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(TestFixtures.ToJson(serverInfo), System.Text.Encoding.UTF8, "application/json")
                };
            });

        var cache = new ReadinessCache();

        // Act: start several concurrent callers before the single upstream ping completes
        var calls = Enumerable.Range(0, 5)
            .Select(_ => cache.GetOrRefreshAsync(client, CancellationToken.None))
            .ToArray();

        release.SetResult();
        var results = await Task.WhenAll(calls);

        // Assert: exactly one upstream ping was issued, and every caller got the same result
        handler.GetMatchCount(mockedRequest).Should().Be(1);
        results.Should().OnlyContain(r => r.Success && r.Version == "1.99.0");
    }
}
