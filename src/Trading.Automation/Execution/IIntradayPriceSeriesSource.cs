using Trading.Abstractions;

namespace Trading.Automation.Execution;

public interface IIntradayPriceSeriesSource
{
    Task<CachedPriceSeriesResult> GetSeriesAsync(
        InstrumentId instrument,
        DateTimeOffset requestedAtUtc,
        int chartLookbackHours,
        PriceResolution resolution,
        CancellationToken cancellationToken = default);
}
