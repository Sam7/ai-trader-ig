using System.Diagnostics;
using Ig.Trading.Sdk;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trading.Abstractions;
using Trading.IG.DependencyInjection;

namespace Trading.IG.Tests;

internal sealed class IgDemoIntegrationContext : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private ITradingSession? _session;

    private IgDemoIntegrationContext(
        ServiceProvider provider,
        ITradingGateway gateway,
        IIgTradingApi igTradingApi,
        string epic,
        decimal size,
        decimal workingOrderLevel,
        string marketSearchTerm)
    {
        _provider = provider;
        Gateway = gateway;
        IgTradingApi = igTradingApi;
        Epic = epic;
        Size = size;
        WorkingOrderLevel = workingOrderLevel;
        MarketSearchTerm = marketSearchTerm;
    }

    public ITradingGateway Gateway { get; }

    public IIgTradingApi IgTradingApi { get; }

    public string Epic { get; }

    public decimal Size { get; }

    public decimal WorkingOrderLevel { get; }

    public string MarketSearchTerm { get; }

    public string? WorkingOrderDealId { get; set; }

    public string? PositionDealId { get; set; }

    public static Task<IgDemoIntegrationContext> CreateAsync(bool? useEncryptedPasswordOverride = null)
    {
        var builder = new ConfigurationBuilder()
            .AddEnvironmentVariables();

        if (useEncryptedPasswordOverride is not null)
        {
            builder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["IG:UseEncryptedPassword"] = useEncryptedPasswordOverride.Value.ToString().ToLowerInvariant(),
            });
        }

        var configuration = builder.Build();

        var epic = configuration["IG__TestEpic"] ?? "CC.D.VIX.UMA.IP";
        var size = decimal.TryParse(configuration["IG__TestSize"], out var configuredSize)
            ? configuredSize
            : 1m;
        var workingOrderLevel = decimal.TryParse(configuration["IG__WorkingOrderTestLevel"], out var configuredLevel)
            ? configuredLevel
            : 10m;
        var marketSearchTerm = configuration["IG__MarketSearchTerm"] ?? "VIX";

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIgTradingGateway(configuration);

        var provider = services.BuildServiceProvider();
        var gateway = provider.GetRequiredService<ITradingGateway>();
        var igTradingApi = provider.GetRequiredService<IIgTradingApi>();

        return Task.FromResult(new IgDemoIntegrationContext(provider, gateway, igTradingApi, epic, size, workingOrderLevel, marketSearchTerm));
    }

    public async Task<ITradingSession> AuthenticateAsync()
    {
        _session ??= await Gateway.AuthenticateAsync();
        return _session;
    }

    public async Task<OrderSummary> WaitForOrderStatusAsync(
        string dealReference,
        Func<OrderSummary, bool> predicate,
        TimeSpan timeout)
    {
        var startedAt = Stopwatch.StartNew();
        OrderSummary? last = null;

        while (startedAt.Elapsed < timeout)
        {
            last = await Gateway.GetOrderStatusAsync(dealReference);
            if (last is not null && predicate(last))
            {
                return last;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        if (last is not null)
        {
            return last;
        }

        throw new TimeoutException($"No order status returned for deal reference '{dealReference}' within {timeout}.");
    }

    public async Task WaitForPositionPresenceAsync(
        string dealId,
        bool shouldExist,
        TimeSpan timeout)
    {
        var startedAt = Stopwatch.StartNew();

        while (startedAt.Elapsed < timeout)
        {
            var positions = await Gateway.GetOpenPositionsAsync();
            var exists = positions.Any(position => string.Equals(position.DealId, dealId, StringComparison.OrdinalIgnoreCase));
            if (exists == shouldExist)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        throw new TimeoutException($"Position '{dealId}' did not reach expected state shouldExist={shouldExist} within {timeout}.");
    }

    public async Task<IReadOnlyList<PositionSummary>> WaitForPositionCountChangeAsync(
        int initialCount,
        int expectedMinimumCount,
        TimeSpan timeout)
    {
        var startedAt = Stopwatch.StartNew();

        while (startedAt.Elapsed < timeout)
        {
            var positions = await Gateway.GetOpenPositionsAsync();
            if (positions.Count >= expectedMinimumCount && positions.Count > initialCount)
            {
                return positions;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        throw new TimeoutException($"Open positions did not grow beyond {initialCount} within {timeout}.");
    }

    public async Task WaitForPositionProtectionAsync(
        string dealId,
        decimal expectedStopLevel,
        decimal expectedLimitLevel,
        TimeSpan timeout)
    {
        var startedAt = Stopwatch.StartNew();

        while (startedAt.Elapsed < timeout)
        {
            var position = await Gateway.GetOpenPositionsAsync();
            var match = position.FirstOrDefault(x => string.Equals(x.DealId, dealId, StringComparison.OrdinalIgnoreCase));
            if (match is not null
                && match.StopLevel == expectedStopLevel
                && match.LimitLevel == expectedLimitLevel)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        throw new TimeoutException($"Position '{dealId}' did not reflect expected stop/limit levels within {timeout}.");
    }

    public async Task<(decimal StopLevel, decimal LimitLevel)> CreateValidProtectionLevelsAsync(string dealId)
    {
        var position = await IgTradingApi.GetPositionByDealIdAsync(dealId)
            ?? throw new InvalidOperationException($"Position '{dealId}' was not found.");
        var market = await IgTradingApi.GetMarketByEpicAsync(position.Market.Epic);

        var basis = position.Position.Level
            ?? market.Snapshot.Bid
            ?? market.Snapshot.Offer
            ?? 0m;
        var snapshotBid = market.Snapshot.Bid ?? basis;
        var snapshotOffer = market.Snapshot.Offer ?? basis;
        var minDistance = ResolveDistance(market, snapshotBid, snapshotOffer);

        var bufferedDistance = decimal.Round(Math.Max(minDistance * 3m, 1m), 2, MidpointRounding.AwayFromZero);

        return string.Equals(position.Position.Direction, "BUY", StringComparison.OrdinalIgnoreCase)
            ? (basis - bufferedDistance, basis + bufferedDistance)
            : (basis + bufferedDistance, basis - bufferedDistance);
    }

    public async ValueTask DisposeAsync()
    {
        await CleanupAsync();
        await _provider.DisposeAsync();
    }

    private async Task CleanupAsync()
    {
        if (!string.IsNullOrWhiteSpace(WorkingOrderDealId))
        {
            try
            {
                await EnsureAuthenticatedAsync();
                await Gateway.CancelWorkingOrderAsync(WorkingOrderDealId);
            }
            catch (TradingGatewayException)
            {
                // Best-effort cleanup for live integration tests.
            }
        }

        if (!string.IsNullOrWhiteSpace(PositionDealId))
        {
            try
            {
                await EnsureAuthenticatedAsync();
                await Gateway.ClosePositionAsync(new ClosePositionRequest(PositionDealId, null));
            }
            catch (TradingGatewayException)
            {
                // Best-effort cleanup for live integration tests.
            }
        }
    }

    private Task EnsureAuthenticatedAsync()
        => _session is null ? AuthenticateAsync() : Task.CompletedTask;

    private static decimal ResolveDistance(Ig.Trading.Sdk.Models.MarketDetailsResponse market, decimal bid, decimal offer)
    {
        var value = market.DealingRules?.MinNormalStopOrLimitDistance?.Value ?? 1m;
        var unit = market.DealingRules?.MinNormalStopOrLimitDistance?.Unit;
        if (string.Equals(unit, "PERCENTAGE", StringComparison.OrdinalIgnoreCase))
        {
            var basis = Math.Max((bid + offer) / 2m, 1m);
            return decimal.Round(basis * (value / 100m), 2, MidpointRounding.AwayFromZero);
        }

        return value;
    }
}
