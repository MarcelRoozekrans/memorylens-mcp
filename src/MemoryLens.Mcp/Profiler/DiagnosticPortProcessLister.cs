#pragma warning disable MA0048 // File name must match type name - intentional companion types
using System.Diagnostics;
using System.Globalization;

namespace MemoryLens.Mcp.Profiler;

public record DotNetProcess(int Pid, string Name, string CommandLine);

public interface IDotNetProcessLister
{
    IReadOnlyList<DotNetProcess> ListProcesses();
}

/// <summary>
/// Enumerates running .NET processes from their diagnostic IPC endpoints.
/// <para>
/// Every .NET Core 3.0+ runtime opens a diagnostic server by default and advertises
/// it as a Unix domain socket under the temp directory, or as a named pipe on
/// Windows. Both encode the owning pid, so listing the endpoints lists the
/// profileable processes. This is the same discovery mechanism the dotnet-* CLI
/// diagnostic tools use.
/// </para>
/// <para>
/// The alternative — asking the profiler — is not available: dotMemory Console
/// exposes only get-snapshot, attach, start, start-net-core and recover. It has no
/// process-listing command, so this must not depend on dotMemory being installed.
/// </para>
/// </summary>
public class DiagnosticPortProcessLister : IDotNetProcessLister
{
    private const string UnixSocketPrefix = "dotnet-diagnostic-";
    private const string UnixSocketSuffix = "-socket";
    private const string WindowsPipePrefix = "dotnet-diagnostic-";

    private readonly string _unixEndpointDirectory;

    public DiagnosticPortProcessLister(string? unixEndpointDirectory = null)
    {
        // The runtime honours TMPDIR when placing the socket, which is exactly what
        // Path.GetTempPath() reads, so the default agrees with the runtime.
        _unixEndpointDirectory = unixEndpointDirectory ?? Path.GetTempPath();
    }

    public IReadOnlyList<DotNetProcess> ListProcesses()
    {
        var processes = new List<DotNetProcess>();

        foreach (var pid in EnumerateCandidatePids().Distinct())
        {
            if (Describe(pid) is { } process)
                processes.Add(process);
        }

        return processes;
    }

    private IEnumerable<int> EnumerateCandidatePids() =>
        OperatingSystem.IsWindows()
            ? EnumerateWindowsPipePids()
            : EnumerateUnixSocketPids();

    private IEnumerable<int> EnumerateUnixSocketPids()
    {
        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFiles(
                _unixEndpointDirectory, UnixSocketPrefix + "*" + UnixSocketSuffix);
        }
        catch (DirectoryNotFoundException) { yield break; }
        catch (UnauthorizedAccessException) { yield break; }

        foreach (var entry in entries)
        {
            if (TryParseUnixSocketPid(Path.GetFileName(entry), out var pid))
                yield return pid;
        }
    }

    private static IEnumerable<int> EnumerateWindowsPipePids()
    {
        IEnumerable<string> entries;
        try
        {
            // The pipe filesystem does not support globbing, so filter after listing.
            entries = Directory.GetFiles(@"\\.\pipe\");
        }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }

        var pids = new List<int>();
        foreach (var entry in entries)
        {
            if (TryParseWindowsPipePid(Path.GetFileName(entry), out var pid))
                pids.Add(pid);
        }

        return pids;
    }

    /// <summary>
    /// Parses the pid out of "dotnet-diagnostic-{pid}-{disambiguator}-socket".
    /// </summary>
    internal static bool TryParseUnixSocketPid(string fileName, out int pid)
    {
        pid = 0;

        if (!fileName.StartsWith(UnixSocketPrefix, StringComparison.Ordinal) ||
            !fileName.EndsWith(UnixSocketSuffix, StringComparison.Ordinal))
            return false;

        var middle = fileName[UnixSocketPrefix.Length..^UnixSocketSuffix.Length];

        // The disambiguator is a start-time token; the pid is everything before it.
        var separator = middle.IndexOf('-', StringComparison.Ordinal);
        var pidText = separator >= 0 ? middle[..separator] : middle;

        return int.TryParse(pidText, NumberStyles.None, CultureInfo.InvariantCulture, out pid)
               && pid > 0;
    }

    /// <summary>
    /// Parses the pid out of "dotnet-diagnostic-{pid}", the Windows pipe form. Unlike
    /// the socket name it carries no disambiguator.
    /// </summary>
    internal static bool TryParseWindowsPipePid(string pipeName, out int pid)
    {
        pid = 0;

        if (!pipeName.StartsWith(WindowsPipePrefix, StringComparison.Ordinal))
            return false;

        var pidText = pipeName[WindowsPipePrefix.Length..];

        return int.TryParse(pidText, NumberStyles.None, CultureInfo.InvariantCulture, out pid)
               && pid > 0;
    }

    private static DotNetProcess? Describe(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return new DotNetProcess(pid, process.ProcessName, SafeMainModulePath(process));
        }
        catch (ArgumentException)
        {
            // Stale endpoint: the process exited without cleaning its socket up.
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string SafeMainModulePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName ?? "";
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Reading another user's module list is commonly denied; the name alone is
            // still enough for ProcessFilter to make an exclusion decision.
            return "";
        }
    }
}
