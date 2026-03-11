using Trading.Strategy.DayPlanning;
using Trading.Strategy.Shared;

namespace Trading.AI.DailyBriefing;

public sealed class OpenAiDailyBriefingComposer : IDailyBriefingComposer
{
    private readonly DailyBriefResearcher _researcher;
    private readonly DailyPlanConverter _planConverter;

    public OpenAiDailyBriefingComposer(
        DailyBriefResearcher researcher,
        DailyPlanConverter planConverter)
    {
        _researcher = researcher;
        _planConverter = planConverter;
    }

    public async Task<TradingDayPlan> ComposeAsync(DailyBriefingRequest request, CancellationToken cancellationToken = default)
    {
        var research = await _researcher.ResearchAsync(request, cancellationToken);
        return await _planConverter.ConvertAsync(request, research.Markdown, cancellationToken);
    }
}
