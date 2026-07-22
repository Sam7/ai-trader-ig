namespace Trading.MarketData;

public enum MarketDataSource
{
    Unknown = 0,
    LocalCache = 1,
    Stream = 2,
    RestBackfill = 3,
    ManualImport = 4,
    CloudMirror = 5,
}

public enum MarketDataResultSource
{
    None = 0,
    LocalCache = 1,
}

public enum MarketDataStatus
{
    Completed = 0,
    Partial = 1,
    UnsupportedResolution = 2,
}
