using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ImmichMCP.Utils;

/// <summary>
/// Reads the Immich v3 duration contract (integer milliseconds) while also
/// accepting the legacy v2 timespan string representation.
/// </summary>
public sealed class DurationMillisecondsJsonConverter : JsonConverter<int?>
{
    public override int? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.Number &&
            reader.TryGetInt32(out var milliseconds) &&
            milliseconds >= 0)
        {
            return milliseconds;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var value = reader.GetString();
            if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var duration) &&
                duration >= TimeSpan.Zero)
            {
                var durationMilliseconds = duration.Ticks / TimeSpan.TicksPerMillisecond;
                if (durationMilliseconds <= int.MaxValue)
                {
                    return (int)durationMilliseconds;
                }
            }
        }

        throw new JsonException("Asset duration must be non-negative integer milliseconds or a timespan string.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        int? value,
        JsonSerializerOptions options)
    {
        if (value.HasValue)
        {
            writer.WriteNumberValue(value.Value);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}
