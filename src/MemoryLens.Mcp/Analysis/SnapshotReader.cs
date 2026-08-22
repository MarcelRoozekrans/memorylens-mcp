using MemoryLens.Mcp.Profiler;

namespace MemoryLens.Mcp.Analysis;

public interface ISnapshotReader
{
    Task<SnapshotData> ReadAsync(string idOrPath, CancellationToken ct);
    Task<ComparisonData> CompareAsync(string beforeIdOrPath, string afterIdOrPath, CancellationToken ct);
}

/// <summary>
/// Reads persisted snapshots and computes deltas between them. Replaces the old
/// analyzer, which shelled out to an external tool and parsed its text output.
/// </summary>
public sealed class SnapshotReader(ISnapshotStore store) : ISnapshotReader
{
    public Task<SnapshotData> ReadAsync(string idOrPath, CancellationToken ct) =>
        store.LoadAsync(idOrPath, ct);

    public async Task<ComparisonData> CompareAsync(
        string beforeIdOrPath, string afterIdOrPath, CancellationToken ct)
    {
        var before = await store.LoadAsync(beforeIdOrPath, ct).ConfigureAwait(false);
        var after = await store.LoadAsync(afterIdOrPath, ct).ConfigureAwait(false);

        return new ComparisonData
        {
            Before = before,
            After = after,
            Deltas = ComputeDeltas(before, after),
        };
    }

    private static List<TypeDelta> ComputeDeltas(SnapshotData before, SnapshotData after)
    {
        var beforeTypes = before.Types.ToDictionary(t => t.FullName, StringComparer.Ordinal);
        var afterTypes = after.Types.ToDictionary(t => t.FullName, StringComparer.Ordinal);

        var allTypeNames = beforeTypes.Keys.Union(afterTypes.Keys, StringComparer.Ordinal);
        var deltas = new List<TypeDelta>();

        foreach (var typeName in allTypeNames)
        {
            beforeTypes.TryGetValue(typeName, out var beforeInfo);
            afterTypes.TryGetValue(typeName, out var afterInfo);

            var delta = new TypeDelta
            {
                FullName = typeName,
                InstancesBefore = beforeInfo?.InstanceCount ?? 0,
                InstancesAfter = afterInfo?.InstanceCount ?? 0,
                BytesBefore = beforeInfo?.TotalBytes ?? 0,
                BytesAfter = afterInfo?.TotalBytes ?? 0,
            };

            if (delta.InstanceDelta != 0 || delta.BytesDelta != 0)
                deltas.Add(delta);
        }

        return deltas.OrderByDescending(d => d.BytesDelta).ToList();
    }
}
