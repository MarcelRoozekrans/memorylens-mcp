using System.ComponentModel;
using System.Text.Json;
using MemoryLens.Mcp.Profiler;
using ModelContextProtocol.Server;

namespace MemoryLens.Mcp.Tools;

[McpServerToolType]
public class CompareSnapshotsTool(IHeapCollector collector, ISnapshotStore store, ProcessFilter processFilter)
{
    [McpServerTool, Description(
        "Takes two memory snapshots of a .NET process with a delay between them " +
        "for comparison. Useful for detecting memory leaks by comparing before/after state. " +
        "Provide a pid. Returns the ids and paths of both snapshots.")]
    public async Task<string> compare_snapshots(
        [Description("Process ID to snapshot")] int? pid = null,
        [Description("Process name, used only to apply the profiling exclusion list")] string? processName = null,
        [Description("Not implemented; a process id is required")] string? command = null,
        [Description("Seconds to wait between before and after snapshots (default: 10)")] int? delaySeconds = null,
        CancellationToken ct = default)
    {
        if (pid is null)
            return Serialize(new ComparisonResult(false, null, null, null, 0,
                "A process id is required. Use list_processes to find one."));

        if (processName is not null && processFilter.IsExcluded(processName, ""))
            return Serialize(new ComparisonResult(false, null, null, null, 0,
                $"Process '{processName}' is excluded from profiling."));

        try
        {
            var beforeId = await store.SaveAsync(
                await collector.CollectAsync(pid.Value, ct).ConfigureAwait(false), ct).ConfigureAwait(false);

            await Task.Delay(TimeSpan.FromSeconds(delaySeconds ?? 10), ct).ConfigureAwait(false);

            var afterId = await store.SaveAsync(
                await collector.CollectAsync(pid.Value, ct).ConfigureAwait(false), ct).ConfigureAwait(false);

            var paths = store as SnapshotStore;
            return Serialize(new ComparisonResult(
                true, afterId, paths?.PathFor(beforeId), paths?.PathFor(afterId), 2, null));
        }
        catch (HeapCollectionException ex)
        {
            return Serialize(new ComparisonResult(false, null, null, null, 0, ex.Message));
        }
    }

    private static string Serialize(ComparisonResult result) =>
        JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
}
