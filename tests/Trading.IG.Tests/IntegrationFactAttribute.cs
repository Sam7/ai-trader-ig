namespace Trading.IG.Tests;

[AttributeUsage(AttributeTargets.Method)]
public sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute()
    {
        var runIntegration = Environment.GetEnvironmentVariable("RUN_IG_INTEGRATION");
        if (!string.Equals(runIntegration, "true", StringComparison.OrdinalIgnoreCase))
        {
            Skip = "Set RUN_IG_INTEGRATION=true to run IG demo integration tests.";
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
