using Microsoft.Extensions.Configuration;

namespace Trading.Automation.Configuration;

public static class TrackedMarketsConfiguration
{
    public const string SettingPath = "AI:DailyBriefing:TrackedMarketsConfigFile";
    public const string DefaultFileName = "tracked-markets.json";

    public static void AddConfiguredTrackedMarketsFile(ConfigurationManager configuration)
    {
        var configuredPath = configuration[SettingPath];
        var path = string.IsNullOrWhiteSpace(configuredPath) ? DefaultFileName : configuredPath;
        configuration.AddJsonFile(path, optional: true, reloadOnChange: false);
    }
}
