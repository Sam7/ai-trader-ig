using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Trading.Abstractions;
using Trading.IG;
using Trading.MarketData;

namespace Trading.IG.Tests;

public sealed class BoundedMarketDataStreamDispatcherTests
{
    [Fact]
    public async Task TryPost_WhenQueueIsFull_ShouldEvictFormingUpdateForFinalUpdate()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handled = new List<StreamPriceBarUpdate>();
        await using var dispatcher = new BoundedMarketDataStreamDispatcher(
            async (update, cancellationToken) =>
            {
                handled.Add(update);
                started.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            },
            capacity: 1,
            new MarketDataStreamPipelineMetrics(),
            NullLogger.Instance);
        var instrument = new InstrumentId("CS.D.BITCOIN.CFD.IP");

        dispatcher.TryPost(CreateUpdate(instrument, "2026-07-12T00:00:00Z", isFinal: false)).Should().BeTrue();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        dispatcher.TryPost(CreateUpdate(instrument, "2026-07-12T00:05:00Z", isFinal: false)).Should().BeTrue();
        dispatcher.TryPost(CreateUpdate(instrument, "2026-07-12T00:10:00Z", isFinal: false)).Should().BeFalse();
        dispatcher.TryPost(CreateUpdate(instrument, "2026-07-12T00:15:00Z", isFinal: true)).Should().BeTrue();

        release.SetResult();
        await Task.Delay(50);

        handled.Select(update => update.Bar.TimestampUtc).Should().Contain(DateTimeOffset.Parse("2026-07-12T00:15:00Z"));
        handled.Select(update => update.Bar.TimestampUtc).Should().NotContain(DateTimeOffset.Parse("2026-07-12T00:05:00Z"));
    }

    private static StreamPriceBarUpdate CreateUpdate(InstrumentId instrument, string timestampUtc, bool isFinal)
        => new(
            instrument,
            PriceResolution.FiveMinutes,
            new PriceBar(
                DateTimeOffset.Parse(timestampUtc),
                100m,
                101m,
                99m,
                100.5m,
                100.2m,
                101.2m,
                99.2m,
                100.7m,
                10),
            isFinal,
            DateTimeOffset.Parse(timestampUtc).AddMinutes(5));
}
