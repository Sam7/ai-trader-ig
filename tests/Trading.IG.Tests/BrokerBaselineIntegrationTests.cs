using FluentAssertions;
using Ig.Trading.Sdk.Errors;
using Ig.Trading.Sdk.Models;
using Trading.Abstractions;
using SdkCreatePositionRequest = Ig.Trading.Sdk.Models.CreatePositionRequest;

namespace Trading.IG.Tests;

public class BrokerBaselineIntegrationTests
{
    [OptionalIntegrationFact("RUN_IG_BROKER_BASELINE", "phase-zero broker baseline")]
    [Trait("Category", "BrokerBaseline")]
    public async Task PhaseZeroBrokerBaseline_ShouldRecordPositionLifecycleScenarios()
    {
        var evidence = BrokerBaselineEvidenceWriter.Create();
        await using var context = await IgDemoIntegrationContext.CreateAsync();

        var session = await context.AuthenticateAsync();
        await evidence.RecordAsync(
            "preflight-demo-safety",
            "authenticate",
            "passed",
            new { session.BrokerName, Account = Redact(session.AccountId), session.AuthenticatedAtUtc });

        var epic = await SelectCanaryEpicAsync(context, evidence);
        var market = await context.IgTradingApi.GetMarketByEpicAsync(epic);
        var size = ResolveBaselineSize(market, context.Size);

        await evidence.RecordAsync(
            "preflight-demo-safety",
            "market-details",
            "passed",
            new
            {
                Epic = epic,
                market.Instrument.Name,
                market.Snapshot.MarketStatus,
                market.Snapshot.Bid,
                market.Snapshot.Offer,
                Size = size,
                MinDealSize = market.DealingRules?.MinDealSize,
                MinStopOrLimitDistance = market.DealingRules?.MinNormalStopOrLimitDistance,
                market.DealingRules?.MarketOrderPreference,
            });

        await AssertNoExistingCanaryExposureAsync(context, evidence, epic);

        var observedProtection = await RunGatewayOpenProtectCloseAsync(context, evidence, epic, size);
        await RunAtomicProtectedOpenCloseAsync(context, evidence, epic, size, observedProtection.StopLevel, observedProtection.LimitLevel);
        await RunInvalidSizeRejectionAsync(context, evidence, epic, size);
        await RunInvalidProtectionRejectionAsync(context, evidence, epic, size);
        await AssertNoExistingCanaryExposureAsync(context, evidence, epic);

        File.Exists(evidence.SummaryPath).Should().BeTrue();
    }

