using Microsoft.Extensions.Options;
using Trading.AI.Configuration;
using Trading.AI.Prompts;
using Trading.Strategy.DayPlanning;
using Trading.Strategy.Shared;

namespace Trading.AI.DailyBriefing;

public sealed class DailyPlanConverter
{
    private readonly IChatClientFactory _chatClientFactory;
    private readonly PromptExecutor _promptExecutor;
    private readonly DailyBriefingOptions _options;
    private readonly TrackedMarketsFormatter _trackedMarketsFormatter;
    private readonly DailyPlanMapper _mapper;

    public DailyPlanConverter(
        IChatClientFactory chatClientFactory,
        PromptExecutor promptExecutor,
        IOptions<DailyBriefingOptions> options,
        TrackedMarketsFormatter trackedMarketsFormatter,
        DailyPlanMapper mapper)
    {
        _chatClientFactory = chatClientFactory;
        _promptExecutor = promptExecutor;
        _options = options.Value;
        _trackedMarketsFormatter = trackedMarketsFormatter;
        _mapper = mapper;
    }

    public async Task<TradingDayPlan> ConvertAsync(
        DailyBriefingRequest request,
        string researchBriefMarkdown,
        CancellationToken cancellationToken)
    {
        var trackedMarkets = _options.TrackedMarkets.ToDictionary(x => x.InstrumentId, StringComparer.Ordinal);
        var context = new PromptExecutionContext(
            PromptRegistry.DailyPlanJson,
            _options.PlanJson,
            BuildVariables(request, researchBriefMarkdown),
            DateTimeOffset.UtcNow,
            request.TradingDay.TradingDate);

        var (execution, document) = await _promptExecutor.ExecuteStructuredAsync<DailyPlanDocument>(
            _chatClientFactory.CreateClient(_options.PlanJson.ModelId),
            context,
            cancellationToken);

        _mapper.ValidateTrackedMarkets(document, trackedMarkets);
        var plannedAtUtc = execution.Response.CreatedAt ?? DateTimeOffset.UtcNow;
        return _mapper.Map(document, request, trackedMarkets, plannedAtUtc);
    }

    private Dictionary<string, string> BuildVariables(DailyBriefingRequest request, string researchBriefMarkdown)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["TRADING_DATE"] = request.TradingDay.TradingDate.ToString("yyyy-MM-dd"),
            ["REPORT_TIMEZONE"] = _options.DefaultTimezone,
            ["WATCHLIST_SIZE"] = request.Rules.MarketWatch.ShortlistSize.ToString(),
            ["MIN_REWARD_RISK_RATIO"] = request.Rules.Risk.MinimumRewardRiskRatio.ToString("0.##"),
            ["TRACKED_MARKETS"] = _trackedMarketsFormatter.Format(_options.TrackedMarkets),
            ["RESEARCH_BRIEF"] = researchBriefMarkdown,
        };
    }
}
