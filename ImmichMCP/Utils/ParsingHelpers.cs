using ImmichMCP.Models.Common;

namespace ImmichMCP.Utils;

/// <summary>
/// Shared parsing utilities for MCP tool parameters.
/// </summary>
public static class ParsingHelpers
{
    /// <summary>
    /// Parses a comma-separated string of values into an array.
    /// </summary>
    /// <param name="input">Comma-separated values (e.g., "a,b,c")</param>
    /// <returns>Array of parsed strings, or null if input is empty/whitespace</returns>
    public static string[]? ParseStringArray(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        return input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// Clamps a requested page size to at least 1 and at most <paramref name="max"/>.
    /// </summary>
    public static int ClampPageSize(int requested, int max) => Math.Clamp(requested, 1, max);

    /// <summary>
    /// Parses a comma-separated ID list and returns a ready-to-serialize validation error
    /// string when empty; returns null (with <paramref name="ids"/> populated) otherwise.
    /// </summary>
    /// <param name="csv">Comma-separated ID list (e.g., "id1,id2,id3")</param>
    /// <param name="baseUrl">Immich base URL to include in the error response's meta</param>
    /// <param name="entityLabel">Label describing the missing IDs, e.g. "asset IDs" or "source person IDs"</param>
    /// <param name="ids">Populated with the parsed IDs (empty array if none)</param>
    /// <returns>A serialized <see cref="McpErrorResponse"/> when <paramref name="ids"/> is empty; otherwise null</returns>
    public static string? RequireIds(string? csv, string baseUrl, string entityLabel, out string[] ids)
    {
        ids = ParseStringArray(csv) ?? [];
        if (ids.Length > 0)
            return null;

        var errorResponse = McpErrorResponse.Create(
            ErrorCodes.Validation,
            $"No valid {entityLabel} provided",
            meta: new McpMeta { ImmichBaseUrl = baseUrl });
        return System.Text.Json.JsonSerializer.Serialize(errorResponse);
    }

    /// <summary>
    /// Parses a comma-separated string of integers into an array.
    /// </summary>
    /// <param name="input">Comma-separated integer values (e.g., "1,2,3")</param>
    /// <returns>Array of parsed integers, or null if input is empty/whitespace</returns>
    public static int[]? ParseIntArray(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        return input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var n) ? n : (int?)null)
            .Where(n => n.HasValue)
            .Select(n => n!.Value)
            .ToArray();
    }

    /// <summary>
    /// Parses a date string into a DateTime.
    /// </summary>
    /// <param name="input">Date string in any standard format</param>
    /// <returns>Parsed DateTime, or null if input is empty/invalid</returns>
    public static DateTime? ParseDate(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        return DateTime.TryParse(input, out var date) ? date : null;
    }

    /// <summary>
    /// Parses a boolean string.
    /// </summary>
    /// <param name="input">Boolean string ("true", "false", "1", "0")</param>
    /// <returns>Parsed boolean, or null if input is empty/invalid</returns>
    public static bool? ParseBool(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        if (bool.TryParse(input, out var result))
            return result;

        if (input == "1") return true;
        if (input == "0") return false;

        return null;
    }

    /// <summary>
    /// Maps the old archived flag exposed by tools to Immich v3 asset visibility.
    /// </summary>
    public static string? VisibilityFromArchived(bool? isArchived) => isArchived switch
    {
        true => "archive",
        false => "timeline",
        null => null
    };

    /// <summary>
    /// Maps upload visibility parameters to Immich v3 asset visibility.
    /// </summary>
    public static string? VisibilityFromUploadFlags(bool? isArchived, bool? isVisible)
    {
        if (isArchived == true)
            return "archive";

        if (isArchived == false)
            return "timeline";

        return isVisible switch
        {
            true => "timeline",
            false => "hidden",
            null => null
        };
    }
}
