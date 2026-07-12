namespace Trading.Automation.Configuration;

public sealed class AlertingOptions
{
    public const string SectionName = "Alerting";

    public SlackAlertOptions Slack { get; init; } = new();
}

public sealed class SlackAlertOptions
{
    public bool Enabled { get; init; }

    public string WebhookUrl { get; init; } = string.Empty;

    public TimeSpan Cooldown { get; init; } = TimeSpan.FromMinutes(30);

    public WorkerAlertSeverity SeverityThreshold { get; init; } = WorkerAlertSeverity.Warning;

    public TimeSpan StartupSuppressionWindow { get; init; } = TimeSpan.FromMinutes(2);
}

public enum WorkerAlertSeverity
{
    Info = 0,
    Warning = 1,
    Critical = 2,
}
