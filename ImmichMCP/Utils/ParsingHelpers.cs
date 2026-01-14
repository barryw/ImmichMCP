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
}
