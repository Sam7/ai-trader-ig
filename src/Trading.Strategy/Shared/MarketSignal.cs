namespace Trading.Strategy.Shared;

public enum MarketSignal
{
    EntryZoneTouched = 0,
    VolatilityExpanded = 1,
    FreshHeadline = 2,
    ScheduledEventReleased = 3,
    ThesisInvalidated = 4,
    OpenTradeAnomaly = 5,
}
