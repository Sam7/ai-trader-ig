using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trading.Automation.Configuration;

namespace Trading.Automation.Execution;

public sealed class IntradayOpportunityScanService
{
    private readonly DailyPlanEnsureService _dailyPlanEnsureService;
    private readonly IntradayOpportunityScanGate _scanGate;
    private readonly IIntradayOpportunityPreparationService _preparationService;
    private readonly IIntradayOpportunityAnalysisService _analysisService;
    private readonly IIntradayOpportunityDecisionCoordinator _decisionCoordinator;
    private readonly AutomationOptions _options;
    private readonly ILogger<IntradayOpportunityScanService> _logger;

    public IntradayOpportunityScanService(
        DailyPlanEnsureService dailyPlanEnsureService,
        IntradayOpportunityScanGate scanGate,
        IIntradayOpportunityPreparationService preparationService,
        IIntradayOpportunityAnalysisService analysisService,
        IIntradayOpportunityDecisionCoordinator decisionCoordinator,
        IOptions<AutomationOptions> options,
        ILogger<IntradayOpportunityScanService> logger)
    {
        _dailyPlanEnsureService = dailyPlanEnsureService;
        _scanGate = scanGate;
        _preparationService = preparationService;
        _analysisService = analysisService;
        _decisionCoordinator = decisionCoordinator;
        _options = options.Value;
        _logger = logger;
    }

    public Task<IntradayOpportunitySubmitResult?> RunForTodayAsync(CancellationToken cancellationToken = default)
    {
        var requestedAtUtc = DateTimeOffset.UtcNow;
        return RunAsync(ResolveTradingDate(requestedAtUtc), requestedAtUtc, cancellationToken);
    }

    public Task<IntradayOpportunityPreparationDocument?> PrepareForTodayAsync(CancellationToken cancellationToken = default)
    {
        var requestedAtUtc = DateTimeOffset.UtcNow;
        return PrepareAsync(ResolveTradingDate(requestedAtUtc), requestedAtUtc, cancellationToken);
    }

    public Task<IntradayOpportunityPreparationDocument?> PrepareAsync(
        DateOnly tradingDate,
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken = default)
        => _preparationService.PrepareAsync(tradingDate, requestedAtUtc, cancellationToken);

    public async Task<IntradayOpportunitySubmitResult> SubmitAsync(
        string preparedJsonPath,
        CancellationToken cancellationToken = default)
    {
        var prepared = await _preparationService.LoadAsync(preparedJsonPath, cancellationToken);
        return await SubmitAsync(prepared, cancellationToken);
    }

    public async Task<IntradayOpportunitySubmitResult?> RunAsync(
        DateOnly tradingDate,
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (!_scanGate.TryEnter())
        {
            _logger.LogWarning(
                "Skipping intraday opportunity scan for {TradingDate}: a previous scan is still running.",
                tradingDate);
            return null;
        }

        try
        {
            try
            {
                await _dailyPlanEnsureService.EnsureAsync(tradingDate, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(
                    exception,
                    "Skipping intraday opportunity scan for {TradingDate}: daily plan could not be created.",
                    tradingDate);
                return null;
            }

            var prepared = await PrepareAsync(tradingDate, requestedAtUtc, cancellationToken);
            return prepared is null ? null : await SubmitAsync(prepared, cancellationToken);
        }
        finally
        {
            _scanGate.Release();
        }
    }

    private async Task<IntradayOpportunitySubmitResult> SubmitAsync(
        IntradayOpportunityPreparationDocument prepared,
        CancellationToken cancellationToken)
    {
        var execution = await _analysisService.AnalyzeAsync(prepared, cancellationToken);
        return await _decisionCoordinator.CoordinateAsync(prepared, execution, cancellationToken);
    }

    private DateOnly ResolveTradingDate(DateTimeOffset utcNow)
    {
        var timezone = TimeZoneInfo.FindSystemTimeZoneById(_options.Timezone);
        var localNow = TimeZoneInfo.ConvertTime(utcNow, timezone);
        return DateOnly.FromDateTime(localNow.DateTime);
    }
}
