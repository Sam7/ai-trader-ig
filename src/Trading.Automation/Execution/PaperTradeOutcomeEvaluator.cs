using Trading.Abstractions;
using Trading.Strategy.Shared;

namespace Trading.Automation.Execution;

public sealed class PaperTradeOutcomeEvaluator
{
    public PaperTradeOutcome Evaluate(
        DecisionAuditCandidate candidate,
        DateTimeOffset reviewedAtUtc,
        PriceSeries series,
        DateTimeOffset evaluatedAtUtc)
    {
        var bars = series.Bars
            .Where(bar => bar.TimestampUtc >= reviewedAtUtc && bar.TimestampUtc <= candidate.SetupExpiresAtUtc)
            .OrderBy(bar => bar.TimestampUtc)
            .ToArray();

        if (bars.Length == 0)
        {
            return CreateOutcome(
                candidate,
                PaperTradeOutcomeStatus.DataInsufficient,
                evaluatedAtUtc,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                0,
                "No post-signal price bars were available before setup expiry.");
        }

        var risk = Math.Abs(candidate.EntryPrice - candidate.StopLossPrice);
        if (risk <= 0m)
        {
            return CreateOutcome(
                candidate,
                PaperTradeOutcomeStatus.DataInsufficient,
                evaluatedAtUtc,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                bars.Length,
                "Candidate risk distance was zero or negative.");
        }

        var entryBar = ResolveEntryBar(candidate, bars);
        if (entryBar is null)
        {
            if (bars[^1].TimestampUtc < candidate.SetupExpiresAtUtc)
            {
                return CreateOutcome(
                    candidate,
                    PaperTradeOutcomeStatus.DataInsufficient,
                    evaluatedAtUtc,
                    null,
                    bars[^1].TimestampUtc,
                    null,
                    null,
                    null,
                    null,
                    null,
                    bars.Length,
                    "Local market data did not cover the setup through expiry.");
            }

            return CreateOutcome(
                candidate,
                PaperTradeOutcomeStatus.NoFill,
                evaluatedAtUtc,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                bars.Length,
                "Entry was not reached before setup expiry.");
        }

        var filledAtUtc = entryBar.TimestampUtc;
        var tradeBars = bars
            .Where(bar => bar.TimestampUtc >= filledAtUtc)
            .ToArray();
        var excursions = CalculateExcursions(candidate, tradeBars);
        var ignoredEntryBarTarget = false;

        foreach (var bar in tradeBars)
        {
            var stopTouched = IsStopTouched(candidate, bar);
            var targetTouched = IsTargetTouched(candidate, bar);

            if (stopTouched)
            {
                return CreateOutcome(
                    candidate,
                    PaperTradeOutcomeStatus.StoppedOut,
                    evaluatedAtUtc,
                    filledAtUtc,
                    bar.TimestampUtc,
                    candidate.EntryPrice,
                    candidate.StopLossPrice,
                    -1m,
                    excursions.MaxFavorable,
                    excursions.MaxAdverse,
                    bars.Length,
                    targetTouched
                        ? "Stop and target were both touched in the same bar; stop was chosen conservatively."
                        : "Stop-loss was touched before target.");
            }

            if (targetTouched)
            {
                if (candidate.EntryMethod != TradeEntryMethod.Market && bar.TimestampUtc == filledAtUtc)
                {
                    ignoredEntryBarTarget = true;
                    continue;
                }

                return CreateOutcome(
                    candidate,
                    PaperTradeOutcomeStatus.TargetHit,
                    evaluatedAtUtc,
                    filledAtUtc,
                    bar.TimestampUtc,
                    candidate.EntryPrice,
                    candidate.TakeProfitPrice,
                    Math.Abs(candidate.TakeProfitPrice - candidate.EntryPrice) / risk,
                    excursions.MaxFavorable,
                    excursions.MaxAdverse,
                    bars.Length,
                    "Take-profit was touched before stop-loss.");
            }
        }

        var finalBar = tradeBars[^1];
        if (finalBar.TimestampUtc < candidate.SetupExpiresAtUtc)
        {
            return CreateOutcome(
                candidate,
                PaperTradeOutcomeStatus.DataInsufficient,
                evaluatedAtUtc,
                filledAtUtc,
                finalBar.TimestampUtc,
                candidate.EntryPrice,
                null,
                null,
                excursions.MaxFavorable,
                excursions.MaxAdverse,
                bars.Length,
                "Local market data did not cover the setup through expiry.");
        }

        var exitPrice = candidate.Direction == TradeDirection.Buy
            ? finalBar.BidClose
            : finalBar.AskClose;
        var estimatedR = candidate.Direction == TradeDirection.Buy
            ? (exitPrice - candidate.EntryPrice) / risk
            : (candidate.EntryPrice - exitPrice) / risk;

        return CreateOutcome(
            candidate,
            PaperTradeOutcomeStatus.Expired,
            evaluatedAtUtc,
            filledAtUtc,
            finalBar.TimestampUtc,
            candidate.EntryPrice,
            exitPrice,
            estimatedR,
            excursions.MaxFavorable,
            excursions.MaxAdverse,
            bars.Length,
            ignoredEntryBarTarget
                ? "Setup expired after entry without a confirmed post-entry target touch. A target touch on the entry bar was ignored because OHLC sequencing is unknown for non-market entries."
                : "Setup expired after entry without touching stop-loss or take-profit.");
    }

