namespace MemoryLens.Mcp.Rules;

public record SnapshotAnalysisContext(
    string SnapshotId,
    string? SnapshotPath,
    string? BeforePath,
    string? AfterPath,
    bool IsComparison,
    string? WorkingDirectory);
