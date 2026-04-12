#pragma warning disable MA0048 // File name must match type name - intentional companion types

namespace MemoryLens.Mcp.Profiler;

public record ToolStatus(bool IsInstalled, string? Version, string Message);

public enum DotMemoryCommandKind
{
    ExplicitPath,
    PathDiscovery,
    LocalTool,
    GlobalTool
}

public sealed record DotMemoryCommand(
    string FileName,
    string ArgumentsPrefix,
    string DisplayName,
    string? Version,
    DotMemoryCommandKind Kind)
{
    public string BuildArguments(string arguments) =>
        string.IsNullOrWhiteSpace(ArgumentsPrefix)
            ? arguments
            : $"{ArgumentsPrefix} {arguments}";
}

public class DotMemoryToolManager(IProcessRunner processRunner)
{
    private static readonly string[] ConfiguredPathVariables =
    [
        "MEMORYLENS_DOTMEMORY_PATH",
        "DOTMEMORY_PATH"
    ];

    private static readonly string[] PathCandidates = OperatingSystem.IsWindows()
        ? ["dotMemory.exe", "dotnet-dotmemory"]
        : ["dotMemory.sh", "dotMemory", "dotnet-dotmemory"];

    private DotMemoryCommand? _cachedCommand;

    public async Task<ToolStatus> EnsureInstalledAsync(CancellationToken ct = default)
    {
        InvalidateCache();
        var command = await ResolveCommandAsync(ct).ConfigureAwait(false);
        if (command is not null)
        {
            // Only attempt global tool update when the resolved command is actually the global tool
            if (command.Kind == DotMemoryCommandKind.GlobalTool)
            {
                var globalToolResult = await TryRunAsync("dotnet", "tool list -g", ct).ConfigureAwait(false);
                if (globalToolResult is not null && globalToolResult.ExitCode == 0 &&
                    ContainsTool(globalToolResult.Output))
                {
                    var version = ParseVersion(globalToolResult.Output);
                    await processRunner.RunAsync("dotnet", "tool update -g dotnet-dotmemory", ct).ConfigureAwait(false);
                    return new ToolStatus(true, version, $"dotnet-dotmemory {version} is installed.");
                }
            }

            return new ToolStatus(
                true,
                string.IsNullOrWhiteSpace(command.Version) ? null : command.Version,
                $"{command.DisplayName} is available.");
        }

        // Legacy compatibility path: keep old install behavior as a fallback only.
        var installResult = await processRunner
            .RunAsync("dotnet", "tool install -g dotnet-dotmemory", ct)
            .ConfigureAwait(false);

        if (installResult.ExitCode != 0)
        {
            var errorMessage = "dotMemory CLI was not found. " +
                "Set DOTMEMORY_PATH to dotMemory.exe/dotMemory.sh, " +
                "put dotMemory in PATH, or install dotnet-dotmemory.";

            if (!string.IsNullOrWhiteSpace(installResult.Error))
            {
                errorMessage += $" Details: {installResult.Error}";
            }

            return new ToolStatus(
                false,
                null,
                errorMessage);
        }

        command = await ResolveCommandAsync(ct).ConfigureAwait(false);
        if (command is not null)
        {
            return new ToolStatus(
                true,
                string.IsNullOrWhiteSpace(command.Version) ? null : command.Version,
                $"{command.DisplayName} is available.");
        }

        return new ToolStatus(
            false,
            null,
            "dotnet-dotmemory was installed, but the executable could not be resolved. " +
            "Add the .NET tools directory to PATH or set DOTMEMORY_PATH explicitly.");
    }

    public virtual async Task<DotMemoryCommand?> ResolveCommandAsync(CancellationToken ct = default)
    {
        if (_cachedCommand is not null)
            return _cachedCommand;

        var configured = ResolveConfiguredPath();
        if (configured is not null)
        {
            _cachedCommand = configured;
            return configured;
        }

        var fromPath = await ResolveFromPathAsync(ct).ConfigureAwait(false);
        if (fromPath is not null)
        {
            _cachedCommand = fromPath;
            return fromPath;
        }

        var localTool = await ResolveLocalToolAsync(ct).ConfigureAwait(false);
        if (localTool is not null)
        {
            _cachedCommand = localTool;
            return localTool;
        }

        var globalTool = await ResolveGlobalToolAsync(ct).ConfigureAwait(false);
        _cachedCommand = globalTool;
        return globalTool;
    }

