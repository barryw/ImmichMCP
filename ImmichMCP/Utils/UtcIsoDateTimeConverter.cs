using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ImmichMCP.Utils;

/// <summary>
/// Serializes <see cref="DateTime"/> values as UTC ISO 8601 with a trailing 'Z'.
///
/// Immich v3 validates datetime request fields with Zod (z.iso.datetime), which rejects
/// any datetime string lacking a timezone designator (Z or +/-hh:mm) with HTTP 400.
/// System.Text.Json's default serializer emits DateTimes whose Kind is Unspecified
/// (e.g. those produced by DateTime.Parse("2026-06-02")) without a suffix, so date
/// filters like takenAfter/takenBefore/updatedAfter were silently rejected upstream.
/// </summary>
public sealed class UtcIsoDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetDateTime();

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
        writer.WriteStringValue(utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
    }
}
