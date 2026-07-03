using Trading.Abstractions;
using Trading.MarketData;

namespace Trading.Automation.Execution;

public sealed class AuditMarketDataQualityAnalyzer
{
    private readonly IMarketDataStore _marketDataStore;
    private readonly IMarketSessionEvidenceStore _sessionEvidenceStore;

    public AuditMarketDataQualityAnalyzer(
        IMarketDataStore marketDataStore,
        IMarketSessionEvidenceStore sessionEvidenceStore)
    {
        _marketDataStore = marketDataStore;
        _sessionEvidenceStore = sessionEvidenceStore;
    }

    public async Task<AuditDataQualityResult> AnalyzeAsync(
        InstrumentId instrument,
        PriceResolution resolution,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        AuditDataQualityUseCase useCase,
        AuditDataQualityPolicy policy,
        CancellationToken cancellationToken = default)
    {
        var interval = ToInterval(resolution);
        var alignedFromUtc = AlignDown(fromUtc, interval);
        var alignedToUtc = AlignUp(toUtc, interval);
        var allStoredBars = await _marketDataStore.GetRangeAsync(instrument, resolution, alignedFromUtc, alignedToUtc, cancellationToken);
        var coverage = await _marketDataStore.GetCoverageAsync(instrument, resolution, alignedFromUtc, alignedToUtc, cancellationToken);
        var sessionStatus = await _sessionEvidenceStore.GetSessionStatusAsync(instrument, alignedFromUtc, alignedToUtc, cancellationToken);

        var finalBuckets = allStoredBars
            .Where(bar => bar.IsFinal)
            .Select(bar => bar.Bar.TimestampUtc)
            .ToHashSet();
        var nonFinalBuckets = allStoredBars
            .Where(bar => !bar.IsFinal)
            .Select(bar => bar.Bar.TimestampUtc)
            .ToHashSet();

        var expectedBars = 0;
        var finalBars = 0;
        var unknownMissingBars = 0;
        var maxConsecutiveUnknown = 0;
        var currentConsecutiveUnknown = 0;
        var closedMarketBars = 0;
        var abnormalNonTradeableBars = 0;
        var nonFinalOnlyBars = 0;
        var knownNoBarsWithoutSessionBars = 0;
        DateTimeOffset? firstUnknown = null;
        DateTimeOffset? firstTail = null;
        DateTimeOffset? firstClosed = null;
        DateTimeOffset? firstAbnormal = null;

        for (var bucket = alignedFromUtc; bucket < alignedToUtc; bucket = bucket.Add(interval))
        {
            expectedBars++;

            if (finalBuckets.Contains(bucket))
            {
                finalBars++;
                currentConsecutiveUnknown = 0;
                continue;
            }

            var bucketEnd = bucket.Add(interval);
            var status = FindStatus(sessionStatus, bucket, bucketEnd);
            if (status?.Status == MarketStatus.Closed)
            {
                closedMarketBars++;
                firstClosed ??= bucket;
                currentConsecutiveUnknown = 0;
                continue;
            }

            if (status?.Status is MarketStatus.Suspended or MarketStatus.EditsOnly or MarketStatus.Unknown)
            {
                abnormalNonTradeableBars++;
                firstAbnormal ??= bucket;
                currentConsecutiveUnknown = 0;
                continue;
            }

            if (nonFinalBuckets.Contains(bucket))
            {
                nonFinalOnlyBars++;
            }

            if (IsCoveredByNoBars(coverage, bucket))
            {
                knownNoBarsWithoutSessionBars++;
            }

            unknownMissingBars++;
            currentConsecutiveUnknown++;
            maxConsecutiveUnknown = Math.Max(maxConsecutiveUnknown, currentConsecutiveUnknown);
            firstUnknown ??= bucket;

            if (bucketEnd >= alignedToUtc)
            {
                firstTail ??= bucket;
            }
        }

        if (expectedBars == 0)
        {
            return Build(
                AuditDataQualityClassification.NoBars,
                firstUnknown ?? alignedFromUtc,
                "No final market-data bars were available for the audit window.");
        }

        if (policy.StrictData && finalBars != expectedBars)
        {
            return Build(
                firstTail is null ? AuditDataQualityClassification.UnsafeUnknownGaps : AuditDataQualityClassification.InsufficientTailData,
                firstUnknown ?? firstTail ?? firstClosed ?? firstAbnormal ?? alignedFromUtc,
                "Strict data mode requires every expected final bar in the audit window.");
        }

        if (abnormalNonTradeableBars > 0)
        {
            return Build(
                AuditDataQualityClassification.AbnormalNonTradeable,
                firstAbnormal!.Value,
                "Broker session evidence showed an abnormal non-tradeable state in the audit window.");
        }

        if (finalBars == 0)
        {
            return unknownMissingBars == 0 && closedMarketBars > 0
                ? Build(
                    AuditDataQualityClassification.ClosedMarket,
                    firstClosed!.Value,
                    "The audit window had no final bars because broker evidence showed the market was closed.")
                : Build(
                    AuditDataQualityClassification.NoBars,
                    firstUnknown ?? firstClosed ?? alignedFromUtc,
                    "No final market-data bars were available for the audit window.");
        }

        if (unknownMissingBars == 0)
        {
            return closedMarketBars > 0
                ? Build(
                    AuditDataQualityClassification.ClosedMarket,
                    firstClosed!.Value,
                    "Missing buckets were covered by broker closed-market evidence.")
                : Build(
                    AuditDataQualityClassification.Complete,
                    null,
                    "Every expected final bar was available.");
        }

        if (firstTail is not null)
        {
            return Build(
                AuditDataQualityClassification.InsufficientTailData,
                firstUnknown ?? firstTail.Value,
                "The audit window is missing final bars at the tail.");
        }

        if (useCase == AuditDataQualityUseCase.Assessment && IsAssessmentToleranceAllowed())
        {
            return Build(
                AuditDataQualityClassification.EvaluatedWithToleratedGaps,
                firstUnknown!.Value,
                "Assessment window had a small interior data gap within audit tolerance.");
        }

        return Build(
            AuditDataQualityClassification.UnsafeUnknownGaps,
            firstUnknown!.Value,
            knownNoBarsWithoutSessionBars > 0
                ? "IG returned no bars for part of the window, but no session evidence proved the market was closed."
                : "The audit window has missing final bars without broker session evidence.");

        bool IsAssessmentToleranceAllowed()
        {
            var firstExpected = alignedFromUtc;
            var lastExpected = alignedToUtc.Subtract(interval);
            if (firstUnknown is null || firstUnknown.Value <= firstExpected || firstUnknown.Value >= lastExpected)
            {
                return false;
            }

            var ratio = expectedBars == 0 ? 1m : unknownMissingBars / (decimal)expectedBars;
            return unknownMissingBars <= policy.MaxAssessmentInteriorMissingBars
                && maxConsecutiveUnknown <= policy.MaxAssessmentConsecutiveMissingBars
                && ratio <= policy.MaxAssessmentMissingRatio;
        }

        AuditDataQualityResult Build(AuditDataQualityClassification classification, DateTimeOffset? issueStart, string reason)
        {
            var issue = issueStart is null ? null : new MarketDataGap(issueStart.Value, issueStart.Value.Add(interval));
            return new AuditDataQualityResult(
                useCase,
                classification,
                issue,
                expectedBars,
                finalBars,
                unknownMissingBars,
                maxConsecutiveUnknown,
                closedMarketBars,
                abnormalNonTradeableBars,
                nonFinalOnlyBars,
                knownNoBarsWithoutSessionBars,
                reason);
        }
    }

