using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Trading.Abstractions;
using Trading.Strategy.Inputs;
using Trading.Strategy.Persistence;
using Trading.Strategy.Shared;

namespace Trading.Strategy.OpportunityReview;

public sealed class IntradayCandidateDecisionService
{
    private readonly ShadowDecisionPolicy _policy;

    public IntradayCandidateDecisionService(ShadowDecisionPolicy policy)
    {
        policy.Validate();
        _policy = policy;
    }

    public IntradayCandidateDecisionReview Review(
        TradingDayRecord record,
        IntradayOpportunityBatch batch)
    {
        var plan = record.Plan
            ?? throw new InvalidOperationException($"No trading day plan exists for {batch.TradingDate:yyyy-MM-dd}.");
        var watchedMarkets = plan.WatchList
            .ToDictionary(market => market.Instrument.Value, market => market, StringComparer.Ordinal);
        var quoteByInstrument = batch.Quotes
            .GroupBy(quote => quote.Instrument.Value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(quote => quote.LatestPriceAtUtc).First(), StringComparer.Ordinal);
        var handledDecisionIds = record.HandledShadowDecisionIds.ToHashSet(StringComparer.Ordinal);
        var approvedThisRun = new HashSet<string>(StringComparer.Ordinal);
        var decisions = new List<IntradayCandidateDecision>(batch.CandidateOpportunities.Count);

        foreach (var candidate in batch.CandidateOpportunities)
        {
            var decisionId = CreateDecisionId(batch.TradingDate, candidate);
            if (handledDecisionIds.Contains(decisionId) || approvedThisRun.Contains(decisionId))
            {
                decisions.Add(CreateTerminalDecision(
                    candidate,
                    decisionId,
                    IntradayCandidateDecisionStatus.AlreadyProcessed,
                    IntradayCandidateDecisionReason.AlreadyProcessed,
                    "An equivalent candidate has already been handled for this trading day."));
                continue;
            }

            var decision = EvaluateCandidate(
                batch,
                plan,
                watchedMarkets,
                quoteByInstrument,
                candidate,
                decisionId);
            decisions.Add(decision);

            if (decision.Status == IntradayCandidateDecisionStatus.ApprovedForShadowExecution)
            {
                approvedThisRun.Add(decisionId);
            }
        }

        var selectedIntent = SelectIntent(decisions);
        return new IntradayCandidateDecisionReview(
            _policy.Mode,
            decisions,
            selectedIntent,
            Summarize(decisions));
    }

