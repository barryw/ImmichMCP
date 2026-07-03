using System.Net;
using System.Text.Json;
using FluentAssertions;
using ImmichMCP.Client;
using ImmichMCP.Tools.Gateway;
using ModelContextProtocol.Protocol;

namespace ImmichMCP.Tests.Gateway;

/// <summary>
/// The MCP spec requires tool execution errors to be reported with isError: true.
/// These guard the gateway boundary that enforces that for both thrown upstream
/// failures and tool-serialized {"ok": false} envelopes.
/// </summary>
public class ImmichToolGatewayErrorHandlingTests
{
    [Theory]
    [InlineData("{\"ok\":false,\"error\":{\"code\":\"NOT_FOUND\"}}", true)]
    [InlineData("{\"ok\":true,\"result\":[]}", false)]
    [InlineData("{\"result\":[]}", false)]
    [InlineData("not json", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsErrorEnvelope_DetectsOkFalse(string? text, bool expected)
    {
        ImmichToolGateway.IsErrorEnvelope(text).Should().Be(expected);
    }

    [Fact]
    public void MarkErrorEnvelope_FlagsIsError_ForOkFalseEnvelope()
    {
        var result = new CallToolResult
        {
            Content = [new TextContentBlock { Text = "{\"ok\":false,\"error\":{\"code\":\"UPSTREAM_ERROR\"}}" }],
            IsError = false
        };

        ImmichToolGateway.MarkErrorEnvelope(result).IsError.Should().BeTrue();
    }

    [Fact]
    public void MarkErrorEnvelope_LeavesSuccess_Untouched()
    {
        var result = new CallToolResult
        {
            Content = [new TextContentBlock { Text = "{\"ok\":true,\"result\":[]}" }],
            IsError = false
        };

        ImmichToolGateway.MarkErrorEnvelope(result).IsError.Should().BeFalse();
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "AUTH_FAILED")]
    [InlineData(HttpStatusCode.Forbidden, "AUTH_FAILED")]
    [InlineData(HttpStatusCode.NotFound, "NOT_FOUND")]
    [InlineData(HttpStatusCode.BadRequest, "VALIDATION")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "VALIDATION")]
    [InlineData(HttpStatusCode.TooManyRequests, "RATE_LIMIT")]
    [InlineData(HttpStatusCode.InternalServerError, "UPSTREAM_ERROR")]
    [InlineData(HttpStatusCode.BadGateway, "UPSTREAM_ERROR")]
    public void BuildUpstreamError_MapsStatusToErrorCode(HttpStatusCode status, string expectedCode)
    {
        var ex = new ImmichApiException(status, "GET", "api/thing", "body");

        var json = JsonSerializer.SerializeToElement(ImmichToolGateway.BuildUpstreamError(ex));

        json.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.GetProperty("error").GetProperty("code").GetString().Should().Be(expectedCode);
        json.GetProperty("error").GetProperty("details").GetProperty("status").GetInt32().Should().Be((int)status);
    }
}
