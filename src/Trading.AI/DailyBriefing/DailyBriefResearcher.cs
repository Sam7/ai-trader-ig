using Microsoft.Extensions.Options;
using Trading.AI.Configuration;
using Trading.AI.Prompts;
using Trading.Strategy.DayPlanning;

namespace Trading.AI.DailyBriefing;

public sealed class DailyBriefResearcher
{
    private readonly IChatClientFactory _chatClientFactory;
    private readonly PromptExecutor _promptExecutor;
    private readonly DailyBriefingOptions _options;
    private readonly TrackedMarketsFormatter _trackedMarketsFormatter;

    public DailyBriefResearcher(
        IChatClientFactory chatClientFactory,
        PromptExecutor promptExecutor,
        IOptions<DailyBriefingOptions> options,
        TrackedMarketsFormatter trackedMarketsFormatter)
    {
        _chatClientFactory = chatClientFactory;
        _promptExecutor = promptExecutor;
        _options = options.Value;
        _trackedMarketsFormatter = trackedMarketsFormatter;
    }

    public async Task<DailyBriefResearchResult> ResearchAsync(DailyBriefingRequest request, CancellationToken cancellationToken)
    {
        EnsureTrackedMarketsSatisfyShortlist(request);

        var context = new PromptExecutionContext(
            PromptRegistry.DailyBriefResearch,
            _options.Research,
            BuildVariables(request),
            request.RequestedAtUtc,
            request.TradingDay.TradingDate);

        var execution = await _promptExecutor.ExecuteTextAsync(
            _chatClientFactory.CreateClient(_options.Research.ModelId),
            context,
            cancellationToken);

        var artifactPath = Path.Combine(
            Path.GetFullPath(_options.ObservabilityRootPath),
            request.TradingDay.TradingDate.ToString("yyyy-MM-dd"),
            $"{request.RequestedAtUtc:HHmmssfff}-{PromptRegistry.DailyBriefResearch.Name}.md");

        return new DailyBriefResearchResult(execution.ResponseText, artifactPath, execution.Response.CreatedAt ?? DateTimeOffset.UtcNow);
    }

    private Dictionary<string, string> BuildVariables(DailyBriefingRequest request)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["REPORT_DATE"] = request.TradingDay.TradingDate.ToString("yyyy-MM-dd"),
            ["REPORT_TIMEZONE"] = _options.DefaultTimezone,
            ["WATCHLIST_SIZE"] = request.Rules.MarketWatch.ShortlistSize.ToString(),
            ["TRACKED_MARKETS"] = _trackedMarketsFormatter.Format(_options.TrackedMarkets),
        };
    }

    private void EnsureTrackedMarketsSatisfyShortlist(DailyBriefingRequest request)
    {
        if (_options.TrackedMarkets.Length < request.Rules.MarketWatch.ShortlistSize)
        {
            throw new InvalidOperationException(
                $"Configured tracked markets ({_options.TrackedMarkets.Length}) must be at least the shortlist size ({request.Rules.MarketWatch.ShortlistSize}).");
        }
    }
}
