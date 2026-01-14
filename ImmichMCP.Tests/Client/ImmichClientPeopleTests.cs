using System.Net;
using FluentAssertions;
using RichardSzalay.MockHttp;
using ImmichMCP.Tests.Fixtures;

namespace ImmichMCP.Tests.Client;

public class ImmichClientPeopleTests
{
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
}
