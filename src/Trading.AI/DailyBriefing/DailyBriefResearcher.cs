using Microsoft.Extensions.Options;
using Trading.AI.Configuration;
using Trading.AI.PromptExecution;
using Trading.AI.Prompts;
using Trading.AI.Prompts.DailyBriefResearch;
using Trading.Strategy.DayPlanning;

namespace Trading.AI.DailyBriefing;

public sealed class DailyBriefResearcher
{
    private readonly PromptExecutor _promptExecutor;
    private readonly DailyBriefingOptions _options;
    private readonly TrackedMarketsFormatter _trackedMarketsFormatter;

    public DailyBriefResearcher(
        PromptExecutor promptExecutor,
        IOptions<DailyBriefingOptions> options,
        TrackedMarketsFormatter trackedMarketsFormatter)
    {
        _promptExecutor = promptExecutor;
        _options = options.Value;
        _trackedMarketsFormatter = trackedMarketsFormatter;
    }

    public async Task<DailyBriefResearchResult> ResearchAsync(DailyBriefingRequest request, CancellationToken cancellationToken)
    {
        EnsureTrackedMarketsSatisfyShortlist(request);

        var execution = await _promptExecutor.ExecuteTextAsync(
            PromptRegistry.DailyBriefResearch,
            _options.Research,
            BuildInput(request),
            PromptTextArtifactKind.Markdown,
            cancellationToken);

        return new DailyBriefResearchResult(
            execution.ResponseText,
            execution.TextArtifactPath ?? string.Empty,
            execution.Response.CreatedAt ?? DateTimeOffset.UtcNow);
    }

    private DailyBriefResearchInput BuildInput(DailyBriefingRequest request)
        => new(
            request.TradingDay.TradingDate,
            _options.DefaultTimezone,
            request.Rules.MarketWatch.ShortlistSize,
            _trackedMarketsFormatter.Format(_options.TrackedMarkets),
            request.TradingDay.TradingDate,
            request.RequestedAtUtc);

    private void EnsureTrackedMarketsSatisfyShortlist(DailyBriefingRequest request)
    {
        if (_options.TrackedMarkets.Length < request.Rules.MarketWatch.ShortlistSize)
        {
            throw new InvalidOperationException(
                $"Configured tracked markets ({_options.TrackedMarkets.Length}) must be at least the shortlist size ({request.Rules.MarketWatch.ShortlistSize}).");
        }
    }
}
