using Ig.Trading.Sdk.Streaming;

namespace Trading.IG;

internal sealed class IgChartCandleUpdateAccumulator
{
    private static readonly string[] RequiredFields =
    [
        "UTM",
        "BID_OPEN",
        "BID_HIGH",
        "BID_LOW",
        "BID_CLOSE",
        "OFR_OPEN",
        "OFR_HIGH",
        "OFR_LOW",
        "OFR_CLOSE",
        "CONS_END",
    ];

    private readonly object _sync = new();
    private readonly Dictionary<string, Dictionary<string, string?>> _fieldsByItem = new(StringComparer.Ordinal);

    public IgChartCandleUpdate? Apply(
        string epic,
        string scale,
        IReadOnlyDictionary<string, string?> fields)
    {
        lock (_sync)
        {
            var itemKey = $"{epic}:{scale}";
            var current = GetOrCreateFields(itemKey);
            var hasIncomingUtm = fields.TryGetValue("UTM", out var incomingUtm)
                && !string.IsNullOrWhiteSpace(incomingUtm);

            if (!hasIncomingUtm && !current.ContainsKey("UTM"))
            {
                return null;
            }

            if (hasIncomingUtm
                && current.TryGetValue("UTM", out var currentUtm)
                && !string.Equals(currentUtm, incomingUtm, StringComparison.Ordinal))
            {
                current.Clear();
            }

            foreach (var field in fields)
            {
                if (!string.IsNullOrWhiteSpace(field.Value))
                {
                    current[field.Key] = field.Value;
                }
            }

            return HasRequiredFields(current)
                ? IgChartCandleMapper.Map(epic, scale, current)
                : null;
        }
    }

    private Dictionary<string, string?> GetOrCreateFields(string itemKey)
    {
        if (!_fieldsByItem.TryGetValue(itemKey, out var fields))
        {
            fields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            _fieldsByItem[itemKey] = fields;
        }

        return fields;
    }

    private static bool HasRequiredFields(IReadOnlyDictionary<string, string?> fields)
        => RequiredFields.All(field => fields.TryGetValue(field, out var value) && !string.IsNullOrWhiteSpace(value));
}