    private static MarketSessionStatusRecord? FindStatus(
        IReadOnlyList<MarketSessionStatusRecord> statuses,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc)
        => statuses
            .Where(status => status.ObservedAtUtc < toUtc && status.ValidUntilUtc > fromUtc)
            .OrderByDescending(status => status.ObservedAtUtc)
            .FirstOrDefault();

    private static bool IsCoveredByNoBars(IReadOnlyList<MarketDataCoverageRecord> coverage, DateTimeOffset bucket)
        => coverage.Any(record => record.Status == MarketDataCoverageStatus.NoBars
            && bucket >= record.FromUtc
            && bucket < record.ToUtc);

    private static TimeSpan ToInterval(PriceResolution resolution)
        => resolution switch
        {
            PriceResolution.Second => TimeSpan.FromSeconds(1),
            PriceResolution.Minute => TimeSpan.FromMinutes(1),
            PriceResolution.TwoMinutes => TimeSpan.FromMinutes(2),
            PriceResolution.ThreeMinutes => TimeSpan.FromMinutes(3),
            PriceResolution.FiveMinutes => TimeSpan.FromMinutes(5),
            PriceResolution.TenMinutes => TimeSpan.FromMinutes(10),
            PriceResolution.FifteenMinutes => TimeSpan.FromMinutes(15),
            PriceResolution.ThirtyMinutes => TimeSpan.FromMinutes(30),
            PriceResolution.Hour => TimeSpan.FromHours(1),
            PriceResolution.TwoHours => TimeSpan.FromHours(2),
            PriceResolution.ThreeHours => TimeSpan.FromHours(3),
            PriceResolution.FourHours => TimeSpan.FromHours(4),
            PriceResolution.Day => TimeSpan.FromDays(1),
            PriceResolution.Week => TimeSpan.FromDays(7),
            PriceResolution.Month => TimeSpan.FromDays(31),
            _ => TimeSpan.FromMinutes(5),
        };

    private static DateTimeOffset AlignDown(DateTimeOffset value, TimeSpan interval)
    {
        var utc = value.ToUniversalTime();
        var remainder = utc.Ticks % interval.Ticks;
        return new DateTimeOffset(new DateTime(utc.Ticks - remainder, DateTimeKind.Utc));
    }

    private static DateTimeOffset AlignUp(DateTimeOffset value, TimeSpan interval)
    {
        var aligned = AlignDown(value, interval);
        return aligned == value.ToUniversalTime() ? aligned : aligned.Add(interval);
    }
}
