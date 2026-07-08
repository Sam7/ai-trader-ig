using System.Text.Json;
using System.Text.Json.Serialization;
using Trading.Abstractions;

namespace Trading.Execution;

internal static class ExecutionBoundaryJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        Converters =
        {
            new JsonStringEnumConverter(),
            new InstrumentIdJsonConverter(),
        },
    };
}

internal sealed class InstrumentIdJsonConverter : JsonConverter<InstrumentId>
{
    public override InstrumentId Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
        => new(reader.GetString() ?? string.Empty);

    public override void Write(
        Utf8JsonWriter writer,
        InstrumentId value,
        JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}
