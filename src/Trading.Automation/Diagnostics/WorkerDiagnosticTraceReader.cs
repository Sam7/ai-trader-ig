using System.Text.Json;

namespace Trading.Automation.Diagnostics;

/// <summary>Reads both legacy version-one and current version-two JSONL samples without inventing missing evidence.</summary>
internal static class WorkerDiagnosticTraceReader
{
    public static bool TryParse(string line, out WorkerDiagnosticSnapshot? snapshot)
    {
        snapshot = null;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var parsed = JsonSerializer.Deserialize(
                line,
                WorkerDiagnosticsJsonContext.Default.WorkerDiagnosticSnapshot);
            if (parsed is null)
            {
                return false;
            }

            var schemaVersion = document.RootElement.TryGetProperty("schemaVersion", out var schema)
                && schema.TryGetInt32(out var declaredVersion)
                    ? declaredVersion
                    : 1;
            snapshot = parsed with { SchemaVersion = schemaVersion };
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
