namespace MemoryLens.Mcp.Profiler;

public class ProcessFilter
{
    private static readonly string[] ExcludedProcessNames =
    [
        "devenv",
        "rider64",
        "JetBrains.Rider",
        "Code",
        "code-insiders",
        "ServiceHub",
        "dotnet-dotmemory",
        "MemoryLens.Mcp"
    ];

    private static readonly string[] ExcludedCommandLinePatterns =
    [
        "roslyn-codegraph-mcp",
        "memorylens-mcp"
    ];

    public bool IsExcluded(string processName, string commandLine)
    {
        if (ExcludedProcessNames.Any(excluded =>
                processName.StartsWith(excluded, StringComparison.OrdinalIgnoreCase)))
            return true;

        if (!string.IsNullOrEmpty(commandLine) &&
            ExcludedCommandLinePatterns.Any(pattern =>
                commandLine.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }
}
