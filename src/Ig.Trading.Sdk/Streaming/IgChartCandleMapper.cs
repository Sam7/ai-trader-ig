using System.Globalization;

namespace Ig.Trading.Sdk.Streaming;

public static class IgChartCandleMapper
{
    public static IgChartCandleUpdate Map(
        string epic,
        string scale,
        IReadOnlyDictionary<string, string?> fields)
    {
        if (string.IsNullOrWhiteSpace(epic))
        {
            throw new ArgumentException("EPIC is required.", nameof(epic));
        }

        if (string.IsNullOrWhiteSpace(scale))
        {
            throw new ArgumentException("Chart scale is required.", nameof(scale));
        }

        return new IgChartCandleUpdate(
            epic,
            scale,
            ReadUnixMilliseconds(fields, "UTM"),
            ReadDecimal(fields, "BID_OPEN"),
            ReadDecimal(fields, "BID_HIGH"),
            ReadDecimal(fields, "BID_LOW"),
            ReadDecimal(fields, "BID_CLOSE"),
            ReadDecimal(fields, "OFR_OPEN"),
            ReadDecimal(fields, "OFR_HIGH"),
            ReadDecimal(fields, "OFR_LOW"),
            ReadDecimal(fields, "OFR_CLOSE"),
            string.Equals(ReadRequired(fields, "CONS_END"), "1", StringComparison.Ordinal),
            ReadOptionalLong(fields, "CONS_TICK_COUNT"));
    }

    private static DateTimeOffset ReadUnixMilliseconds(IReadOnlyDictionary<string, string?> fields, string name)
    {
        var value = ReadRequired(fields, name);
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var milliseconds))
        {
            throw new IgStreamingDataException($"IG chart field '{name}' has invalid millisecond timestamp '{value}'.");
        }

        return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
    }

    private static decimal ReadDecimal(IReadOnlyDictionary<string, string?> fields, string name)
    {
        var value = ReadRequired(fields, name);
        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new IgStreamingDataException($"IG chart field '{name}' has invalid decimal value '{value}'.");
        }

        return parsed;
    }

    private static long? ReadOptionalLong(IReadOnlyDictionary<string, string?> fields, string name)
    {
        if (!fields.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new IgStreamingDataException($"IG chart field '{name}' has invalid integer value '{value}'.");
        }

        return parsed;
    }

    private static string ReadRequired(IReadOnlyDictionary<string, string?> fields, string name)
    {
        if (!fields.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new IgStreamingDataException($"IG chart field '{name}' is required.");
        }

        return value;
    }
}
