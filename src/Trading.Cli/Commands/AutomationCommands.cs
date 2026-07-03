using System.ComponentModel;
using Microsoft.Extensions.Options;
using Spectre.Console;
using Spectre.Console.Cli;
using Trading.AI.DailyBriefing;
using Trading.Automation.Configuration;
using Trading.Automation.Execution;
using Trading.Strategy.DayPlanning;
using Trading.Strategy.Inputs;
using Trading.Strategy.Rules;
using Trading.Strategy.Shared;

[Description("Start the background automation worker in the foreground.")]
public sealed class AutomationRunCommand : AsyncCommand<AutomationRunSettings>
{
    private readonly IAutomationRuntime _runtime;

    public AutomationRunCommand(IAutomationRuntime runtime)
    {
        _runtime = runtime;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, AutomationRunSettings settings, CancellationToken cancellationToken)
    {
        await _runtime.RunAsync(settings.ResolveDuration(), settings.ResolveInstruments(), settings.Root, cancellationToken);
        return 0;
    }
}

public sealed class AutomationRunSettings : CommandSettings
{
    private static readonly TimeSpan MaxBoundedDuration = TimeSpan.FromDays(7);

    [CommandOption("--duration <TIMESPAN>")]
    [Description("Optional duration. Omit to run until cancelled. HH:mm:ss values support hours greater than 23.")]
    public string? Duration { get; init; }

    [CommandOption("--instruments <EPICS>")]
    [Description("Optional comma-separated EPIC filter. When set, automation runs only for these configured tracked markets.")]
    public string? Instruments { get; init; }

    [CommandOption("--root <PATH>")]
    [Description("Optional prompt/evidence root path for automation artifacts.")]
    public string? Root { get; init; }

    public override ValidationResult Validate()
    {
        if (!CollectMarketDataSettings.TryParseDuration(Duration, out var parsedDuration))
        {
            return ValidationResult.Error("Option --duration must be a valid TimeSpan.");
        }

        if (Instruments is not null)
        {
            var instruments = ResolveInstruments();
            if (instruments.Count == 0)
            {
                return ValidationResult.Error("Option --instruments must include at least one EPIC.");
            }

            if (instruments.Any(instrument => instrument.Any(char.IsWhiteSpace)))
            {
                return ValidationResult.Error("Option --instruments must contain comma-separated EPICs without whitespace.");
            }
        }

        if (Root is not null && string.IsNullOrWhiteSpace(Root))
        {
            return ValidationResult.Error("Option --root must not be empty.");
        }

        if (parsedDuration is null)
        {
            return ValidationResult.Success();
        }

        if (parsedDuration < TimeSpan.Zero)
        {
            return ValidationResult.Error("Option --duration must be zero or greater.");
        }

        return parsedDuration <= MaxBoundedDuration
            ? ValidationResult.Success()
            : ValidationResult.Error("Option --duration must be 7 days or less. Omit --duration to run indefinitely.");
    }

    public TimeSpan? ResolveDuration()
        => CollectMarketDataSettings.ParseDuration(Duration);

    public IReadOnlyList<string> ResolveInstruments()
        => string.IsNullOrWhiteSpace(Instruments)
            ? []
            : Instruments.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

[Description("Generate the research markdown brief for a trading date.")]
public sealed class AutomationBriefResearchCommand : AsyncCommand<AutomationBriefSettings>
{
    private readonly DailyBriefingResearchService _service;
    private readonly TradingCliRenderer _renderer;
    private readonly AutomationOptions _options;

    public AutomationBriefResearchCommand(
        DailyBriefingResearchService service,
        TradingCliRenderer renderer,
        IOptions<AutomationOptions> options)
    {
        _service = service;
        _renderer = renderer;
        _options = options.Value;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, AutomationBriefSettings settings, CancellationToken cancellationToken)
    {
        var tradingDate = AutomationBriefSettings.ResolveTradingDate(settings.Date, _options.Timezone);
        var result = await _service.RunAsync(tradingDate, cancellationToken);
        _renderer.WriteDailyBriefResearch(tradingDate, result);
        return 0;
    }
}

[Description("Generate and save the trading-day plan for a trading date.")]
public sealed class AutomationBriefPlanCommand : AsyncCommand<AutomationBriefSettings>
{
    private readonly DailyBriefingPlanService _service;
    private readonly TradingCliRenderer _renderer;
    private readonly AutomationOptions _options;

