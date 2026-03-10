namespace Trading.IG.Tests;

[AttributeUsage(AttributeTargets.Method)]
public sealed class OptionalIntegrationFactAttribute : FactAttribute
{
    public OptionalIntegrationFactAttribute(string optInEnvironmentVariable, string description)
    {
        var runIntegration = Environment.GetEnvironmentVariable("RUN_IG_INTEGRATION");
        if (!string.Equals(runIntegration, "true", StringComparison.OrdinalIgnoreCase))
        {
            Skip = "Set RUN_IG_INTEGRATION=true to run IG demo integration tests.";
            return;
        }

        var runOptional = Environment.GetEnvironmentVariable(optInEnvironmentVariable);
        if (!string.Equals(runOptional, "true", StringComparison.OrdinalIgnoreCase))
        {
            Skip = $"Set {optInEnvironmentVariable}=true to run optional integration test: {description}.";
            return;
        }

        var required = new[]
        {
            "IG__BaseUrl",
            "IG__ApiKey",
            "IG__Identifier",
            "IG__Password",
        };

        foreach (var key in required)
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)))
            {
                Skip = $"Missing required environment variable: {key}";
                return;
            }
        }
    }
}
