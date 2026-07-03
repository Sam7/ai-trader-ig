using Microsoft.Extensions.Configuration;
using Trading.AI.Configuration;

namespace Trading.Automation.Configuration;

public static class TrackedMarketsConfiguration
{
    public const string SettingPath = "AI:DailyBriefing:TrackedMarketsConfigFile";
    public const string DefaultFileName = "tracked-markets.json";
    public const string InstrumentFilterPath = "AI:DailyBriefing:TrackedMarketInstrumentFilter";

    public static void AddConfiguredTrackedMarketsFile(ConfigurationManager configuration)
    {
        var configuredPath = configuration[SettingPath];
        var path = string.IsNullOrWhiteSpace(configuredPath) ? DefaultFileName : configuredPath;
        configuration.AddJsonFile(path, optional: true, reloadOnChange: false);
    }

    public static void ApplyTrackedMarketsOverride(ConfigurationManager configuration, IReadOnlyList<string> instrumentIds)
    {
        if (instrumentIds.Count == 0)
        {
            return;
        }

        var configuredMarkets = configuration
            .GetSection($"{DailyBriefingOptions.SectionName}:TrackedMarkets")
            .Get<TrackedMarketOptions[]>() ?? [];
        var marketsByInstrument = configuredMarkets.ToDictionary(market => market.InstrumentId, StringComparer.Ordinal);
        var missingInstrument = instrumentIds.FirstOrDefault(instrument => !marketsByInstrument.ContainsKey(instrument));
        if (missingInstrument is not null)
        {
            throw new InvalidOperationException(
                $"Requested instrument '{missingInstrument}' is not configured in tracked markets.");
        }

        var overrideValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < instrumentIds.Count; index++)
        {
            overrideValues[$"{InstrumentFilterPath}:{index}"] = instrumentIds[index];
        }

        configuration.AddInMemoryCollection(overrideValues);
    }
}
