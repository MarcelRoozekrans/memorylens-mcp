namespace MemoryLens.Mcp.Profiler;

public record ComparisonResult(bool Success, string? SnapshotId, string? BeforePath, string? AfterPath, int SnapshotCount, string? Error);
