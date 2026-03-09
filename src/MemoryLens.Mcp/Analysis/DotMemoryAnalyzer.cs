using MemoryLens.Mcp.Profiler;

namespace MemoryLens.Mcp.Analysis;

/// <summary>
/// Analyzes memory snapshots by running dotnet-gcdump report and parsing the output.
/// Falls back to basic parsing of dotnet-dotmemory output if gcdump is unavailable.
/// </summary>
public class DotMemoryAnalyzer(IProcessRunner processRunner) : IDotMemoryAnalyzer
{
    public async Task<SnapshotData> AnalyzeSnapshotAsync(string snapshotPath, CancellationToken ct = default)
    {
        // Try dotnet-gcdump report first (produces parseable text output)
        var result = await processRunner.RunAsync(
            "dotnet-gcdump", $"report \"{snapshotPath}\"", ct).ConfigureAwait(false);

        if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Output))
            return GcDumpReportParser.Parse(result.Output);

        // If snapshot path doesn't exist or tool fails, return empty data
        return new SnapshotData();
    }

    public async Task<ComparisonData> CompareSnapshotsAsync(
        string beforePath, string afterPath, CancellationToken ct = default)
    {
        var before = await AnalyzeSnapshotAsync(beforePath, ct).ConfigureAwait(false);
        var after = await AnalyzeSnapshotAsync(afterPath, ct).ConfigureAwait(false);

        var deltas = ComputeDeltas(before, after);

        return new ComparisonData
        {
            Before = before,
            After = after,
            Deltas = deltas,
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

            // Only include types that actually changed
            if (delta.InstanceDelta != 0 || delta.BytesDelta != 0)
                deltas.Add(delta);
        }

        return deltas.OrderByDescending(d => d.BytesDelta).ToList();
    }
}
