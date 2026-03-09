namespace MemoryLens.Mcp.Profiler;

public record SnapshotResult(bool Success, string? SnapshotId, string? SnapshotPath, string? Error);
