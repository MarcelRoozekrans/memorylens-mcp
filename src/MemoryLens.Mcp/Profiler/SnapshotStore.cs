using System.Globalization;
using System.Text.Json;
using MemoryLens.Mcp.Analysis;

namespace MemoryLens.Mcp.Profiler;

public interface ISnapshotStore
{
    /// <summary>Persists a snapshot and returns its generated id.</summary>
    Task<string> SaveAsync(SnapshotData data, CancellationToken ct);

    /// <summary>Loads a snapshot by id, or by full path to a snapshot file.</summary>
    Task<SnapshotData> LoadAsync(string idOrPath, CancellationToken ct);
}

/// <summary>
/// Persists snapshots as JSON under a snapshot directory. Snapshots are per-type
/// aggregates, not object graphs, so they stay small.
/// </summary>
public sealed class SnapshotStore : ISnapshotStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    private readonly string _root;

    public SnapshotStore(string? rootDirectory = null)
    {
        _root = rootDirectory
            ?? Path.Combine(Path.GetTempPath(), "memorylens-snapshots");
    }

    public string PathFor(string snapshotId) => Path.Combine(_root, snapshotId + ".json");

    public async Task<string> SaveAsync(SnapshotData data, CancellationToken ct)
    {
        Directory.CreateDirectory(_root);

        var id = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..8];
        var stream = File.Create(PathFor(id));
        await using (stream.ConfigureAwait(false))
            await JsonSerializer.SerializeAsync(stream, data, Options, ct).ConfigureAwait(false);

        return id;
    }

    public async Task<SnapshotData> LoadAsync(string idOrPath, CancellationToken ct)
    {
        var path = File.Exists(idOrPath) ? idOrPath : PathFor(idOrPath);

        if (!File.Exists(path))
            throw new FileNotFoundException($"Snapshot '{idOrPath}' not found (looked in '{path}').", path);

        var stream = File.OpenRead(path);
        await using (stream.ConfigureAwait(false))
        {
            return await JsonSerializer.DeserializeAsync<SnapshotData>(stream, Options, ct).ConfigureAwait(false)
                ?? throw new InvalidDataException($"Snapshot '{idOrPath}' deserialized to null.");
        }
    }
}
