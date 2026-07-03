using System.Text.Json;

namespace Trading.IG.Tests;

internal sealed class BrokerBaselineEvidenceWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    private readonly List<BrokerBaselineEvidenceEntry> _entries = [];
    private readonly string _jsonlPath;
    private readonly string _summaryPath;

    private BrokerBaselineEvidenceWriter(string rootPath)
    {
        Directory.CreateDirectory(rootPath);
        _jsonlPath = Path.Combine(rootPath, "broker-baseline.jsonl");
        _summaryPath = Path.Combine(rootPath, "broker-baseline.md");
        File.Delete(_jsonlPath);
        File.Delete(_summaryPath);
    }

    public static BrokerBaselineEvidenceWriter Create()
    {
        var rootPath = Environment.GetEnvironmentVariable("BROKER_BASELINE_EVIDENCE_ROOT");
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            rootPath = Path.Combine(
                FindRepositoryRoot(),
                "artifacts",
                "verification",
                $"broker-baseline-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssZ}",
                "broker-baseline");
        }

        return new BrokerBaselineEvidenceWriter(rootPath);
    }

    public string SummaryPath => _summaryPath;

    public async Task RecordAsync(string scenario, string step, string status, object? details = null)
    {
        var entry = new BrokerBaselineEvidenceEntry(
            DateTimeOffset.UtcNow,
            scenario,
            step,
            status,
            Sanitize(details));

        _entries.Add(entry);
        await File.AppendAllTextAsync(_jsonlPath, JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine);
        await WriteSummaryAsync();
    }

    private async Task WriteSummaryAsync()
    {
        var lines = new List<string>
        {
            "# Broker Baseline Evidence",
            string.Empty,
            $"Updated UTC: `{DateTimeOffset.UtcNow:O}`",
            string.Empty,
            "| Time UTC | Scenario | Step | Status |",
            "|---|---|---|---|",
        };

        lines.AddRange(_entries.Select(entry =>
            $"| `{entry.AtUtc:O}` | {Escape(entry.Scenario)} | {Escape(entry.Step)} | {Escape(entry.Status)} |"));

        lines.Add(string.Empty);
        lines.Add($"JSONL: `{Path.GetFileName(_jsonlPath)}`");

        await File.WriteAllLinesAsync(_summaryPath, lines);
    }

    private static object? Sanitize(object? value)
    {
        if (value is null)
        {
            return null;
        }

        var json = JsonSerializer.Serialize(value, JsonOptions);
        json = RedactKnownSensitiveValues(json);
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    private static string RedactKnownSensitiveValues(string value)
    {
        foreach (var key in new[]
        {
            "IG__ApiKey",
            "IG__Identifier",
            "IG__Password",
            "IG__AccountId",
            "AI__OpenAI__ApiKey",
            "OpenAI__ApiKey",
            "OPENAI_API_KEY",
        })
        {
            var secret = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(secret) && secret.Length > 2)
            {
                value = value.Replace(secret, $"[REDACTED:{key}]", StringComparison.Ordinal);
            }
        }

        return value;
    }

    private static string Escape(string value)
        => value.Replace("|", "\\|", StringComparison.Ordinal);

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

        throw new InvalidOperationException("Could not locate repository root for broker baseline evidence.");
    }

    private sealed record BrokerBaselineEvidenceEntry(
        DateTimeOffset AtUtc,
        string Scenario,
        string Step,
        string Status,
        object? Details);
}