    public AutomationBriefPlanCommand(
        DailyBriefingPlanService service,
        TradingCliRenderer renderer,
        IOptions<AutomationOptions> options)
    {
        _service = service;
        _renderer = renderer;
        _options = options.Value;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, AutomationBriefSettings settings, CancellationToken cancellationToken)
    {
        var tradingDate = AutomationBriefSettings.ResolveTradingDate(settings.Date, _options.Timezone);
        var plan = await _service.RunAsync(tradingDate, cancellationToken);
        _renderer.WriteTradingDayPlan(plan);
        return 0;
    }
}

[Description("Convert an existing research markdown brief into a trading-day plan.")]
public sealed class AutomationBriefConvertCommand : AsyncCommand<AutomationBriefConvertSettings>
{
    private readonly DailyPlanConverter _converter;
    private readonly TradingCliRenderer _renderer;
    private readonly AutomationOptions _options;
    private readonly StrategyRules _rules;
    private readonly ITradingClock _tradingClock;

    public AutomationBriefConvertCommand(
        DailyPlanConverter converter,
        TradingCliRenderer renderer,
        IOptions<AutomationOptions> options,
        StrategyRules rules,
        ITradingClock tradingClock)
    {
        _converter = converter;
        _renderer = renderer;
        _options = options.Value;
        _rules = rules;
        _tradingClock = tradingClock;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, AutomationBriefConvertSettings settings, CancellationToken cancellationToken)
    {
        var tradingDate = AutomationBriefSettings.ResolveTradingDate(settings.Date, _options.Timezone);
        var markdown = await File.ReadAllTextAsync(settings.Input, cancellationToken);
        var request = new DailyBriefingRequest(new TradingDayRequest(tradingDate), _rules, _tradingClock.UtcNow);
        var plan = await _converter.ConvertAsync(request, markdown, cancellationToken);
        _renderer.WriteTradingDayPlan(plan);
        return 0;
    }
}

[Description("Run the 15-minute intraday opportunity scan once.")]
public sealed class AutomationIntradayScanCommand : AsyncCommand<AutomationIntradayScanSettings>
{
    private readonly IntradayOpportunityScanService _service;
    private readonly TradingCliRenderer _renderer;
    private readonly AutomationOptions _options;

    public AutomationIntradayScanCommand(
        IntradayOpportunityScanService service,
        TradingCliRenderer renderer,
        IOptions<AutomationOptions> options)
    {
        _service = service;
        _renderer = renderer;
        _options = options.Value;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, AutomationIntradayScanSettings settings, CancellationToken cancellationToken)
    {
        var requestedAtUtc = settings.ResolveRequestedAtUtc();
        var tradingDate = settings.ResolveTradingDate(_options.Timezone, requestedAtUtc);
        var result = await _service.RunAsync(tradingDate, requestedAtUtc, cancellationToken);

        if (result is null)
        {
            _renderer.WriteInfo("No eligible intraday opportunity scan result was produced.");
            return 0;
        }

        _renderer.WriteIntradayOpportunitySubmitResult(result);
        return 0;
    }
}

[Description("Prepare the intraday prompt payload and chart artifacts without calling OpenAI.")]
public sealed class AutomationIntradayPrepareCommand : AsyncCommand<AutomationIntradayScanSettings>
{
    private readonly IntradayOpportunityScanService _service;
    private readonly TradingCliRenderer _renderer;
    private readonly AutomationOptions _options;

