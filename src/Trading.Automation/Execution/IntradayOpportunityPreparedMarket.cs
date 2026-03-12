namespace Trading.Automation.Execution;

public sealed record IntradayOpportunityPreparedMarket(
    string InstrumentId,
    string InstrumentName,
    int Rank,
    decimal CurrentBid,
    decimal CurrentAsk,
    decimal CurrentPrice,
    decimal CurrentSpread,
    DateTimeOffset LatestBarAtUtc,
    PriceSeriesRefreshMode PriceSeriesRefreshMode,
    int FetchedBarCount,
    ArtifactReference ChartArtifact);
