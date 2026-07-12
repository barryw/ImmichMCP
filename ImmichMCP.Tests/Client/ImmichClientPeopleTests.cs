using System.Net;
using FluentAssertions;
using RichardSzalay.MockHttp;
using ImmichMCP.Tests.Fixtures;

namespace ImmichMCP.Tests.Client;

public class ImmichClientPeopleTests
{
    [Fact]
    public async Task GetPersonAssetsAsync_UsesMetadataSearchWithPersonFilter()
    {
        // Arrange
        const string responseJson = """
            {
              "assets": {
                "total": 1,
                "count": 1,
                "items": [
                  {
                    "id": "asset-1",
                    "duration": 1234
                  }
                ],
                "nextPage": null
              }
            }
            """;

        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        string? requestBody = null;
        handler.When(HttpMethod.Post, "*/search/metadata")
            .With(request =>
            {
                requestBody = request.Content!.ReadAsStringAsync().Result;
                return true;
            })
            .Respond("application/json", responseJson);

        // Act
        var result = await client.GetPersonAssetsAsync("person-1");

        // Assert
        result.Should().ContainSingle();
        result[0].Id.Should().Be("asset-1");
        requestBody.Should().Contain("\"personIds\":[\"person-1\"]");
        requestBody.Should().Contain("\"withExif\":true");
    }

    [Fact]
    public async Task GetPersonAssetsAsync_FollowsNextPageTokenUntilComplete()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var requestBodies = new List<string>();

        handler.Expect(HttpMethod.Post, "*/search/metadata")
            .With(request => CaptureRequestBody(request, requestBodies))
            .Respond("application/json", CreatePersonAssetsResponse("asset-1", "7"));
        handler.Expect(HttpMethod.Post, "*/search/metadata")
            .With(request => CaptureRequestBody(request, requestBodies))
            .Respond("application/json", CreatePersonAssetsResponse("asset-7", null));

        // Act
        var result = await client.GetPersonAssetsAsync("person-1");

        // Assert
        result.Select(asset => asset.Id).Should().Equal("asset-1", "asset-7");
        requestBodies.Should().HaveCount(2);
        requestBodies[0].Should().Contain("\"page\":1");
        requestBodies[1].Should().Contain("\"page\":7");
        handler.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task GetPersonAssetsAsync_DoesNotSilentlyStopAfterFiftyPages()
    {
        // Arrange
        const int pageCount = 51;
        var (client, handler) = MockHttpClientFactory.CreateMockClient();

        for (var page = 1; page <= pageCount; page++)
        {
            var nextPage = page < pageCount ? $"{page + 1}" : null;
            handler.Expect(HttpMethod.Post, "*/search/metadata")
                .Respond("application/json", CreatePersonAssetsResponse($"asset-{page}", nextPage));
        }

        // Act
        var result = await client.GetPersonAssetsAsync("person-1");

        // Assert
        result.Should().HaveCount(pageCount);
        result[0].Id.Should().Be("asset-1");
        result[^1].Id.Should().Be("asset-51");
        handler.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [InlineData("1")]
    [InlineData("not-a-page")]
    public async Task GetPersonAssetsAsync_RejectsInvalidOrRepeatedNextPageTokens(string nextPage)
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        handler.Expect(HttpMethod.Post, "*/search/metadata")
            .Respond("application/json", CreatePersonAssetsResponse("asset-1", nextPage));

        // Act
        var act = () => client.GetPersonAssetsAsync("person-1");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetPeopleAsync_ReturnsPeople_WhenSuccessful()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var response = new
        {
            people = new[]
            {
                TestFixtures.CreatePerson(id: "person-1", name: "John Doe"),
                TestFixtures.CreatePerson(id: "person-2", name: "Jane Doe")
            },
            total = 2,
            hidden = 0
        };

        handler.When(HttpMethod.Get, "*/people*")
            .Respond("application/json", TestFixtures.ToJson(response));

        // Act
        var result = await client.GetPeopleAsync();

        // Assert
        result.Should().NotBeNull();
        result.People.Should().HaveCount(2);
        result.People[0].Name.Should().Be("John Doe");
    }

    [Fact]
    public async Task GetPersonAsync_ReturnsPerson_WhenFound()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        var personId = "test-person-id";
        var person = TestFixtures.CreatePerson(id: personId, name: "Test Person");

        handler.When(HttpMethod.Get, $"*/people/{personId}")
            .Respond("application/json", TestFixtures.ToJson(person));

        // Act
        var result = await client.GetPersonAsync(personId);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(personId);
        result.Name.Should().Be("Test Person");
    }

    [Fact]
    public async Task GetPersonAsync_ReturnsNull_WhenNotFound()
    {
        // Arrange
        var (client, handler) = MockHttpClientFactory.CreateMockClient();

        handler.When(HttpMethod.Get, "*/people/non-existent")
            .Respond(HttpStatusCode.NotFound);

        // Act
        var result = await client.GetPersonAsync("non-existent");

        // Assert
        result.Should().BeNull();
    }

    private static bool CaptureRequestBody(HttpRequestMessage request, List<string> requestBodies)
    {
        requestBodies.Add(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
        return true;
    }

    private static string CreatePersonAssetsResponse(string assetId, string? nextPage)
    {
        return TestFixtures.ToJson(new
        {
            assets = new
            {
                total = 1,
                count = 1,
                items = new[]
                {
                    new
                    {
                        id = assetId,
                        duration = 1234
                    }
                },
                nextPage
            }
        });
    }
}