    public AutomationIntradayPrepareCommand(
        IntradayOpportunityScanService service,
        TradingCliRenderer renderer,
        IOptions<AutomationOptions> options)
    {
        _service = service;
        _renderer = renderer;
        _options = options.Value;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, AutomationIntradayScanSettings settings, CancellationToken cancellationToken)
    {
        var requestedAtUtc = settings.ResolveRequestedAtUtc();
        var tradingDate = settings.ResolveTradingDate(_options.Timezone, requestedAtUtc);
        var result = await _service.PrepareAsync(tradingDate, requestedAtUtc, cancellationToken);

        if (result is null)
        {
            _renderer.WriteInfo("No eligible intraday preparation result was produced.");
            return 0;
        }

        _renderer.WriteIntradayOpportunityPreparation(result);
        return 0;
    }
}

[Description("Submit a prepared intraday prompt payload to OpenAI.")]
public sealed class AutomationIntradaySubmitCommand : AsyncCommand<AutomationIntradaySubmitSettings>
{
    private readonly IntradayOpportunityScanService _service;
    private readonly TradingCliRenderer _renderer;

    public AutomationIntradaySubmitCommand(
        IntradayOpportunityScanService service,
        TradingCliRenderer renderer)
    {
        _service = service;
        _renderer = renderer;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, AutomationIntradaySubmitSettings settings, CancellationToken cancellationToken)
    {
        var result = await _service.SubmitAsync(settings.Input, cancellationToken);
        _renderer.WriteIntradayOpportunitySubmitResult(result);
        return 0;
    }
}

[Description("Evaluate decision audit records against locally stored market data.")]
public sealed class AutomationAuditEvaluateCommand : AsyncCommand<AutomationAuditEvaluateSettings>
{
    private readonly IDecisionAuditEvaluationService _evaluationService;
    private readonly TradingCliRenderer _renderer;

    public AutomationAuditEvaluateCommand(
        IDecisionAuditEvaluationService evaluationService,
        TradingCliRenderer renderer)
    {
        _evaluationService = evaluationService;
        _renderer = renderer;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, AutomationAuditEvaluateSettings settings, CancellationToken cancellationToken)
    {
        var report = await _evaluationService.EvaluateAsync(
            new DecisionAuditEvaluationRequest(
                settings.Root,
                settings.ResolveTradingDate(),
                CliParsing.ParsePriceResolution(settings.Resolution),
                settings.StrictData,
                settings.MaxAssessmentMissingBars,
                settings.MaxAssessmentConsecutiveMissingBars,
                settings.MaxAssessmentMissingRatio),
            cancellationToken);
        _renderer.WriteDecisionAuditEvaluation(report);
        return 0;
    }
}

public class AutomationBriefSettings : CommandSettings
{
    [CommandOption("--date <YYYY-MM-DD>")]
    public string? Date { get; init; }

    public override ValidationResult Validate()
    {
        if (Date is null)
        {
            return ValidationResult.Success();
        }

        return DateOnly.TryParseExact(Date, "yyyy-MM-dd", out _)
            ? ValidationResult.Success()
            : ValidationResult.Error("Option --date must be in yyyy-MM-dd format.");
    }

    internal static DateOnly ResolveTradingDate(string? value, string timezoneId)
    {
        if (value is not null)
        {
            return DateOnly.ParseExact(value, "yyyy-MM-dd");
        }

        var timezone = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
        var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timezone);
        return DateOnly.FromDateTime(localNow.DateTime);
    }
}

public sealed class AutomationBriefConvertSettings : AutomationBriefSettings
{
    [CommandOption("--input <PATH>")]
    public string Input { get; init; } = string.Empty;

    public override ValidationResult Validate()
    {
        var baseValidation = base.Validate();
        if (!baseValidation.Successful)
        {
            return baseValidation;
        }

        if (string.IsNullOrWhiteSpace(Input))
        {
            return ValidationResult.Error("Missing required option --input.");
        }

        return File.Exists(Input)
            ? ValidationResult.Success()
            : ValidationResult.Error("Option --input must point to an existing markdown file.");
    }
}

public sealed class AutomationIntradayScanSettings : AutomationBriefSettings
{
    [CommandOption("--at <UTC-ISO>")]
    public string? At { get; init; }

