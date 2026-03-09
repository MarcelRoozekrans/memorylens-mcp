namespace MemoryLens.Mcp.Analysis;

/// <summary>
/// Analyzes memory snapshot files and extracts structured type/heap data.
/// </summary>
public interface IDotMemoryAnalyzer
{
    Task<SnapshotData> AnalyzeSnapshotAsync(string snapshotPath, CancellationToken ct = default);
    Task<ComparisonData> CompareSnapshotsAsync(string beforePath, string afterPath, CancellationToken ct = default);
}