    public static string CreateDecisionId(DateOnly tradingDate, IntradayOpportunityCandidate candidate)
    {
        var canonical = string.Join(
            "|",
            tradingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            candidate.Instrument.Value,
            candidate.Direction,
            candidate.EntryMethod,
            FormatPrice(candidate.EntryPrice),
            FormatPrice(candidate.StopLossPrice),
            FormatPrice(candidate.TakeProfitPrice),
            candidate.SetupExpiresAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return $"dec_{Convert.ToHexString(hashBytes).ToLowerInvariant()}";
    }

    private IntradayCandidateDecision EvaluateCandidate(
        IntradayOpportunityBatch batch,
        TradingDayPlan plan,
        IReadOnlyDictionary<string, MarketWatch> watchedMarkets,
        IReadOnlyDictionary<string, IntradayMarketQuote> quoteByInstrument,
        IntradayOpportunityCandidate candidate,
        string decisionId)
    {
        var quote = quoteByInstrument.TryGetValue(candidate.Instrument.Value, out var foundQuote)
            ? foundQuote
            : new IntradayMarketQuote(candidate.Instrument, candidate.CurrentPrice, candidate.CurrentSpread, batch.LookbackEndUtc);
        var resolvedTradingDate = ResolveTradingDate(batch.LookbackEndUtc);
        var risk = CalculateRisk(candidate);
        var reward = CalculateReward(candidate);
        var recalculatedRewardRisk = risk > 0m && reward > 0m ? reward / risk : (decimal?)null;
        var spreadRiskRatio = risk > 0m ? quote.CurrentSpread / risk : (decimal?)null;
        var priceMovementRiskRatio = risk > 0m ? Math.Abs(quote.CurrentPrice - candidate.EntryPrice) / risk : (decimal?)null;
        var reasons = new List<IntradayCandidateDecisionReason>();

        if (_policy.Mode == TradingExecutionMode.Disabled)
        {
            return CreateRejectedDecision(
                candidate,
                decisionId,
                IntradayCandidateDecisionStatus.Rejected,
                [IntradayCandidateDecisionReason.ExecutionDisabled],
                recalculatedRewardRisk,
                spreadRiskRatio,
                priceMovementRiskRatio,
                "Execution mode is Disabled; no candidate can be approved for shadow execution.");
        }

        if (_policy.Mode != TradingExecutionMode.Shadow)
        {
            return CreateRejectedDecision(
                candidate,
                decisionId,
                IntradayCandidateDecisionStatus.Rejected,
                [IntradayCandidateDecisionReason.ExecutionDisabled],
                recalculatedRewardRisk,
                spreadRiskRatio,
                priceMovementRiskRatio,
                "Phase one can evaluate only Disabled and Shadow execution modes.");
        }

        if (!watchedMarkets.ContainsKey(candidate.Instrument.Value))
        {
            reasons.Add(IntradayCandidateDecisionReason.NotOnActiveWatchlist);
        }

        if (_policy.SupportedInstruments.Count == 0
            || !_policy.SupportedInstruments.Any(instrument => string.Equals(instrument.Value, candidate.Instrument.Value, StringComparison.Ordinal)))
        {
            return CreateRejectedDecision(
                candidate,
                decisionId,
                IntradayCandidateDecisionStatus.UnsupportedByCurrentExecutionScope,
                [IntradayCandidateDecisionReason.UnsupportedInstrument],
                recalculatedRewardRisk,
                spreadRiskRatio,
                priceMovementRiskRatio,
                "The candidate instrument is not allowlisted for the current execution phase.");
        }

        if (!_policy.SupportedEntryMethods.Contains(candidate.EntryMethod))
        {
            return CreateRejectedDecision(
                candidate,
                decisionId,
                IntradayCandidateDecisionStatus.UnsupportedByCurrentExecutionScope,
                [IntradayCandidateDecisionReason.UnsupportedEntryMethod],
                recalculatedRewardRisk,
                spreadRiskRatio,
                priceMovementRiskRatio,
                "The candidate entry method is not supported by the current execution phase.");
        }

        if (batch.TradingDate != resolvedTradingDate)
        {
            reasons.Add(IntradayCandidateDecisionReason.TradingDateMismatch);
        }

        if (candidate.SetupExpiresAtUtc <= batch.ReviewedAtUtc)
        {
            reasons.Add(IntradayCandidateDecisionReason.Expired);
        }

        if (batch.ReviewedAtUtc - quote.LatestPriceAtUtc > _policy.FreshQuoteMaxAge)
        {
            reasons.Add(IntradayCandidateDecisionReason.StaleQuote);
        }

        if (risk <= 0m || reward <= 0m)
        {
            reasons.Add(IntradayCandidateDecisionReason.InvalidPriceGeometry);
        }

        if (recalculatedRewardRisk is null || recalculatedRewardRisk < _policy.MinimumRewardRiskRatio)
        {
            reasons.Add(IntradayCandidateDecisionReason.RewardRiskTooLow);
        }

        if (spreadRiskRatio is null || spreadRiskRatio > _policy.MaxSpreadRiskRatio)
        {
            reasons.Add(IntradayCandidateDecisionReason.SpreadTooWide);
        }

        if (priceMovementRiskRatio is null || priceMovementRiskRatio > _policy.MaxPriceMovementRiskRatio)
        {
            reasons.Add(IntradayCandidateDecisionReason.PriceMovedTooFar);
        }

        if (candidate.OpportunityScore < _policy.MinimumOpportunityScore)
        {
            reasons.Add(IntradayCandidateDecisionReason.OpportunityScoreTooLow);
        }

        if (IsHighImpactEventBlocked(plan, candidate, batch.ReviewedAtUtc))
        {
            reasons.Add(IntradayCandidateDecisionReason.HighImpactEventBlocked);
        }

        if (reasons.Count > 0)
        {
            return CreateRejectedDecision(
                candidate,
                decisionId,
                IntradayCandidateDecisionStatus.Rejected,
                reasons,
                recalculatedRewardRisk,
                spreadRiskRatio,
                priceMovementRiskRatio,
                "The candidate failed one or more deterministic shadow decision checks.");
        }

        var intent = new ExecutionReadyTradeIntent(
            decisionId,
            string.IsNullOrWhiteSpace(batch.SourceDecisionAuditId) ? "unassigned" : batch.SourceDecisionAuditId,
            batch.TradingDate,
            candidate.Instrument,
            candidate.InstrumentName,
            candidate.Direction,
            candidate.EntryMethod,
            candidate.EntryPrice,
            candidate.StopLossPrice,
            candidate.TakeProfitPrice,
            candidate.SetupExpiresAtUtc,
            _policy.QuantityPolicy,
            batch.ReviewedAtUtc,
            [
                "Candidate passed deterministic phase-one shadow checks.",
                "Reward:risk was recalculated from entry, stop, and target.",
            ],
            _policy.ToSnapshot(),
            CreateContext(batch, plan, watchedMarkets, candidate, quote),
            [
                "Shadow mode records intent only; no broker order is submitted.",
                "Quantity is a policy placeholder until broker-aware sizing is implemented.",
                "Execution state is not durable until the phase-two reservation boundary.",
            ]);

        return new IntradayCandidateDecision(
            decisionId,
            candidate.Instrument,
            candidate.Direction,
            candidate.EntryMethod,
            candidate.OpportunityScore,
            IntradayCandidateDecisionStatus.ApprovedForShadowExecution,
            [IntradayCandidateDecisionReason.Approved],
            recalculatedRewardRisk,
            spreadRiskRatio,
            priceMovementRiskRatio,
            "The candidate is approved for shadow execution.",
            intent);
    }

    private IntradayCandidateDecision CreateRejectedDecision(
        IntradayOpportunityCandidate candidate,
        string decisionId,
        IntradayCandidateDecisionStatus status,
        IReadOnlyList<IntradayCandidateDecisionReason> reasons,
        decimal? recalculatedRewardRisk,
        decimal? spreadRiskRatio,
        decimal? priceMovementRiskRatio,
        string explanation)
        => new(
            decisionId,
            candidate.Instrument,
            candidate.Direction,
            candidate.EntryMethod,
            candidate.OpportunityScore,
            status,
            reasons,
            recalculatedRewardRisk,
            spreadRiskRatio,
            priceMovementRiskRatio,
            explanation,
            null);

    private static IntradayCandidateDecision CreateTerminalDecision(
        IntradayOpportunityCandidate candidate,
        string decisionId,
        IntradayCandidateDecisionStatus status,
        IntradayCandidateDecisionReason reason,
        string explanation)
        => new(
            decisionId,
            candidate.Instrument,
            candidate.Direction,
            candidate.EntryMethod,
            candidate.OpportunityScore,
            status,
            [reason],
            null,
            null,
            null,
            explanation,
            null);

    private ExecutionReadyTradeIntent? SelectIntent(IReadOnlyList<IntradayCandidateDecision> decisions)
        => decisions
            .Where(decision => decision.Intent is not null)
            .OrderByDescending(decision => decision.OpportunityScore)
            .ThenByDescending(decision => decision.RecalculatedRewardRiskRatio ?? 0m)
            .ThenBy(decision => decision.SpreadRiskRatio ?? decimal.MaxValue)
            .ThenBy(decision => decision.DecisionId, StringComparer.Ordinal)
            .Select(decision => decision.Intent)
            .FirstOrDefault();

    private static IntradayCandidateDecisionSummary Summarize(IReadOnlyList<IntradayCandidateDecision> decisions)
        => new(
            decisions.Count,
            decisions.Count(decision => decision.Status == IntradayCandidateDecisionStatus.ApprovedForShadowExecution),
            decisions.Count(decision => decision.Status == IntradayCandidateDecisionStatus.Rejected),
            decisions.Count(decision => decision.Status == IntradayCandidateDecisionStatus.AlreadyProcessed),
            decisions.Count(decision => decision.Status == IntradayCandidateDecisionStatus.UnsupportedByCurrentExecutionScope));

    private ShadowDecisionContextSnapshot CreateContext(
        IntradayOpportunityBatch batch,
        TradingDayPlan plan,
        IReadOnlyDictionary<string, MarketWatch> watchedMarkets,
        IntradayOpportunityCandidate candidate,
        IntradayMarketQuote quote)
    {
        var rank = watchedMarkets.TryGetValue(candidate.Instrument.Value, out var watch)
            ? watch.Rank
            : 0;
        return new ShadowDecisionContextSnapshot(
            _policy.TradingTimezone,
            batch.TradingDate,
            ResolveTradingDate(batch.LookbackEndUtc),
            batch.ReviewedAtUtc,
            quote.LatestPriceAtUtc,
            quote.CurrentPrice,
            quote.CurrentSpread,
            rank,
            plan.MarketRegime.ToString());
    }

    private bool IsHighImpactEventBlocked(
        TradingDayPlan plan,
        IntradayOpportunityCandidate candidate,
        DateTimeOffset reviewedAtUtc)
        => plan.CalendarEvents.Any(calendarEvent =>
            calendarEvent.Impact == EconomicEventImpact.High
            && calendarEvent.AffectedInstruments.Any(instrument => string.Equals(instrument.Value, candidate.Instrument.Value, StringComparison.Ordinal))
            && calendarEvent.ScheduledAtUtc >= reviewedAtUtc
            && calendarEvent.ScheduledAtUtc - reviewedAtUtc <= _policy.BlockBeforeHighImpactEvent);

    private DateOnly ResolveTradingDate(DateTimeOffset timestampUtc)
    {
        var timezone = TimeZoneInfo.FindSystemTimeZoneById(_policy.TradingTimezone);
        var localNow = TimeZoneInfo.ConvertTime(timestampUtc, timezone);
        return DateOnly.FromDateTime(localNow.DateTime);
    }

    private static decimal CalculateRisk(IntradayOpportunityCandidate candidate)
        => candidate.Direction == TradeDirection.Buy
            ? candidate.EntryPrice - candidate.StopLossPrice
            : candidate.StopLossPrice - candidate.EntryPrice;

    private static decimal CalculateReward(IntradayOpportunityCandidate candidate)
        => candidate.Direction == TradeDirection.Buy
            ? candidate.TakeProfitPrice - candidate.EntryPrice
            : candidate.EntryPrice - candidate.TakeProfitPrice;

    private static string FormatPrice(decimal value)
        => value.ToString("0.##########", CultureInfo.InvariantCulture);
}

public sealed record IntradayCandidateDecisionReview(
    TradingExecutionMode ExecutionMode,
    IReadOnlyList<IntradayCandidateDecision> Decisions,
    ExecutionReadyTradeIntent? SelectedShadowIntent,
    IntradayCandidateDecisionSummary Summary);