    public override ValidationResult Validate()
    {
        var baseValidation = base.Validate();
        if (!baseValidation.Successful)
        {
            return baseValidation;
        }

        return At is null || DateTimeOffset.TryParse(At, out _)
            ? ValidationResult.Success()
            : ValidationResult.Error("Option --at must be a valid UTC timestamp.");
    }

    public DateTimeOffset ResolveRequestedAtUtc()
        => At is null ? DateTimeOffset.UtcNow : DateTimeOffset.Parse(At).ToUniversalTime();

    public DateOnly ResolveTradingDate(string timezoneId, DateTimeOffset requestedAtUtc)
    {
        if (Date is not null)
        {
            return DateOnly.ParseExact(Date, "yyyy-MM-dd");
        }

        var timezone = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
        var localNow = TimeZoneInfo.ConvertTime(requestedAtUtc, timezone);
        return DateOnly.FromDateTime(localNow.DateTime);
    }
}

public sealed class AutomationIntradaySubmitSettings : CommandSettings
{
    [CommandOption("--input <PATH>")]
    public string Input { get; init; } = string.Empty;

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(Input))
        {
            return ValidationResult.Error("Missing required option --input.");
        }

        return File.Exists(Input)
            ? ValidationResult.Success()
            : ValidationResult.Error("Option --input must point to an existing preparation JSON file.");
    }
}

public sealed class AutomationAuditEvaluateSettings : CommandSettings
{
    [CommandOption("--root <PATH>")]
    public string Root { get; init; } = Path.Combine("Logs", "Observability");

    [CommandOption("--date <YYYY-MM-DD>")]
    public string? Date { get; init; }

    [CommandOption("--resolution <RESOLUTION>")]
    public string Resolution { get; init; } = "5minute";

    [CommandOption("--strict-data")]
    [Description("Require every expected final bar in each audit outcome window.")]
    public bool StrictData { get; init; }

    [CommandOption("--max-assessment-missing-bars <COUNT>")]
    [Description("Maximum small interior missing bars tolerated for market assessment outcomes.")]
    public int MaxAssessmentMissingBars { get; init; } = 1;

    [CommandOption("--max-assessment-consecutive-missing-bars <COUNT>")]
    [Description("Maximum consecutive interior missing bars tolerated for market assessment outcomes.")]
    public int MaxAssessmentConsecutiveMissingBars { get; init; } = 1;

    [CommandOption("--max-assessment-missing-ratio <RATIO>")]
    [Description("Maximum interior missing-bar ratio tolerated for market assessment outcomes.")]
    public decimal MaxAssessmentMissingRatio { get; init; } = 0.10m;

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(Root))
        {
            return ValidationResult.Error("Option --root must not be empty.");
        }

        if (!Directory.Exists(Root))
        {
            return ValidationResult.Error("Option --root must point to an existing observability directory.");
        }

        if (Date is not null && !DateOnly.TryParseExact(Date, "yyyy-MM-dd", out _))
        {
            return ValidationResult.Error("Option --date must be in yyyy-MM-dd format.");
        }

        if (!CliParsing.IsValidPriceResolution(Resolution))
        {
            return ValidationResult.Error("Option --resolution is not supported.");
        }

        if (MaxAssessmentMissingBars < 0)
        {
            return ValidationResult.Error("Option --max-assessment-missing-bars must be zero or greater.");
        }

        if (MaxAssessmentConsecutiveMissingBars < 0)
        {
            return ValidationResult.Error("Option --max-assessment-consecutive-missing-bars must be zero or greater.");
        }

        return MaxAssessmentMissingRatio >= 0m && MaxAssessmentMissingRatio <= 1m
            ? ValidationResult.Success()
            : ValidationResult.Error("Option --max-assessment-missing-ratio must be between 0 and 1.");
    }

    public DateOnly? ResolveTradingDate()
        => Date is null ? null : DateOnly.ParseExact(Date, "yyyy-MM-dd");
}
