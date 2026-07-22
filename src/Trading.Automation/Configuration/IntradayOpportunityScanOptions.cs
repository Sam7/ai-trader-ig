using Trading.Abstractions;

namespace Trading.Automation.Configuration;

public sealed class IntradayOpportunityScanOptions
{
    public bool Enabled { get; init; } = true;

    public string Cron { get; init; } = "0 */15 * * * *";

    public int LookbackMinutes { get; init; } = 60;

    public int ChartLookbackHours { get; init; } = 96;

    public PriceResolution ChartResolution { get; init; } = PriceResolution.TenMinutes;

    public int FreshPriceMaxAgeMinutes { get; init; } = 20;

    /// <summary>
    /// Allows a diagnostics-only run to render incomplete or stale local price history.
    /// Production remains fail-closed unless this is explicitly enabled.
    /// </summary>
    public bool AllowStalePriceDataForDiagnostics { get; init; }

    public int MaxCandidatesPerRun { get; init; } = 4;

    public void Validate()
    {
        if (LookbackMinutes <= 0)
        {
            throw new InvalidOperationException("Intraday opportunity lookback minutes must be greater than zero.");
        }

        if (ChartLookbackHours <= 0)
        {
            throw new InvalidOperationException("Intraday chart lookback hours must be greater than zero.");
        }

        if (FreshPriceMaxAgeMinutes <= 0)
        {
            throw new InvalidOperationException("Intraday fresh price max age minutes must be greater than zero.");
        }

        if (MaxCandidatesPerRun <= 0)
        {
            throw new InvalidOperationException("Intraday max candidates per run must be greater than zero.");
        }
    }
}
