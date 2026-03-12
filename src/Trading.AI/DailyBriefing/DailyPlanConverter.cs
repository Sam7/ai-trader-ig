using Microsoft.Extensions.Options;
using Trading.AI.Configuration;
using Trading.AI.PromptExecution;
using Trading.AI.Prompts;
using Trading.AI.Prompts.DailyPlanJson;
using Trading.Strategy.DayPlanning;
using Trading.Strategy.Shared;

namespace Trading.AI.DailyBriefing;

public sealed class DailyPlanConverter
{
    private readonly PromptExecutor _promptExecutor;
    private readonly DailyBriefingOptions _options;
    private readonly TrackedMarketsFormatter _trackedMarketsFormatter;
    private readonly DailyPlanMapper _mapper;

    public DailyPlanConverter(
        PromptExecutor promptExecutor,
        IOptions<DailyBriefingOptions> options,
        TrackedMarketsFormatter trackedMarketsFormatter,
        DailyPlanMapper mapper)
    {
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
        EnsureTrackedMarketsSatisfyShortlist(request);

        var trackedMarkets = _options.TrackedMarkets.ToDictionary(x => x.InstrumentId, StringComparer.Ordinal);
        var planModel = new PromptModelOptions
        {
            ModelId = _options.PlanJson.ModelId,
            Temperature = null,
            MaxOutputTokens = _options.PlanJson.MaxOutputTokens,
            EnableWebSearch = _options.PlanJson.EnableWebSearch,
            Pricing = _options.PlanJson.Pricing,
        };
        var execution = await _promptExecutor.ExecuteStructuredAsync<DailyPlanJsonInput, DailyPlanDocument>(
            PromptRegistry.DailyPlanJson,
            planModel,
            BuildInput(request, researchBriefMarkdown),
            DailyPlanJsonResponseFormat.Create(request.Rules.MarketWatch.ShortlistSize),
            cancellationToken);

        var document = execution.StructuredValue;
        _mapper.ValidateTrackedMarkets(document, trackedMarkets);
        var plannedAtUtc = execution.Response.CreatedAt ?? DateTimeOffset.UtcNow;
        return _mapper.Map(document, request, trackedMarkets, plannedAtUtc);
    }

    private DailyPlanJsonInput BuildInput(DailyBriefingRequest request, string researchBriefMarkdown)
        => new(
            request.TradingDay.TradingDate,
            _options.DefaultTimezone,
            request.Rules.MarketWatch.ShortlistSize,
            request.Rules.Risk.MinimumRewardRiskRatio,
            _trackedMarketsFormatter.Format(_options.TrackedMarkets),
            researchBriefMarkdown,
            DateTimeOffset.UtcNow);

    private void EnsureTrackedMarketsSatisfyShortlist(DailyBriefingRequest request)
    {
        if (_options.TrackedMarkets.Length < request.Rules.MarketWatch.ShortlistSize)
        {
            throw new InvalidOperationException(
                $"Configured tracked markets ({_options.TrackedMarkets.Length}) must be at least the shortlist size ({request.Rules.MarketWatch.ShortlistSize}).");
        }
    }
}
