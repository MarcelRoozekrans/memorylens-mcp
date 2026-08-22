using System.ComponentModel;
using System.Text.Json;
using MemoryLens.Mcp.Profiler;
using ModelContextProtocol.Server;

namespace MemoryLens.Mcp.Tools;

[McpServerToolType]
public class SnapshotTool(IHeapCollector collector, ISnapshotStore store, ProcessFilter processFilter)
{
    [McpServerTool, Description(
        "Takes a memory snapshot of a running .NET process. " +
        "Provide a pid. Returns a snapshot id to pass to analyze.")]
    public async Task<string> snapshot(
        [Description("Process ID to snapshot")] int? pid = null,
        [Description("Process name, used only to apply the profiling exclusion list")] string? processName = null,
        [Description("Not implemented; a process id is required")] string? command = null,
        [Description("Seconds to wait before taking snapshot")] int? durationSeconds = null,
        CancellationToken ct = default)
    {
        if (pid is null)
            return Fail("A process id is required. Use list_processes to find one.");

        if (processName is not null && processFilter.IsExcluded(processName, ""))
            return Fail($"Process '{processName}' is excluded from profiling.");

        if (durationSeconds is > 0)
            await Task.Delay(TimeSpan.FromSeconds(durationSeconds.Value), ct).ConfigureAwait(false);

        try
        {
            var data = await collector.CollectAsync(pid.Value, ct).ConfigureAwait(false);
            var id = await store.SaveAsync(data, ct).ConfigureAwait(false);

            return Serialize(new SnapshotResult(
                true, id, (store as SnapshotStore)?.PathFor(id), null));
        }
        catch (HeapCollectionException ex)
        {
            return Fail(ex.Message);
        }
    }

    private static string Fail(string error) =>
        Serialize(new SnapshotResult(false, null, null, error));

    private static string Serialize(SnapshotResult result) =>
        JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
}