    public void InvalidateCache()
    {
        _cachedCommand = null;
    }

    private DotMemoryCommand? ResolveConfiguredPath()
    {
        foreach (var variableName in ConfiguredPathVariables)
        {
            var raw = Environment.GetEnvironmentVariable(variableName);
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var candidate = raw.Trim().Trim('"');
            if (LooksLikePath(candidate) && !File.Exists(candidate))
                continue;

            // Validate the configured path with --version to ensure it's actually dotMemory CLI
            var version = TryProbeSync(candidate);
            if (version is null)
                continue;

            return new DotMemoryCommand(
                candidate,
                "",
                $"{variableName} ({candidate})",
                version,
                DotMemoryCommandKind.ExplicitPath);
        }

        return null;
    }

    private string? TryProbeSync(string fileName)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = fileName,
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process is null)
                return null;

            process.WaitForExit();

            if (process.ExitCode != 0)
                return null;

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            var text = string.IsNullOrWhiteSpace(output) ? error : output;

            var firstLine = text
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();

            return firstLine ?? string.Empty;
        }
        catch
        {
            return null;
        }
    }

    private async Task<DotMemoryCommand?> ResolveFromPathAsync(CancellationToken ct)
    {
        foreach (var candidate in PathCandidates)
        {
            var version = await TryProbeAsync(candidate, ct).ConfigureAwait(false);
            if (version is not null)
            {
                return new DotMemoryCommand(
                    candidate,
                    "",
                    candidate,
                    version,
                    DotMemoryCommandKind.PathDiscovery);
            }
        }

        return null;
    }

    private async Task<DotMemoryCommand?> ResolveLocalToolAsync(CancellationToken ct)
    {
        var result = await TryRunAsync("dotnet", "tool list", ct).ConfigureAwait(false);
        if (result is null || result.ExitCode != 0 || !ContainsTool(result.Output))
            return null;

        return new DotMemoryCommand(
            "dotnet",
            "tool run dotnet-dotmemory --",
            "local tool manifest (dotnet tool run dotnet-dotmemory)",
            ParseVersion(result.Output),
            DotMemoryCommandKind.LocalTool);
    }

    private async Task<DotMemoryCommand?> ResolveGlobalToolAsync(CancellationToken ct)
    {
        var result = await TryRunAsync("dotnet", "tool list -g", ct).ConfigureAwait(false);
        if (result is null || result.ExitCode != 0 || !ContainsTool(result.Output))
            return null;

        var shimPath = GetGlobalToolShimPath();
        if (File.Exists(shimPath))
        {
            return new DotMemoryCommand(
                shimPath,
                "",
                $"global tool shim ({shimPath})",
                ParseVersion(result.Output),
                DotMemoryCommandKind.GlobalTool);
        }

        var probe = await TryProbeAsync("dotnet-dotmemory", ct).ConfigureAwait(false);
        if (probe is null)
            return null;

        return new DotMemoryCommand(
            "dotnet-dotmemory",
            "",
            "global tool (dotnet-dotmemory)",
            ParseVersion(result.Output),
            DotMemoryCommandKind.GlobalTool);
    }

    private static bool ContainsTool(string output) =>
        output.Contains("dotnet-dotmemory", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikePath(string candidate) =>
        Path.IsPathRooted(candidate)
        || candidate.Contains(Path.DirectorySeparatorChar)
        || candidate.Contains(Path.AltDirectorySeparatorChar);

    private static string GetGlobalToolShimPath()
    {
        var toolsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".dotnet",
            "tools");

        var executableName = OperatingSystem.IsWindows()
            ? "dotnet-dotmemory.exe"
            : "dotnet-dotmemory";

        return Path.Combine(toolsDir, executableName);
    }

    private async Task<string?> TryProbeAsync(string fileName, CancellationToken ct)
    {
        var result = await TryRunAsync(fileName, "--version", ct).ConfigureAwait(false);
        if (result is null)
            return null;

        // Require successful exit code to validate this is actually dotMemory
        if (result.ExitCode != 0)
            return null;

        var text = string.IsNullOrWhiteSpace(result.Output)
            ? result.Error
            : result.Output;

        var firstLine = text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        // Non-null means "process started", even if it doesn't print version.
        return firstLine ?? string.Empty;
    }

    private async Task<ProcessResult?> TryRunAsync(string fileName, string arguments, CancellationToken ct)
    {
        try
        {
            return await processRunner.RunAsync(fileName, arguments, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
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
