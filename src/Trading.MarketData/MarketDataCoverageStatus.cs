namespace Trading.MarketData;

public enum MarketDataCoverageStatus
{
    Unknown = 0,
    NoBars = 1,
    BarsReturned = 2,
    Failed = 3,
    AllowanceBlocked = 4,
}
