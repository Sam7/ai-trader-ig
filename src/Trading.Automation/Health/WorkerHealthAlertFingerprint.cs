namespace Trading.Automation.Health;

internal static class WorkerHealthAlertFingerprint
{
    public static string Create(WorkerHealthStatus status, IReadOnlyList<string> reasons)
    {
        ArgumentNullException.ThrowIfNull(reasons);

        var conditionKeys = reasons
            .Select(NormalizeReason)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return $"{status}:{string.Join('|', conditionKeys)}";
    }

    private static string NormalizeReason(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (reason.StartsWith("Working set is critical", StringComparison.Ordinal))
        {
            return "working-set-critical";
        }

        if (reason.StartsWith("Working set is elevated", StringComparison.Ordinal))
        {
            return "working-set-warning";
        }

        if (reason.StartsWith("Historical recovery is blocked by IG allowance", StringComparison.Ordinal))
        {
            return "historical-recovery-allowance";
        }

        if (reason.Equals("Market-data stream queue depth is critical.", StringComparison.Ordinal))
        {
            return "stream-queue-critical";
        }

        if (reason.Equals("Market-data stream queue depth is elevated.", StringComparison.Ordinal))
        {
            return "stream-queue-warning";
        }

        if (reason.Equals("One or more final market-data candles were rejected by the stream dispatcher.", StringComparison.Ordinal))
        {
            return "stream-final-candle-rejected";
        }

        if (reason.Equals("No final market-data bar is available for configured instruments.", StringComparison.Ordinal))
        {
            return "no-final-market-data-bar";
        }

        return reason.Trim();
    }
}
