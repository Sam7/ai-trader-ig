using System.Text.Json;
using Trading.IG;

public sealed class FileOrderReferenceJournal : IOrderReferenceJournal
{
    private const int MaxEntries = 256;
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _filePath;

    public FileOrderReferenceJournal()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Trading.Cli");
        Directory.CreateDirectory(root);
        _filePath = Path.Combine(root, "order-journal.json");
    }

    public async Task SaveAsync(OrderSubmissionRecord record, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var records = await LoadAllAsync(cancellationToken);
            records[record.DealReference] = record;
            await WriteAllAsync(records, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OrderSubmissionRecord?> GetAsync(string dealReference, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var records = await LoadAllAsync(cancellationToken);
            return records.GetValueOrDefault(dealReference);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<string, OrderSubmissionRecord>> LoadAllAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        try
        {
            await using var stream = File.OpenRead(_filePath);
            var records = await JsonSerializer.DeserializeAsync<Dictionary<string, OrderSubmissionRecord>>(stream, JsonOptions, cancellationToken);
            return Prune(records ?? []);
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task WriteAllAsync(Dictionary<string, OrderSubmissionRecord> records, CancellationToken cancellationToken)
    {
        var prunedRecords = Prune(records);
        var tempFilePath = $"{_filePath}.tmp";

        await using (var stream = File.Create(tempFilePath))
        {
            await JsonSerializer.SerializeAsync(stream, prunedRecords, JsonOptions, cancellationToken);
        }

        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }

        File.Move(tempFilePath, _filePath);
    }

    private static Dictionary<string, OrderSubmissionRecord> Prune(Dictionary<string, OrderSubmissionRecord> records)
    {
        var cutoff = DateTimeOffset.UtcNow - Retention;

        return records
            .Where(pair => pair.Value.SubmittedAtUtc >= cutoff)
            .OrderByDescending(pair => pair.Value.SubmittedAtUtc)
            .Take(MaxEntries)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }
}
