#pragma warning disable MA0048 // File name must match type name - intentional companion types
namespace MemoryLens.Mcp.Profiler;

public record ToolStatus(bool IsInstalled, string? Version, string Message);

public class DotMemoryToolManager(IProcessRunner processRunner)
{
    public async Task<ToolStatus> EnsureInstalledAsync(CancellationToken ct = default)
    {
        var listResult = await processRunner.RunAsync("dotnet", "tool list -g", ct).ConfigureAwait(false);

        if (listResult.ExitCode == 0 && listResult.Output.Contains("dotnet-dotmemory"))
        {
            var version = ParseVersion(listResult.Output);
            await processRunner.RunAsync("dotnet", "tool update -g dotnet-dotmemory", ct).ConfigureAwait(false);
            return new ToolStatus(true, version, $"dotnet-dotmemory {version} is installed.");
        }

        var installResult = await processRunner.RunAsync("dotnet", "tool install -g dotnet-dotmemory", ct).ConfigureAwait(false);

        if (installResult.ExitCode != 0)
            return new ToolStatus(false, null, $"Failed to install dotnet-dotmemory: {installResult.Error}");

        return new ToolStatus(true, null, "dotnet-dotmemory installed successfully.");
    }

    private static string? ParseVersion(string output)
    {
        foreach (var line in output.Split('\n'))
        {
            if (!line.Contains("dotnet-dotmemory", StringComparison.OrdinalIgnoreCase))
                continue;
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 ? parts[1] : null;
        }
        return null;
    }
}
