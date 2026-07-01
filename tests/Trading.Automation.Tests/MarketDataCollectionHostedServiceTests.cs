using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Trading.Abstractions;
using Trading.AI.Configuration;
using Trading.Automation.Configuration;
using Trading.Automation.MarketData;
using Trading.MarketData;

public sealed class MarketDataCollectionHostedServiceTests
{
    [Fact]
    public async Task StartAsync_ShouldStartCollectorForConfiguredTrackedMarkets()
    {
        var collector = new FakeMarketDataCollector();
        var service = CreateService(collector, CreateDailyBriefingOptions(
            "CS.D.BITCOIN.CFD.IP",
            "CC.D.CFAGOLD.CFA.IP"));

        await service.StartAsync(CancellationToken.None);
        await collector.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await service.StopAsync(CancellationToken.None);

        collector.Requests.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new CollectRequest(
                [new InstrumentId("CS.D.BITCOIN.CFD.IP"), new InstrumentId("CC.D.CFAGOLD.CFA.IP")],
                Duration: null));
    }

    [Fact]
    public async Task StartAsync_ShouldDeduplicateConfiguredTrackedMarkets()
    {
        var collector = new FakeMarketDataCollector();
        var service = CreateService(collector, CreateDailyBriefingOptions(
            "CS.D.BITCOIN.CFD.IP",
            "CS.D.BITCOIN.CFD.IP",
            " CC.D.CFAGOLD.CFA.IP "));

        await service.StartAsync(CancellationToken.None);
        await collector.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await service.StopAsync(CancellationToken.None);

        collector.Requests.Should().ContainSingle()
            .Which.Instruments.Should().Equal(
                new InstrumentId("CS.D.BITCOIN.CFD.IP"),
                new InstrumentId("CC.D.CFAGOLD.CFA.IP"));
    }

    [Fact]
    public async Task StartAsync_WhenNoTrackedMarketsAreConfigured_ShouldFail()
    {
        var collector = new FakeMarketDataCollector();
        var service = CreateService(collector, CreateDailyBriefingOptions());

        await service.StartAsync(CancellationToken.None);
        var action = () => service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(2));

        await action.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*No tracked markets*");
    }

    [Fact]
    public async Task StartAsync_WhenCollectorFails_ShouldRetryWithoutStoppingHost()
    {
        var collector = new FakeMarketDataCollector
        {
            FailuresBeforeSuccess = 1,
        };
        var service = CreateService(
            collector,
            CreateDailyBriefingOptions("CS.D.BITCOIN.CFD.IP"),
            new MarketDataCollectionOptions
            {
                InitialRetryDelay = TimeSpan.FromMilliseconds(1),
                MaxRetryDelay = TimeSpan.FromMilliseconds(5),
            });

        await service.StartAsync(CancellationToken.None);
        await collector.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await service.StopAsync(CancellationToken.None);

        collector.Requests.Should().HaveCount(2);
    }

    private static MarketDataCollectionHostedService CreateService(
        FakeMarketDataCollector collector,
        DailyBriefingOptions dailyBriefingOptions,
        MarketDataCollectionOptions? options = null)
        => new(
            collector,
            Options.Create(dailyBriefingOptions),
            Options.Create(options ?? new MarketDataCollectionOptions()),
            NullLogger<MarketDataCollectionHostedService>.Instance);

    private static DailyBriefingOptions CreateDailyBriefingOptions(params string[] instruments)
        => new()
        {
            TrackedMarkets = instruments
                .Select(instrument => new TrackedMarketOptions { InstrumentId = instrument })
                .ToArray(),
        };

    private sealed class FakeMarketDataCollector : IMarketDataCollector
    {
        public List<CollectRequest> Requests { get; } = [];

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int FailuresBeforeSuccess { get; init; }

        public async Task RunAsync(
            IReadOnlyList<InstrumentId> instruments,
            TimeSpan? duration,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(new CollectRequest(instruments, duration));

            if (Requests.Count <= FailuresBeforeSuccess)
            {
                throw new InvalidOperationException("collector failed");
            }

            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed record CollectRequest(IReadOnlyList<InstrumentId> Instruments, TimeSpan? Duration);
}
