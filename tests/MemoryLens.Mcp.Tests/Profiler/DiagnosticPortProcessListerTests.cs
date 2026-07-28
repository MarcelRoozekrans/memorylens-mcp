using System.Diagnostics;
using MemoryLens.Mcp.Profiler;
using Xunit;

namespace MemoryLens.Mcp.Tests.Profiler;

public class DiagnosticPortProcessListerTests
{
    [Theory]
    // Real runtime form: pid, then a start-time disambiguator.
    [InlineData("dotnet-diagnostic-1234-9876543-socket", true, 1234)]
    [InlineData("dotnet-diagnostic-7-1-socket", true, 7)]
    // Tolerate the disambiguator being absent.
    [InlineData("dotnet-diagnostic-4242-socket", true, 4242)]
    // Not endpoints.
    [InlineData("dotnet-diagnostic-notapid-1234-socket", false, 0)]
    [InlineData("dotnet-diagnostic-1234-socket.lock", false, 0)]
    [InlineData("clr-debug-pipe-1234-socket", false, 0)]
    [InlineData("dotnet-diagnostic-0-1-socket", false, 0)]
    [InlineData("dotnet-diagnostic--1-1-socket", false, 0)]
    [InlineData("", false, 0)]
    public void TryParseUnixSocketPid_AcceptsOnlyRuntimeEndpoints(
        string fileName, bool expected, int expectedPid)
    {
        var parsed = DiagnosticPortProcessLister.TryParseUnixSocketPid(fileName, out var pid);

        Assert.Equal(expected, parsed);
        Assert.Equal(expectedPid, pid);
    }

    [Theory]
    [InlineData("dotnet-diagnostic-1234", true, 1234)]
    [InlineData("dotnet-diagnostic-1", true, 1)]
    // The pipe form carries no disambiguator, so a trailing segment is not a pid.
    [InlineData("dotnet-diagnostic-1234-5678", false, 0)]
    [InlineData("dotnet-diagnostic-", false, 0)]
    [InlineData("dotnet-diagnostic-0", false, 0)]
    [InlineData("some-other-pipe", false, 0)]
    public void TryParseWindowsPipePid_AcceptsOnlyRuntimeEndpoints(
        string pipeName, bool expected, int expectedPid)
    {
        var parsed = DiagnosticPortProcessLister.TryParseWindowsPipePid(pipeName, out var pid);

        Assert.Equal(expected, parsed);
        Assert.Equal(expectedPid, pid);
    }

    [Fact]
    public void ListProcesses_MissingEndpointDirectory_ReturnsEmpty()
    {
        // Windows discovery reads the pipe namespace and ignores this directory, so
        // there is no "missing directory" state to exercise there.
        if (OperatingSystem.IsWindows())
            return;

        var lister = new DiagnosticPortProcessLister(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        Assert.Empty(lister.ListProcesses());
    }

    /// <summary>
    /// A socket left behind by an exited process must not be reported — the pid no
    /// longer resolves, and callers would try to profile something that is gone.
    /// </summary>
    [Fact]
    public void ListProcesses_StaleEndpoint_IsSkipped()
    {
        if (OperatingSystem.IsWindows())
            return; // Windows discovery reads the pipe namespace, not this directory.

        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        // A pid that cannot be live: allocated far above the practical maximum.
        File.WriteAllText(Path.Combine(dir, "dotnet-diagnostic-2000000000-1-socket"), "");

        try
        {
            var lister = new DiagnosticPortProcessLister(dir);
            Assert.Empty(lister.ListProcesses());
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>
    /// End-to-end against a real endpoint: this test process is a .NET process, so its
    /// own pid must be discoverable through whichever mechanism the platform uses.
    /// </summary>
    [Fact]
    public void ListProcesses_FindsTheCurrentProcess()
    {
        var lister = new DiagnosticPortProcessLister();

        var pids = lister.ListProcesses().Select(p => p.Pid).ToList();

        Assert.Contains(Environment.ProcessId, pids);
    }

    [Fact]
    public void ListProcesses_ReportsTheProcessName()
    {
        var lister = new DiagnosticPortProcessLister();

        var self = lister.ListProcesses().SingleOrDefault(p => p.Pid == Environment.ProcessId);

        Assert.NotNull(self);
        using var current = Process.GetCurrentProcess();
        Assert.Equal(current.ProcessName, self.Name);
    }
}
