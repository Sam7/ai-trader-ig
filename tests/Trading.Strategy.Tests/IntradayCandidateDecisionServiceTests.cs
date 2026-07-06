using FluentAssertions;
using Trading.Abstractions;
using Trading.Strategy.Inputs;
using Trading.Strategy.OpportunityReview;
using Trading.Strategy.Persistence;
using Trading.Strategy.Shared;

namespace Trading.Strategy.Tests;

public sealed class IntradayCandidateDecisionServiceTests
{
    private static readonly InstrumentId TestInstrument = new("CC.D.TEST.IP");
    private static readonly DateOnly TradingDate = new(2026, 03, 12);
    private static readonly DateTimeOffset ReviewedAtUtc = DateTimeOffset.Parse("2026-03-12T01:00:00Z");

    [Fact]
    public void Review_WithValidShadowCandidate_ShouldApproveIntentAndRecalculateRewardRisk()
    {
        var candidate = CreateCandidate(rewardRiskRatio: 99m);
        var review = CreateService().Review(CreateRecord(), CreateBatch(candidate));

        review.SelectedShadowIntent.Should().NotBeNull();
        review.Summary.Approved.Should().Be(1);
        var decision = review.Decisions.Should().ContainSingle().Subject;
        decision.Status.Should().Be(IntradayCandidateDecisionStatus.ApprovedForShadowExecution);
        decision.RecalculatedRewardRiskRatio.Should().Be(2m);
        decision.Intent!.SourceDecisionAuditId.Should().Be("audit-1");
        decision.Intent.QuantityPolicy.Should().Be("BrokerMinimum");
        decision.Intent.EntryMethod.Should().Be(TradeEntryMethod.Market);
        decision.Intent.StopLossPrice.Should().Be(candidate.StopLossPrice);
        decision.Intent.TakeProfitPrice.Should().Be(candidate.TakeProfitPrice);
    }

    [Theory]
    [MemberData(nameof(RejectionCases))]
    public void Review_WithInvalidCandidate_ShouldRejectWithDeterministicReason(
        IntradayOpportunityCandidate candidate,
        IntradayMarketQuote quote,
        IntradayCandidateDecisionReason expectedReason)
    {
        var review = CreateService().Review(CreateRecord(), CreateBatch(candidate, quote));

        var decision = review.Decisions.Should().ContainSingle().Subject;
        decision.Status.Should().Be(IntradayCandidateDecisionStatus.Rejected);
        decision.Reasons.Should().Contain(expectedReason);
        decision.Intent.Should().BeNull();
        review.SelectedShadowIntent.Should().BeNull();
    }

    [Fact]
    public void Review_WithUnsupportedEntryMethod_ShouldReturnUnsupportedScope()
    {
        var review = CreateService().Review(
            CreateRecord(),
            CreateBatch(CreateCandidate(entryMethod: TradeEntryMethod.Limit)));

        var decision = review.Decisions.Should().ContainSingle().Subject;
        decision.Status.Should().Be(IntradayCandidateDecisionStatus.UnsupportedByCurrentExecutionScope);
        decision.Reasons.Should().Contain(IntradayCandidateDecisionReason.UnsupportedEntryMethod);
    }

    [Fact]
    public void Review_WithPreviouslyHandledDecisionId_ShouldReturnAlreadyProcessed()
    {
        var candidate = CreateCandidate();
        var decisionId = IntradayCandidateDecisionService.CreateDecisionId(TradingDate, candidate);
        var record = CreateRecord() with { HandledShadowDecisionIds = [decisionId] };

        var review = CreateService().Review(record, CreateBatch(candidate));

        var decision = review.Decisions.Should().ContainSingle().Subject;
        decision.Status.Should().Be(IntradayCandidateDecisionStatus.AlreadyProcessed);
        decision.Reasons.Should().Contain(IntradayCandidateDecisionReason.AlreadyProcessed);
    }

    [Fact]
    public void Review_ShouldResolveTradingDateInConfiguredTimezone()
    {
        var batch = CreateBatch(CreateCandidate()) with
        {
            TradingDate = new DateOnly(2026, 03, 11),
            LookbackEndUtc = DateTimeOffset.Parse("2026-03-11T13:30:00Z"),
            ReviewedAtUtc = DateTimeOffset.Parse("2026-03-11T13:31:00Z"),
            MarketQuotes =
            [
                new IntradayMarketQuote(
                    TestInstrument,
                    100m,
                    0.2m,
                    DateTimeOffset.Parse("2026-03-11T13:30:00Z")),
            ],
        };

        var review = CreateService().Review(CreateRecord(), batch);

        review.Decisions[0].Reasons.Should().Contain(IntradayCandidateDecisionReason.TradingDateMismatch);
    }

    [Fact]
    public void Review_WithHighImpactEventInsideBlockWindow_ShouldReject()
    {
        var record = CreateRecord([
            new EconomicEvent(
                "event-1",
                "High impact release",
                ReviewedAtUtc.AddMinutes(15),
                EconomicEventImpact.High,
                [TestInstrument]),
        ]);

        var review = CreateService().Review(record, CreateBatch(CreateCandidate()));

        review.Decisions[0].Reasons.Should().Contain(IntradayCandidateDecisionReason.HighImpactEventBlocked);
    }

