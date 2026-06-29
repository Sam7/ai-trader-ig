using Microsoft.Extensions.Configuration;

namespace Trading.IG.Tests;

internal static class IgDemoIntegrationConfiguration
{
    public const string DemoBaseUrl = "https://demo-api.ig.com/gateway/deal";

    public static IConfigurationRoot Build(bool? useEncryptedPasswordOverride = null)
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(FindRepositoryRoot())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables();

        if (useEncryptedPasswordOverride is not null)
        {
            builder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["IG:UseEncryptedPassword"] = useEncryptedPasswordOverride.Value.ToString().ToLowerInvariant(),
            });
        }

        return builder.Build();
    }

    public static bool TryValidateDemoConfiguration(out string skipReason)
    {
        var configuration = Build();
        if (!string.Equals(configuration["IG:BaseUrl"], DemoBaseUrl, StringComparison.OrdinalIgnoreCase))
        {
            skipReason = "IG:BaseUrl is not configured as the IG demo endpoint.";
            return false;
        }

        foreach (var key in new[] { "IG:ApiKey", "IG:Identifier", "IG:Password" })
        {
            if (string.IsNullOrWhiteSpace(configuration[key]))
            {
                skipReason = $"Missing required IG demo configuration value: {key}";
                return false;
            }
        }

        skipReason = string.Empty;
        return true;
    }

    public static string? GetValue(IConfiguration configuration, string primaryKey, string legacyKey)
        => configuration[primaryKey] ?? configuration[legacyKey];

    private static string FindRepositoryRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var current = new DirectoryInfo(start);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "Trading.slnx")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }
        }

        throw new InvalidOperationException("Could not locate repository root for IG integration test configuration.");
    }
}
