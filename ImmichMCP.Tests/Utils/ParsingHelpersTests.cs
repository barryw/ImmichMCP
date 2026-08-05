using System.Text.Json;
using FluentAssertions;
using ImmichMCP.Models.Common;
using static ImmichMCP.Utils.ParsingHelpers;

namespace ImmichMCP.Tests.Utils;

public class ParsingHelpersTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(1, 1)]
    [InlineData(50, 50)]
    [InlineData(100, 100)]
    [InlineData(101, 100)]
    [InlineData(int.MaxValue, 100)]
    public void ClampPageSize_ClampsToRange(int requested, int expected)
    {
        ClampPageSize(requested, 100).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",,")]
    public void RequireIds_ReturnsValidationError_WhenNoIdsPresent(string? csv)
    {
        var error = RequireIds(csv, "https://immich.example", "asset IDs", out var ids);

        error.Should().NotBeNull();
        ids.Should().BeEmpty();

        using var json = JsonDocument.Parse(error!);
        json.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be(ErrorCodes.Validation);
        json.RootElement.GetProperty("error").GetProperty("message").GetString().Should().Be("No valid asset IDs provided");
    }

    [Fact]
    public void RequireIds_ReturnsNullAndPopulatesIds_WhenIdsPresent()
    {
        var error = RequireIds(" id1 , id2 ,,id3 ", "https://immich.example", "asset IDs", out var ids);

        error.Should().BeNull();
        ids.Should().Equal("id1", "id2", "id3");
    }
}
