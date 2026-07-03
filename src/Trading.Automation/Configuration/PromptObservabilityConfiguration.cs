using Microsoft.Extensions.Configuration;
using Trading.AI.Configuration;

namespace Trading.Automation.Configuration;

public static class PromptObservabilityConfiguration
{
    public static void ApplyRootOverride(ConfigurationManager configuration, string? rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return;
        }

        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{PromptObservabilityOptions.SectionName}:ObservabilityRootPath"] = rootPath,
        });
    }
}
