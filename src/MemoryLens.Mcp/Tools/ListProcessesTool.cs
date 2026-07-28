using System.ComponentModel;
using System.Text.Json;
using MemoryLens.Mcp.Profiler;
using ModelContextProtocol.Server;

namespace MemoryLens.Mcp.Tools;

[McpServerToolType]
public class ListProcessesTool(IDotNetProcessLister processLister, ProcessFilter processFilter)
{
    [McpServerTool, Description(
        "Lists running .NET processes suitable for memory profiling, discovered from " +
        "their diagnostic IPC endpoints. Does not require dotMemory to be installed. " +
        "Excludes IDE, tooling, and MCP server processes to prevent interference.")]
    public Task<string> list_processes(
        [Description("Optional filter to match process name")] string? filter = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var processes = processLister.ListProcesses()
            // Exclude ourselves by pid, not by name. ProcessFilter matches "MemoryLens.Mcp",
            // but launched as `dotnet MemoryLens.Mcp.dll` the process name is just
            // "dotnet" and the module path carries no assembly name, so the server would
            // otherwise offer itself up for profiling.
            .Where(p => p.Pid != Environment.ProcessId)
            .Where(p => !processFilter.IsExcluded(p.Name, p.CommandLine))
            .Where(p => filter == null ||
                        p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(JsonSerializer.Serialize(processes, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }
}
