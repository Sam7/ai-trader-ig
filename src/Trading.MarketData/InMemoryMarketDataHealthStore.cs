using Trading.Abstractions;

namespace Trading.MarketData;

public sealed class InMemoryMarketDataHealthStore : IMarketDataHealthStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<(string Instrument, PriceResolution Resolution), MarketDataHealthRecord> _records = [];

    public async Task UpsertAsync(MarketDataHealthRecord health, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _records[(health.Instrument.Value, health.Resolution)] = health;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MarketDataHealthRecord?> GetAsync(
        InstrumentId instrument,
        PriceResolution resolution,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return _records.TryGetValue((instrument.Value, resolution), out var health)
                ? health
                : null;
        }
        finally
        {
            _gate.Release();
        }
    }
}
