using System.Text.Json;

namespace Trading.MarketData;

public sealed class FileMarketDataMirrorStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly MarketDataOptions _options;

    public FileMarketDataMirrorStateStore(Microsoft.Extensions.Options.IOptions<MarketDataOptions> options)
    {
        _options = options.Value;
    }

    public async Task<MarketDataMirrorState?> LoadAsync(CancellationToken cancellationToken = default)
    {
        var path = Path.GetFullPath(_options.CloudSnapshot.Mirror.StatePath);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<MarketDataMirrorState>(stream, JsonOptions, cancellationToken);
    }

    public async Task SaveAsync(MarketDataMirrorState state, CancellationToken cancellationToken = default)
    {
        var path = Path.GetFullPath(_options.CloudSnapshot.Mirror.StatePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = $"{path}.tmp";

        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
        }

        File.Move(tempPath, path, overwrite: true);
    }
}
