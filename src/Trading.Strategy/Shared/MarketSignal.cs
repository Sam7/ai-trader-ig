namespace Trading.Strategy.Shared;

public enum MarketSignal
{
    VolatilityExpanded = 0,
    FreshHeadline = 1,
    ScheduledEventReleased = 2,
    ThesisInvalidated = 3,
    OpenTradeAnomaly = 4,
}