    [Fact]
    public void Review_WithMultipleApprovedCandidates_ShouldSelectHighestScoreDeterministically()
    {
        var lowerScore = CreateCandidate(opportunityScore: 80, takeProfitPrice: 120m);
        var higherScore = CreateCandidate(opportunityScore: 90, entryPrice: 101m, stopLossPrice: 96m, takeProfitPrice: 111m);

        var review = CreateService().Review(CreateRecord(), CreateBatch([lowerScore, higherScore]));

        review.Summary.Approved.Should().Be(2);
        review.SelectedShadowIntent!.ExpectedEntryPrice.Should().Be(101m);
        review.SelectedShadowIntent.DecisionId.Should().Be(
            IntradayCandidateDecisionService.CreateDecisionId(TradingDate, higherScore));
    }

    public static IEnumerable<object[]> RejectionCases()
    {
        yield return
        [
            CreateCandidate(setupExpiresAtUtc: ReviewedAtUtc.AddMinutes(-1)),
            CreateQuote(),
            IntradayCandidateDecisionReason.Expired,
        ];
        yield return
        [
            CreateCandidate(),
            CreateQuote(latestPriceAtUtc: ReviewedAtUtc.AddMinutes(-30)),
            IntradayCandidateDecisionReason.StaleQuote,
        ];
        yield return
        [
            CreateCandidate(stopLossPrice: 105m),
            CreateQuote(),
            IntradayCandidateDecisionReason.InvalidPriceGeometry,
        ];
        yield return
        [
            CreateCandidate(takeProfitPrice: 107m),
            CreateQuote(),
            IntradayCandidateDecisionReason.RewardRiskTooLow,
        ];
        yield return
        [
            CreateCandidate(),
            CreateQuote(currentSpread: 2m),
            IntradayCandidateDecisionReason.SpreadTooWide,
        ];
        yield return
        [
            CreateCandidate(),
            CreateQuote(currentPrice: 102m),
            IntradayCandidateDecisionReason.PriceMovedTooFar,
        ];
        yield return
        [
            CreateCandidate(opportunityScore: 69),
            CreateQuote(),
            IntradayCandidateDecisionReason.OpportunityScoreTooLow,
        ];
    }

    private static IntradayCandidateDecisionService CreateService()
        => new(new ShadowDecisionPolicy(
            TradingExecutionMode.Shadow,
            "Australia/Melbourne",
            [TestInstrument],
            [TradeEntryMethod.Market],
            70,
            2m,
            0.20m,
            0.25m,
            TimeSpan.FromMinutes(20),
            TimeSpan.FromMinutes(30),
            "BrokerMinimum"));

    private static TradingDayRecord CreateRecord(IReadOnlyList<EconomicEvent>? events = null)
        => TradingDayRecord.StartNew(new TradingDayPlan(
            TradingDate,
            "Macro summary",
            "Mixed session",
            MarketRegime.Mixed,
            [CreateWatch(1)],
            [CreateWatch(1)],
            events ?? [],
            ReviewedAtUtc.AddHours(-1)));

    private static MarketWatch CreateWatch(int rank)
        => new(
            TestInstrument,
            rank,
            "Watch rationale",
            new TradeScenario(TradeDirection.Buy, "Long", "Confirm", "Invalidate", [], null),
            new TradeScenario(TradeDirection.Sell, "Short", "Confirm", "Invalidate", [], null));

    private static IntradayOpportunityBatch CreateBatch(IntradayOpportunityCandidate candidate, IntradayMarketQuote? quote = null)
        => CreateBatch([candidate], quote);

    private static IntradayOpportunityBatch CreateBatch(
        IReadOnlyList<IntradayOpportunityCandidate> candidates,
        IntradayMarketQuote? quote = null)
        => new(
            TradingDate,
            ReviewedAtUtc,
            ReviewedAtUtc.AddMinutes(-60),
            ReviewedAtUtc.AddMinutes(-1),
            [
                new IntradayMarketAssessment(
                    TestInstrument,
                    "Test Market",
                    80,
                    TradeDirection.Buy,
                    "Constructive",
                    "Momentum improved",
                    ""),
            ],
            candidates,
            [quote ?? CreateQuote()],
            "audit-1");

    private static IntradayMarketQuote CreateQuote(
        decimal currentPrice = 100m,
        decimal currentSpread = 0.2m,
        DateTimeOffset? latestPriceAtUtc = null)
        => new(
            TestInstrument,
            currentPrice,
            currentSpread,
            latestPriceAtUtc ?? ReviewedAtUtc.AddMinutes(-1));

    private static IntradayOpportunityCandidate CreateCandidate(
        int opportunityScore = 80,
        TradeEntryMethod entryMethod = TradeEntryMethod.Market,
        decimal entryPrice = 100m,
        decimal stopLossPrice = 95m,
        decimal takeProfitPrice = 110m,
        decimal rewardRiskRatio = 2m,
        DateTimeOffset? setupExpiresAtUtc = null)
        => new(
            TestInstrument,
            "Test Market",
            TradeDirection.Buy,
            opportunityScore,
            entryMethod,
            entryPrice,
            stopLossPrice,
            takeProfitPrice,
            rewardRiskRatio,
            100m,
            0.2m,
            "Thesis",
            "Invalidation",
            "Why now",
            setupExpiresAtUtc ?? ReviewedAtUtc.AddMinutes(30));
}
