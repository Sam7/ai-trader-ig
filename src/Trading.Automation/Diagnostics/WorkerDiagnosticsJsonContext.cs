using System.Text.Json.Serialization;

namespace Trading.Automation.Diagnostics;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WorkerDiagnosticSnapshot))]
internal sealed partial class WorkerDiagnosticsJsonContext : JsonSerializerContext;
