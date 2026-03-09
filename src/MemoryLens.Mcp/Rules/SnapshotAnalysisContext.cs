using MemoryLens.Mcp.Analysis;

namespace MemoryLens.Mcp.Rules;

public record SnapshotAnalysisContext(
    string SnapshotId,
    string? SnapshotPath,
    string? BeforePath,
    string? AfterPath,
    bool IsComparison,
    string? WorkingDirectory)
{
    /// <summary>
    /// Parsed snapshot data (single snapshot or the "after" snapshot for comparisons).
    /// </summary>
    public SnapshotData? Data { get; init; }

    /// <summary>
    /// Comparison data when analyzing two snapshots. Only set when IsComparison is true.
    /// </summary>
    public ComparisonData? Comparison { get; init; }
}
