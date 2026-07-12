using com.lightstreamer.client;
using Ig.Trading.Sdk;
using Ig.Trading.Sdk.Streaming;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trading.MarketData;

namespace Trading.IG;

public sealed class IgMarketDataStreamClient : IMarketDataStreamClient
{
    private static readonly string[] ChartFields =
    [
        "UTM",
        "BID_OPEN",
        "BID_HIGH",
        "BID_LOW",
        "BID_CLOSE",
        "OFR_OPEN",
        "OFR_HIGH",
        "OFR_LOW",
        "OFR_CLOSE",
        "CONS_END",
        "CONS_TICK_COUNT",
    ];

    private readonly IIgTradingApi _igTradingApi;
    private readonly MarketDataOptions _options;
    private readonly MarketDataStreamPipelineMetrics _metrics;
    private readonly ILogger<IgMarketDataStreamClient> _logger;

    public IgMarketDataStreamClient(
        IIgTradingApi igTradingApi,
        IOptions<MarketDataOptions> options,
        MarketDataStreamPipelineMetrics metrics,
        ILogger<IgMarketDataStreamClient> logger)
    {
        _igTradingApi = igTradingApi;
        _options = options.Value;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<IMarketDataStreamSession> StartAsync(
        IReadOnlyList<MarketDataStreamSubscription> subscriptions,
        Func<StreamPriceBarUpdate, CancellationToken, Task> onUpdate,
        CancellationToken cancellationToken = default)
    {
        if (subscriptions.Count == 0)
        {
            throw new ArgumentException("At least one market-data stream subscription is required.", nameof(subscriptions));
        }

        var session = await _igTradingApi.AuthenticateAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(session.LightstreamerEndpoint))
        {
            throw new InvalidOperationException("IG authentication did not return a Lightstreamer endpoint.");
        }

        if (string.IsNullOrWhiteSpace(session.CurrentAccountId))
        {
            throw new InvalidOperationException("IG authentication did not return a current account id.");
        }

        if (string.IsNullOrWhiteSpace(session.Cst) || string.IsNullOrWhiteSpace(session.SecurityToken))
        {
            throw new InvalidOperationException("IG authentication did not return streaming security tokens.");
        }

        var client = new LightstreamerClient(session.LightstreamerEndpoint, null);
        client.connectionDetails.User = session.CurrentAccountId;
        client.connectionDetails.Password = $"CST-{session.Cst}|XST-{session.SecurityToken}";

        var items = subscriptions
            .Select(subscription => $"CHART:{subscription.Instrument.Value}:{IgStreamingConversions.ToIgChartScale(subscription.Resolution)}")
            .ToArray();
        _options.StreamIngestion.Validate();
        var dispatcher = new BoundedMarketDataStreamDispatcher(
            onUpdate,
            _options.StreamIngestion.DispatcherCapacity,
            _metrics,
            _logger);
        var subscription = new Subscription("MERGE", items, ChartFields)
        {
            RequestedSnapshot = "yes",
        };
        subscription.addListener(new ChartSubscriptionListener(dispatcher, _logger));

        client.subscribe(subscription);
        client.connect();

        _logger.LogInformation(
            "Started IG Lightstreamer chart session with {SubscriptionCount} subscriptions.",
            subscriptions.Count);

        return new IgMarketDataStreamSession(client, subscription, dispatcher, _logger);
    }

    private sealed class IgMarketDataStreamSession : IMarketDataStreamSession
    {
        private readonly LightstreamerClient _client;
        private readonly Subscription _subscription;
        private readonly BoundedMarketDataStreamDispatcher _dispatcher;
        private readonly ILogger _logger;

        public IgMarketDataStreamSession(
            LightstreamerClient client,
            Subscription subscription,
            BoundedMarketDataStreamDispatcher dispatcher,
            ILogger logger)
        {
            _client = client;
            _subscription = subscription;
            _dispatcher = dispatcher;
            _logger = logger;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                _client.unsubscribe(_subscription);
                _client.disconnect();
                await _client.DisconnectFuture();
                await _dispatcher.DisposeAsync();
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Failed to cleanly stop IG Lightstreamer chart session.");
            }
        }
    }

    private sealed class ChartSubscriptionListener : SubscriptionListener
    {
        private readonly BoundedMarketDataStreamDispatcher _dispatcher;
        private readonly ILogger _logger;
        private readonly IgChartCandleUpdateAccumulator _accumulator = new();

        public ChartSubscriptionListener(
            BoundedMarketDataStreamDispatcher dispatcher,
            ILogger logger)
        {
            _dispatcher = dispatcher;
            _logger = logger;
        }

        public void onClearSnapshot(string itemName, int itemPos)
        {
        }

        public void onCommandSecondLevelItemLostUpdates(int lostUpdates, string key)
        {
        }

        public void onCommandSecondLevelSubscriptionError(int code, string message, string key)
        {
            _logger.LogWarning("IG Lightstreamer second-level subscription error {Code}: {Message}.", code, message);
        }

        public void onEndOfSnapshot(string itemName, int itemPos)
        {
        }

        public void onItemLostUpdates(string itemName, int itemPos, int lostUpdates)
        {
            _logger.LogWarning(
                "IG Lightstreamer lost {LostUpdateCount} updates for {ItemName}.",
                lostUpdates,
                itemName);
        }

        public void onItemUpdate(ItemUpdate itemUpdate)
        {
            try
            {
                var (epic, scale) = ParseChartItemName(itemUpdate.ItemName);
                var fields = itemUpdate.Fields.ToDictionary(
                    pair => pair.Key,
                    pair => (string?)pair.Value,
                    StringComparer.OrdinalIgnoreCase);
                var candle = _accumulator.Apply(epic, scale, fields);
                if (candle is null)
                {
                    _logger.LogDebug(
                        "Ignored incomplete IG Lightstreamer chart update for {ItemName}.",
                        itemUpdate.ItemName);
                    return;
                }

                var update = IgMarketDataStreamMapper.ToStreamPriceBarUpdate(candle, DateTimeOffset.UtcNow);
                if (!_dispatcher.TryPost(update))
                {
                    _logger.LogWarning(
                        "Dropped IG Lightstreamer chart update for {Epic} at {TimestampUtc}; stream dispatcher is full.",
                        epic,
                        update.Bar.TimestampUtc);
                }
            }
            catch (IgStreamingDataException exception)
            {
                _logger.LogWarning(
                    "Ignored invalid IG Lightstreamer chart update for {ItemName}: {Message}",
                    itemUpdate.ItemName,
                    exception.Message);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Ignored malformed IG Lightstreamer chart update for {ItemName}.", itemUpdate.ItemName);
            }
        }

        public void onListenEnd()
        {
        }

        public void onListenStart()
        {
        }

        public void onSubscription()
        {
            _logger.LogInformation("IG Lightstreamer chart subscription established.");
        }

        public void onSubscriptionError(int code, string message)
        {
            _logger.LogWarning("IG Lightstreamer subscription error {Code}: {Message}.", code, message);
        }

        public void onUnsubscription()
        {
            _logger.LogInformation("IG Lightstreamer chart subscription stopped.");
        }

        public void onRealMaxFrequency(string frequency)
        {
        }

        private static (string Epic, string Scale) ParseChartItemName(string itemName)
        {
            var parts = itemName.Split(':', 3);
            if (parts.Length != 3 || !string.Equals(parts[0], "CHART", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Unexpected IG chart stream item name '{itemName}'.");
            }

            return (parts[1], parts[2]);
        }
    }
}
