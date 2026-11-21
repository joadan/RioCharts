using System.Text.Json;

namespace RioCharts.Core.Specs;

/// <summary>
/// Datasets from the CSV and JSON files in a folder (not subfolders): one dataset per file, named
/// after the file without its extension. JSON files hold an array of objects, or an object with
/// one array of objects. Files are read again when they change, so an agent can write a file and
/// chart it right away. A file that cannot be read is listed with the reason and no fields.
/// </summary>
public sealed class FolderDataSource : IChartDataSource
{
    private readonly string _folder;
    private readonly long _maxFileBytes;
    private readonly Dictionary<string, (DateTime Written, long Length, ChartDataset Dataset)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();

    /// <param name="folder">The folder to read.</param>
    /// <param name="maxFileBytes">Larger files are skipped (listed with the reason). Default 100 MB.</param>
    public FolderDataSource(string folder, long maxFileBytes = 100 * 1024 * 1024)
    {
        _folder = Path.GetFullPath(folder);
        _maxFileBytes = maxFileBytes;
    }

    /// <summary>The full path of the folder.</summary>
    public string Folder => _folder;

    /// <inheritdoc />
    public IReadOnlyList<ChartDataset> Describe() =>
        [.. Files().Select(f => Read(f)).OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)];

    /// <inheritdoc />
    public Task<ChartDataset?> LoadAsync(string name, CancellationToken cancellationToken = default)
    {
        var file = Files().FirstOrDefault(f => string.Equals(Path.GetFileNameWithoutExtension(f), name, StringComparison.OrdinalIgnoreCase));
        var dataset = file is null ? null : Read(file);
        return Task.FromResult(dataset is { Fields.Count: > 0 } ? dataset : null);
    }

    private IEnumerable<string> Files()
    {
        if (!Directory.Exists(_folder))
        {
            return [];
        }

        // When a.csv and a.json both exist, the first in name order wins.
        return Directory.EnumerateFiles(_folder)
            .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".csv" or ".tsv" or ".json")
            .Order(StringComparer.OrdinalIgnoreCase)
            .DistinctBy(f => Path.GetFileNameWithoutExtension(f), StringComparer.OrdinalIgnoreCase);
    }

    private ChartDataset Read(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        var info = new FileInfo(file);
        lock (_lock)
        {
            if (_cache.TryGetValue(file, out var cached) && cached.Written == info.LastWriteTimeUtc && cached.Length == info.Length)
            {
                return cached.Dataset;
            }
        }

        ChartDataset dataset;
        if (info.Length > _maxFileBytes)
        {
            dataset = new ChartDataset(name, [], $"{info.Name}: too large to read ({info.Length / (1024 * 1024)} MB; the limit is {_maxFileBytes / (1024 * 1024)} MB).");
        }
        else
        {
            try
            {
                var text = File.ReadAllText(file);
                var description = $"From {info.Name}";
                dataset = Path.GetExtension(file).Equals(".json", StringComparison.OrdinalIgnoreCase)
                    ? FromJsonFile(name, text, description)
                    : ChartDataset.FromCsv(name, text, description);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or FormatException)
            {
                dataset = new ChartDataset(name, [], $"{info.Name}: could not be read ({e.Message}).");
            }
        }

        lock (_lock)
        {
            _cache[file] = (info.LastWriteTimeUtc, info.Length, dataset);
        }

        return dataset;
    }

    private static ChartDataset FromJsonFile(string name, string text, string description)
    {
        using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Object)
        {
            var arrays = root.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Array).ToList();
            if (arrays.Count != 1)
            {
                throw new FormatException("expected an array of objects, or an object with one array of objects");
            }

            root = arrays[0].Value;
        }

        if (root.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("expected an array of objects");
        }

        return ChartDataset.FromJson(name, root, description);
    }
}
