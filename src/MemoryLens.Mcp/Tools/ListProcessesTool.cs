using System.ComponentModel;
using System.Text.Json;
using MemoryLens.Mcp.Profiler;
using ModelContextProtocol.Server;

namespace MemoryLens.Mcp.Tools;

[McpServerToolType]
public class ListProcessesTool(IProcessRunner processRunner, ProcessFilter processFilter)
{
    [McpServerTool, Description(
        "Lists running .NET processes suitable for memory profiling. " +
        "Excludes IDE, tooling, and MCP server processes to prevent interference.")]
    public async Task<string> list_processes(
        [Description("Optional filter to match process name")] string? filter = null,
        CancellationToken ct = default)
    {
        var result = await processRunner.RunAsync(
            "dotnet", "dotmemory list-processes", ct);

        if (result.ExitCode != 0)
            return $"Failed to list processes: {result.Error}";

        var processes = ParseProcessList(result.Output)
            .Where(p => !processFilter.IsExcluded(p.Name, p.CommandLine))
            .Where(p => filter == null ||
                        p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));

        return JsonSerializer.Serialize(processes, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private static IEnumerable<DotNetProcess> ParseProcessList(string output)
    {
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && int.TryParse(parts[0], out var pid))
            {
                yield return new DotNetProcess(pid, parts[1].Trim(), "");
            }
        }
    }
}

public record DotNetProcess(int Pid, string Name, string CommandLine);
