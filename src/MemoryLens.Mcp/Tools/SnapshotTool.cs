using System.ComponentModel;
using System.Text.Json;
using MemoryLens.Mcp.Profiler;
using ModelContextProtocol.Server;

namespace MemoryLens.Mcp.Tools;

[McpServerToolType]
public class SnapshotTool(SnapshotManager snapshotManager)
{
    [McpServerTool, Description(
        "Takes a memory snapshot of a .NET process. " +
        "Provide either a pid, processName, or command to profile. " +
        "Optionally specify durationSeconds to wait before capturing.")]
    public async Task<string> snapshot(
        [Description("Process ID to snapshot")] int? pid = null,
        [Description("Process name to snapshot")] string? processName = null,
        [Description("Command to launch and snapshot")] string? command = null,
        [Description("Seconds to wait before taking snapshot")] int? durationSeconds = null,
        CancellationToken ct = default)
    {
        var result = await snapshotManager.TakeSnapshotAsync(pid, processName, command, durationSeconds, ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }
}