    private static PriceBar? ResolveEntryBar(DecisionAuditCandidate candidate, IReadOnlyList<PriceBar> bars)
    {
        if (candidate.EntryMethod == TradeEntryMethod.Market)
        {
            return bars[0];
        }

        return bars.FirstOrDefault(bar => candidate.EntryMethod switch
        {
            TradeEntryMethod.Limit => candidate.Direction == TradeDirection.Buy
                ? bar.AskLow <= candidate.EntryPrice
                : bar.BidHigh >= candidate.EntryPrice,
            TradeEntryMethod.StopEntry => candidate.Direction == TradeDirection.Buy
                ? bar.AskHigh >= candidate.EntryPrice
                : bar.BidLow <= candidate.EntryPrice,
            _ => false,
        });
    }

    private static bool IsStopTouched(DecisionAuditCandidate candidate, PriceBar bar)
        => candidate.Direction == TradeDirection.Buy
            ? bar.BidLow <= candidate.StopLossPrice
            : bar.AskHigh >= candidate.StopLossPrice;

    private static bool IsTargetTouched(DecisionAuditCandidate candidate, PriceBar bar)
        => candidate.Direction == TradeDirection.Buy
            ? bar.BidHigh >= candidate.TakeProfitPrice
            : bar.AskLow <= candidate.TakeProfitPrice;

    private static (decimal MaxFavorable, decimal MaxAdverse) CalculateExcursions(
        DecisionAuditCandidate candidate,
        IReadOnlyList<PriceBar> bars)
    {
        if (candidate.Direction == TradeDirection.Buy)
        {
            return (
                Math.Max(0m, bars.Max(bar => bar.BidHigh) - candidate.EntryPrice),
                Math.Max(0m, candidate.EntryPrice - bars.Min(bar => bar.BidLow)));
        }

        return (
            Math.Max(0m, candidate.EntryPrice - bars.Min(bar => bar.AskLow)),
            Math.Max(0m, bars.Max(bar => bar.AskHigh) - candidate.EntryPrice));
    }

    private static PaperTradeOutcome CreateOutcome(
        DecisionAuditCandidate candidate,
        PaperTradeOutcomeStatus status,
        DateTimeOffset evaluatedAtUtc,
        DateTimeOffset? filledAtUtc,
        DateTimeOffset? closedAtUtc,
        decimal? entryPrice,
        decimal? exitPrice,
        decimal? estimatedRMultiple,
        decimal? maxFavorableExcursion,
        decimal? maxAdverseExcursion,
        int barsEvaluated,
        string reason)
    {
        var risk = Math.Abs(candidate.EntryPrice - candidate.StopLossPrice);
        return new PaperTradeOutcome(
            candidate.Instrument,
            candidate.Direction,
            status,
            evaluatedAtUtc,
            filledAtUtc,
            closedAtUtc,
            entryPrice,
            exitPrice,
            estimatedRMultiple,
            maxFavorableExcursion,
            maxAdverseExcursion,
            candidate.CurrentSpread,
            risk > 0m ? candidate.CurrentSpread / risk : null,
            barsEvaluated,
            reason);
    }
}
