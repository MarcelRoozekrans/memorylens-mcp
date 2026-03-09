using System.ComponentModel;
using MemoryLens.Mcp.Profiler;
using ModelContextProtocol.Server;

namespace MemoryLens.Mcp.Tools;

[McpServerToolType]
public class EnsureDotMemoryTool(DotMemoryToolManager toolManager)
{
    [McpServerTool, Description(
        "Ensures dotnet-dotmemory is installed and up-to-date. " +
        "Call this before any profiling operation.")]
    public async Task<string> ensure_dotmemory(CancellationToken ct)
    {
        var status = await toolManager.EnsureInstalledAsync(ct);
        return status.Message;
    }
}