    private static async Task<(decimal StopLevel, decimal LimitLevel)> RunGatewayOpenProtectCloseAsync(
        IgDemoIntegrationContext context,
        BrokerBaselineEvidenceWriter evidence,
        string epic,
        decimal size)
    {
        const string scenario = "market-open-protect-close";
        var before = await context.Gateway.GetOpenPositionsAsync();
        var beforeIds = before.Select(position => position.DealId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var opened = await context.Gateway.PlaceMarketOrderAsync(new PlaceOrderRequest(
            new InstrumentId(epic),
            TradeDirection.Buy,
            size));

        await evidence.RecordAsync(scenario, "open-submitted", opened.Status.ToString(), opened);
        opened.Status.Should().NotBe(OrderStatus.Rejected);

        var dealId = await ResolveOpenedDealIdAsync(context, opened.DealReference, beforeIds);
        context.PositionDealId = dealId;

        await context.WaitForPositionPresenceAsync(dealId, shouldExist: true, TimeSpan.FromSeconds(30));
        await evidence.RecordAsync(scenario, "position-visible", "passed", new { DealId = dealId });

        var (stopLevel, limitLevel) = await context.CreateValidProtectionLevelsAsync(dealId);
        var updated = await context.Gateway.UpdatePositionAsync(new Trading.Abstractions.UpdatePositionRequest(dealId, stopLevel, limitLevel));
        await evidence.RecordAsync(
            scenario,
            "protection-update-submitted",
            updated.Status.ToString(),
            new { updated.DealReference, updated.DealId, stopLevel, limitLevel, updated.Message });

        updated.Status.Should().BeOneOf(OrderStatus.Accepted, OrderStatus.Open);
        await context.WaitForPositionProtectionAsync(dealId, stopLevel, limitLevel, TimeSpan.FromSeconds(30));
        await evidence.RecordAsync(scenario, "protection-visible", "passed", new { DealId = dealId, stopLevel, limitLevel });

        await CloseAndRecordAsync(context, evidence, scenario, dealId);
        return (stopLevel, limitLevel);
    }

    private static async Task RunAtomicProtectedOpenCloseAsync(
        IgDemoIntegrationContext context,
        BrokerBaselineEvidenceWriter evidence,
        string epic,
        decimal size,
        decimal observedBuyStopLevel,
        decimal observedBuyLimitLevel)
    {
        const string scenario = "atomic-protected-open-close";
        var market = await context.IgTradingApi.GetMarketByEpicAsync(epic);
        var stopLevel = observedBuyLimitLevel;
        var limitLevel = observedBuyStopLevel;
        var beforeIds = (await context.Gateway.GetOpenPositionsAsync())
            .Select(position => position.DealId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dealReference = CreateDealReference("BBATOM");

        try
        {
            var response = await context.IgTradingApi.CreatePositionAsync(
                new SdkCreatePositionRequest(
                    epic,
                    market.Instrument.Expiry,
                    "SELL",
                    size,
                    "MARKET",
                    ResolveCurrencyCode(market),
                    "FILL_OR_KILL",
                    ForceOpen: true,
                    GuaranteedStop: false,
                    dealReference,
                    StopLevel: stopLevel,
                    LimitLevel: limitLevel));

            await evidence.RecordAsync(
                scenario,
                "open-with-protection-submitted",
                "submitted",
                new { response.DealReference, stopLevel, limitLevel });

            var status = await context.Gateway.GetOrderStatusAsync(response.DealReference);
            if (status?.Status == OrderStatus.Rejected)
            {
                await evidence.RecordAsync(scenario, "atomic-protection-rejected", "observed", status);
                return;
            }

            var dealId = await ResolveOpenedDealIdAsync(context, response.DealReference, beforeIds);
            context.PositionDealId = dealId;
            try
            {
                await context.WaitForPositionProtectionAsync(dealId, stopLevel, limitLevel, TimeSpan.FromSeconds(30));
                await evidence.RecordAsync(scenario, "atomic-protection-visible", "passed", new { DealId = dealId, stopLevel, limitLevel });
            }
            catch (TimeoutException exception)
            {
                await evidence.RecordAsync(
                    scenario,
                    "atomic-protection-visible",
                    "not-observed",
                    new { DealId = dealId, stopLevel, limitLevel, exception.Message });
            }

            await CloseAndRecordAsync(context, evidence, scenario, dealId);
        }
        catch (IgApiException exception)
        {
            if (!IsExpectedBrokerRejection(exception))
            {
                throw;
            }

            await evidence.RecordAsync(
                scenario,
                "atomic-protection-exception",
                "rejected",
                new { Exception = exception.GetType().Name, exception.ErrorCode, exception.Message });
        }
    }

    private static async Task RunInvalidSizeRejectionAsync(
        IgDemoIntegrationContext context,
        BrokerBaselineEvidenceWriter evidence,
        string epic,
        decimal validSize)
    {
        var invalidSize = decimal.Round(validSize / 10m, 4, MidpointRounding.AwayFromZero);
        if (invalidSize <= 0m || invalidSize == validSize)
        {
            invalidSize = 0.0001m;
        }

        await RunRejectedCreateScenarioAsync(
            context,
            evidence,
            "invalid-size-rejection",
            epic,
            invalidSize,
            requestFactory: market => new SdkCreatePositionRequest(
                epic,
                market.Instrument.Expiry,
                "BUY",
                invalidSize,
                "MARKET",
                ResolveCurrencyCode(market),
                "FILL_OR_KILL",
                ForceOpen: true,
                GuaranteedStop: false,
                CreateDealReference("BBSIZE")));
    }

    private static async Task RunInvalidProtectionRejectionAsync(
        IgDemoIntegrationContext context,
        BrokerBaselineEvidenceWriter evidence,
        string epic,
        decimal size)
    {
        await RunRejectedCreateScenarioAsync(
            context,
            evidence,
            "invalid-protection-rejection",
            epic,
            size,
            requestFactory: market =>
            {
                var basis = market.Snapshot.Offer ?? market.Snapshot.Bid ?? 1m;
                var invalidStop = basis + 1m;
                var invalidLimit = Math.Max(0.0001m, basis - 1m);

                return new SdkCreatePositionRequest(
                    epic,
                    market.Instrument.Expiry,
                    "BUY",
                    size,
                    "MARKET",
                    ResolveCurrencyCode(market),
                    "FILL_OR_KILL",
                    ForceOpen: true,
                    GuaranteedStop: false,
                    CreateDealReference("BBPROT"),
                    StopLevel: invalidStop,
                    LimitLevel: invalidLimit);
            });
    }

    private static async Task RunRejectedCreateScenarioAsync(
        IgDemoIntegrationContext context,
        BrokerBaselineEvidenceWriter evidence,
        string scenario,
        string epic,
        decimal size,
        Func<MarketDetailsResponse, SdkCreatePositionRequest> requestFactory)
    {
        var market = await context.IgTradingApi.GetMarketByEpicAsync(epic);
        var beforeIds = (await context.Gateway.GetOpenPositionsAsync())
            .Select(position => position.DealId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        try
        {
            var request = requestFactory(market);
            var response = await context.IgTradingApi.CreatePositionAsync(request);
            var status = await context.Gateway.GetOrderStatusAsync(response.DealReference);
            await evidence.RecordAsync(
                scenario,
                "broker-response",
                status?.Status.ToString() ?? "NoStatus",
                new { response.DealReference, Size = size, Status = status });

            status.Should().NotBeNull();
            if (status!.Status == OrderStatus.Rejected)
            {
                return;
            }

            if (status.DealId is { Length: > 0 } dealId)
            {
                context.PositionDealId = dealId;
                await CloseAndRecordAsync(context, evidence, scenario, dealId);
            }

            status.Status.Should().Be(OrderStatus.Rejected);
        }
        catch (IgApiException exception)
        {
            IsExpectedBrokerRejection(exception).Should().BeTrue(exception.Message);
            await evidence.RecordAsync(
                scenario,
                "broker-exception",
                "rejected",
                new { Exception = exception.GetType().Name, exception.ErrorCode, exception.Message });
        }

        var after = await context.Gateway.GetOpenPositionsAsync();
        after.Should().NotContain(position => !beforeIds.Contains(position.DealId));
    }

    private static async Task CloseAndRecordAsync(
        IgDemoIntegrationContext context,
        BrokerBaselineEvidenceWriter evidence,
        string scenario,
        string dealId)
    {
        var closed = await context.Gateway.ClosePositionAsync(new Trading.Abstractions.ClosePositionRequest(dealId, null));
        await evidence.RecordAsync(scenario, "close-submitted", closed.Status.ToString(), closed);
        closed.Status.Should().NotBe(OrderStatus.Rejected);

        await context.WaitForPositionPresenceAsync(dealId, shouldExist: false, TimeSpan.FromSeconds(45));
        await evidence.RecordAsync(scenario, "position-closed", "passed", new { DealId = dealId });
        context.PositionDealId = null;
    }

    private static async Task<string> ResolveOpenedDealIdAsync(
        IgDemoIntegrationContext context,
        string dealReference,
        IReadOnlySet<string> beforeIds)
    {
        var status = await context.WaitForOrderStatusAsync(
            dealReference,
            order => !string.IsNullOrWhiteSpace(order.DealId) || order.Status == OrderStatus.Rejected,
            TimeSpan.FromSeconds(30));

        status.Status.Should().NotBe(OrderStatus.Rejected, status.Message);
        if (!string.IsNullOrWhiteSpace(status.DealId))
        {
            return status.DealId;
        }

        var started = DateTimeOffset.UtcNow;
        while (DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(30))
        {
            var positions = await context.Gateway.GetOpenPositionsAsync();
            var match = positions.FirstOrDefault(position => !beforeIds.Contains(position.DealId));
            if (match is not null)
            {
                return match.DealId;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        throw new TimeoutException($"No opened position was visible for deal reference '{dealReference}'.");
    }

    private static async Task AssertNoExistingCanaryExposureAsync(
        IgDemoIntegrationContext context,
        BrokerBaselineEvidenceWriter evidence,
        string epic)
    {
        var positions = await context.Gateway.GetOpenPositionsAsync();
        var workingOrders = await context.Gateway.GetWorkingOrdersAsync();
        var canaryPositions = positions.Where(position => position.Instrument.Value == epic).ToList();
        var canaryWorkingOrders = workingOrders.Where(order => order.Instrument.Value == epic).ToList();

        await evidence.RecordAsync(
            "cleanup-verification",
            "canary-exposure",
            canaryPositions.Count == 0 && canaryWorkingOrders.Count == 0 ? "clear" : "blocked",
            new { Epic = epic, OpenPositionCount = canaryPositions.Count, WorkingOrderCount = canaryWorkingOrders.Count });

        canaryPositions.Should().BeEmpty("Phase 0 must not run against a canary instrument with pre-existing positions.");
        canaryWorkingOrders.Should().BeEmpty("Phase 0 must not run against a canary instrument with pre-existing working orders.");
    }

    private static async Task<string> SelectCanaryEpicAsync(
        IgDemoIntegrationContext context,
        BrokerBaselineEvidenceWriter evidence)
    {
        var candidates = new[] { context.Epic, "CS.D.BITCOIN.CFM.IP", "CS.D.BITCOIN.CFD.IP", "CC.D.VIX.UMA.IP" }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var epic in candidates)
        {
            try
            {
                var market = await context.IgTradingApi.GetMarketByEpicAsync(epic);
                var tradeable = string.Equals(market.Snapshot.MarketStatus, "TRADEABLE", StringComparison.OrdinalIgnoreCase);
                var marketOrdersAvailable = !string.Equals(
                    market.DealingRules?.MarketOrderPreference,
                    "NOT_AVAILABLE",
                    StringComparison.OrdinalIgnoreCase);

                await evidence.RecordAsync(
                    "preflight-demo-safety",
                    "canary-candidate",
                    tradeable && marketOrdersAvailable ? "selected" : "skipped",
                    new { Epic = epic, market.Snapshot.MarketStatus, market.DealingRules?.MarketOrderPreference });

                if (tradeable && marketOrdersAvailable)
                {
                    return epic;
                }
            }
            catch (IgApiException exception)
            {
                await evidence.RecordAsync(
                    "preflight-demo-safety",
                    "canary-candidate",
                    "error",
                    new { Epic = epic, exception.ErrorCode, exception.Message });
            }
        }

        throw new InvalidOperationException("No tradeable broker-baseline canary EPIC was available.");
    }

    private static decimal ResolveBaselineSize(MarketDetailsResponse market, decimal configuredSize)
    {
        var minimum = market.DealingRules?.MinDealSize?.Value;
        return minimum is > 0m ? minimum.Value : configuredSize;
    }

    private static bool IsExpectedBrokerRejection(IgApiException exception)
    {
        var text = $"{exception.ErrorCode} {exception.Message}";
        return text.Contains("invalid", StringComparison.OrdinalIgnoreCase)
            || text.Contains("minimum", StringComparison.OrdinalIgnoreCase)
            || text.Contains("size", StringComparison.OrdinalIgnoreCase)
            || text.Contains("stop", StringComparison.OrdinalIgnoreCase)
            || text.Contains("limit", StringComparison.OrdinalIgnoreCase)
            || text.Contains("reject", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveCurrencyCode(MarketDetailsResponse market)
    {
        var currency = market.Instrument.Currencies?.FirstOrDefault(item => item.IsDefault)
            ?? market.Instrument.Currencies?.FirstOrDefault();

        return currency?.Code ?? throw new InvalidOperationException("Unable to determine market currency code.");
    }

    private static string CreateDealReference(string prefix)
    {
        var suffix = Guid.NewGuid().ToString("N")[..20].ToUpperInvariant();
        return $"{prefix}{suffix}";
    }

    private static string Redact(string value)
        => value.Length <= 4 ? "****" : $"{new string('*', value.Length - 4)}{value[^4..]}";
}
