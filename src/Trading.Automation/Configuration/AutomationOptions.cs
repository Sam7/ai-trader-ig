using Trading.Automation.Execution;

namespace Trading.Automation.Configuration;

public sealed class AutomationOptions
{
    public const string SectionName = "Automation";

    public bool Enabled { get; init; } = true;

    public string DailyBriefCron { get; init; } = "0 0 8 * * *";

    public string Timezone { get; init; } = "Australia/Melbourne";

    public string JobName { get; init; } = DailyBriefingConstants.JobName;
}
