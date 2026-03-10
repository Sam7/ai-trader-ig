using System.Globalization;

public sealed class CliOptions
{
    private readonly Dictionary<string, string> _values;

    private CliOptions(Dictionary<string, string> values)
    {
        _values = values;
    }

    public static CliOptions Parse(IEnumerable<string> args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? currentKey = null;

        foreach (var arg in args)
        {
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                currentKey = arg[2..];
                values[currentKey] = string.Empty;
                continue;
            }

            if (currentKey is null)
            {
                continue;
            }

            values[currentKey] = arg;
            currentKey = null;
        }

        return new CliOptions(values);
    }

    public string Required(string key)
    {
        if (!_values.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Missing required option --{key}");
        }

        return value;
    }

    public decimal RequiredDecimal(string key)
    {
        var raw = Required(key);
        if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var result))
        {
            throw new ArgumentException($"Option --{key} must be a decimal value.");
        }

        return result;
    }

    public bool TryGet(string key, out string? value)
    {
        if (_values.TryGetValue(key, out var found) && !string.IsNullOrWhiteSpace(found))
        {
            value = found;
            return true;
        }

        value = null;
        return false;
    }

    public bool TryGetInt(string key, out int value)
    {
        value = default;
        return TryGet(key, out var raw) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    public bool TryGetDecimal(string key, out decimal value)
    {
        value = default;
        return TryGet(key, out var raw) && decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    public bool TryGetDateTimeOffset(string key, out DateTimeOffset value)
    {
        value = default;
        return TryGet(key, out var raw)
               && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out value);
    }
}
