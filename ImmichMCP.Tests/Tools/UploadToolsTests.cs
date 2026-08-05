using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using ImmichMCP.Configuration;
using ImmichMCP.Services;
using ImmichMCP.Tests.Fixtures;
using ImmichMCP.Tools;

namespace ImmichMCP.Tests.Tools;

public class UploadToolsTests
{
    private static UploadTools CreateSut(out UploadSessionService service)
    {
        service = new UploadSessionService();
        return new UploadTools(service, Options.Create(new ImmichOptions()));
    }

    [Fact]
    public void UploadInit_ReturnsSessionAndUploadUrl()
    {
        // Arrange
        var (client, _) = MockHttpClientFactory.CreateMockClient();
        var sut = CreateSut(out var service);

        // Act
        var response = sut.UploadInit(client, fileName: "photo.jpg", isFavorite: true);

        // Assert
        using var json = JsonDocument.Parse(response);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        var result = json.RootElement.GetProperty("result");
        var sessionId = result.GetProperty("session_id").GetString();
        sessionId.Should().NotBeNullOrEmpty();
        result.GetProperty("upload_url").GetString().Should().Contain(sessionId!);
        result.GetProperty("instructions").GetProperty("method").GetString().Should().Be("POST");

        var session = service.GetSession(sessionId!);
        session.Should().NotBeNull();
        session!.FileName.Should().Be("photo.jpg");
        session.IsFavorite.Should().BeTrue();
    }

    [Fact]
    public void UploadStatus_ReturnsSessionDetails_WhenSessionExists()
    {
        // Arrange
        var (client, _) = MockHttpClientFactory.CreateMockClient();
        var sut = CreateSut(out var service);
        var session = service.CreateSession(fileName: "clip.mp4");

        // Act
        var response = sut.UploadStatus(client, session.SessionId);

        // Assert
        using var json = JsonDocument.Parse(response);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        var result = json.RootElement.GetProperty("result");
        result.GetProperty("session_id").GetString().Should().Be(session.SessionId);
        result.GetProperty("status").GetString().Should().Be("pending");
    }

    [Fact]
    public void UploadStatus_ReturnsNotFoundError_WhenSessionIdUnknown()
    {
        // Arrange
        var (client, _) = MockHttpClientFactory.CreateMockClient();
        var sut = CreateSut(out _);

        // Act
        var response = sut.UploadStatus(client, "unknown-session-id");

        // Assert
        using var json = JsonDocument.Parse(response);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("NOT_FOUND");
    }
}
