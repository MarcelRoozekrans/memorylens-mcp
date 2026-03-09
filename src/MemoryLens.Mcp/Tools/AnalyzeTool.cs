using System.ComponentModel;
using System.Text.Json;
using MemoryLens.Mcp.Analysis;
using MemoryLens.Mcp.Rules;
using ModelContextProtocol.Server;

namespace MemoryLens.Mcp.Tools;

[McpServerToolType]
public class AnalyzeTool(AnalysisEngine analysisEngine)
{
    [McpServerTool, Description(
        "Analyzes a memory snapshot using built-in rules to detect leaks, " +
        "fragmentation, excessive allocations, and anti-patterns. " +
        "Returns findings with severity, description, and optional code suggestions.")]
    public async Task<string> analyze(
        [Description("Snapshot ID or path to analyze")] string snapshotId,
        [Description("Path to snapshot file")] string? snapshotPath = null,
        [Description("Path to 'before' snapshot for comparison")] string? beforePath = null,
        [Description("Path to 'after' snapshot for comparison")] string? afterPath = null,
        [Description("Working directory for resolving relative paths")] string? workingDirectory = null,
        CancellationToken ct = default)
    {
        var isComparison = beforePath != null && afterPath != null;
        var context = new SnapshotAnalysisContext(
            snapshotId, snapshotPath, beforePath, afterPath, isComparison, workingDirectory);

        var findings = await analysisEngine.AnalyzeAsync(context, ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new { findings, count = findings.Count }, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }
}
