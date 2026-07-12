using System.Text.Json;
using FluentAssertions;
using RichardSzalay.MockHttp;
using ImmichMCP.Tests.Fixtures;
using ImmichMCP.Tools;

namespace ImmichMCP.Tests.Tools;

public class PeopleToolsTests
{
    [Fact]
    public async Task List_UsesServerPaginationAndComputesVisibleCount()
    {
        // Arrange: raw Immich v3 PeopleResponseDto shape.
        const string responseJson = """
            {
              "people": [
                {
                  "id": "person-26",
                  "name": "Page Two Person",
                  "birthDate": null,
                  "thumbnailPath": "/people/person-26/thumbnail",
                  "isHidden": false
                }
              ],
              "total": 700,
              "hidden": 5,
              "hasNextPage": true
            }
            """;

        var (client, handler) = MockHttpClientFactory.CreateMockClient();
        string? requestUri = null;
        handler.When(HttpMethod.Get, "*/people*")
            .With(request =>
            {
                requestUri = request.RequestUri!.ToString();
                return true;
            })
            .Respond("application/json", responseJson);

        // Act
        var response = await PeopleTools.List(client, withHidden: true, page: 2, size: 25);

        // Assert
        requestUri.Should().Contain("withHidden=true");
        requestUri.Should().Contain("page=2");
        requestUri.Should().Contain("size=25");

        using var json = JsonDocument.Parse(response);
        var result = json.RootElement.GetProperty("result");
        result.GetProperty("people").GetArrayLength().Should().Be(1);
        result.GetProperty("visible").GetInt32().Should().Be(695);
        result.GetProperty("hidden").GetInt32().Should().Be(5);

        var meta = json.RootElement.GetProperty("meta");
        meta.GetProperty("total").GetInt32().Should().Be(700);
        meta.GetProperty("next").GetString().Should().Be("page=3&size=25");
    }

    [Fact]
    public async Task Assets_UsesPaginatedMetadataSearchWithPersonFilter()
    {
        // Arrange: raw Immich v3 SearchResponseDto shape.
        const string responseJson = """
            {
              "assets": {
                "total": 42,
                "count": 1,
                "items": [
                  {
                    "id": "asset-26",
                    "duration": 1234
                  }
                ],
                "nextPage": "3"
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
        var response = await PeopleTools.Assets(client, "person-1", page: 2, size: 25);

        // Assert
        requestBody.Should().Contain("\"personIds\":[\"person-1\"]");
        requestBody.Should().Contain("\"page\":2");
        requestBody.Should().Contain("\"size\":25");

        using var json = JsonDocument.Parse(response);
        json.RootElement.GetProperty("result").GetArrayLength().Should().Be(1);
        var meta = json.RootElement.GetProperty("meta");
        meta.GetProperty("total").GetInt32().Should().Be(42);
        meta.GetProperty("next").GetString().Should().Be("3");
    }
}
