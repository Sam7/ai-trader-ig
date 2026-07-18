using Trading.Charting.MemoryLab;

var arguments = CommandLineArguments.Parse(args);
var options = new ChartMemoryLabOptions(
    arguments.Get("profile", "production"),
    arguments.GetInt("iterations", 100),
    arguments.GetInt("warmup", 10),
    arguments.Get("output", Path.Combine("artifacts", "chart-memory-lab", DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssZ"))),
    arguments.GetInt("sample-ms", 2),
    arguments.GetBool("full-gc", true),
    arguments.GetBool("write-charts", false));

await new ChartMemoryLabRunner().RunAsync(options);

internal sealed class CommandLineArguments
{
    private readonly IReadOnlyDictionary<string, string> _values;

    private CommandLineArguments(IReadOnlyDictionary<string, string> values) => _values = values;

    public static CommandLineArguments Parse(IReadOnlyList<string> args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unexpected argument '{argument}'.");
            }

            var key = argument[2..];
            var value = index + 1 < args.Count && !args[index + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[++index]
                : "true";
            values[key] = value;
        }

        return new CommandLineArguments(values);
    }

    public string Get(string key, string fallback) => _values.TryGetValue(key, out var value) ? value : fallback;

    public int GetInt(string key, int fallback)
        => int.TryParse(Get(key, fallback.ToString()), out var value) ? value : throw new ArgumentException($"Argument '--{key}' must be an integer.");

    public bool GetBool(string key, bool fallback)
        => bool.TryParse(Get(key, fallback.ToString()), out var value) ? value : throw new ArgumentException($"Argument '--{key}' must be true or false.");
}
