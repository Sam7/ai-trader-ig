using System.Globalization;
using System.Text;
using Trading.AI.Prompts.IntradayOpportunityReview;
using Trading.Strategy.Inputs;
using Trading.Strategy.Shared;

namespace Trading.AI.DailyBriefing;

internal static class IntradayOpportunityPromptInputFactory
{
    public static IntradayOpportunityReviewInput Create(IntradayOpportunityReviewRequest request)
        => new(
            request.TradingDate,
            request.LookbackStartUtc,
            request.LookbackEndUtc,
            request.Markets.Count,
            request.MaxCandidatesPerRun,
            request.TradingTimezone,
            FormatDailyPlanSummary(request.DailyPlan),
            FormatWatchedMarketsContext(request.Markets),
            FormatCalendarEventsContext(request.DailyPlan.CalendarEvents),
            request.TradingDate,
            request.RequestedAtUtc);

    private static string FormatDailyPlanSummary(TradingDayPlan plan)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Market regime: {plan.MarketRegime}");
        builder.AppendLine($"Macro summary: {plan.MacroSummary}");
        builder.AppendLine($"Regime summary: {plan.MarketRegimeSummary}");
        return builder.ToString().TrimEnd();
    }

    private static string FormatWatchedMarketsContext(IReadOnlyList<IntradayMarketReviewContext> markets)
    {
        var builder = new StringBuilder();
        foreach (var market in markets.OrderBy(market => market.Rank))
        {
            builder.AppendLine($"## Rank {market.Rank}: {market.InstrumentName}");
            builder.AppendLine($"Instrument ID: {market.Instrument.Value}");
            builder.AppendLine($"Current bid: {market.CurrentBid.ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"Current ask: {market.CurrentAsk.ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"Current mid price: {market.CurrentPrice.ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"Current spread: {market.CurrentSpread.ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"Latest price timestamp UTC: {market.LatestBarAtUtc:O}");
            builder.AppendLine($"Daily rationale: {market.Rationale}");
            builder.AppendLine($"Long scenario thesis: {market.LongScenario.Thesis}");
            builder.AppendLine($"Long confirmation: {market.LongScenario.Confirmation}");
            builder.AppendLine($"Long invalidation: {market.LongScenario.Invalidation}");
            builder.AppendLine($"Short scenario thesis: {market.ShortScenario.Thesis}");
            builder.AppendLine($"Short confirmation: {market.ShortScenario.Confirmation}");
            builder.AppendLine($"Short invalidation: {market.ShortScenario.Invalidation}");
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static string FormatCalendarEventsContext(IReadOnlyList<EconomicEvent> calendarEvents)
    {
        if (calendarEvents.Count == 0)
        {
            return "No scheduled calendar events were captured in the daily plan.";
        }

        var builder = new StringBuilder();
        foreach (var calendarEvent in calendarEvents.OrderBy(calendarEvent => calendarEvent.ScheduledAtUtc))
        {
            builder.AppendLine(
                $"- {calendarEvent.Id} | {calendarEvent.ScheduledAtUtc:O} | {calendarEvent.Impact} | {calendarEvent.Title} | affected instruments: {string.Join(", ", calendarEvent.AffectedInstruments.Select(instrument => instrument.Value))}");
        }

        return builder.ToString().TrimEnd();
    }
}
