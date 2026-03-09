using System.ComponentModel;
using System.Text.Json;
using MemoryLens.Mcp.Profiler;
using ModelContextProtocol.Server;

namespace MemoryLens.Mcp.Tools;

[McpServerToolType]
public class CompareSnapshotsTool(SnapshotManager snapshotManager)
{
    [McpServerTool, Description(
        "Takes two memory snapshots of a .NET process with a delay between them " +
        "for comparison. Useful for detecting memory leaks by comparing before/after state. " +
        "Provide either a pid, processName, or command to profile.")]
    public async Task<string> compare_snapshots(
        [Description("Process ID to snapshot")] int? pid = null,
        [Description("Process name to snapshot")] string? processName = null,
        [Description("Command to launch and snapshot")] string? command = null,
        [Description("Seconds to wait between before and after snapshots (default: 10)")] int? delaySeconds = null,
        CancellationToken ct = default)
    {
        var result = await snapshotManager.CompareSnapshotsAsync(pid, processName, command, delaySeconds, ct);

        return JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }
}
