using System.Text.Json;
using MemoryLens.Mcp.Analysis;
using MemoryLens.Mcp.Config;
using MemoryLens.Mcp.Profiler;
using MemoryLens.Mcp.Tests.Profiler;
using MemoryLens.Mcp.Tools;
using Xunit;

namespace MemoryLens.Mcp.Tests.Integration;

/// <summary>
/// Integration tests for other MCP tools (list_processes, get_rules).
/// </summary>
public class ToolIntegrationTests
{
    [Fact]
    public async Task ListProcesses_FiltersExcludedProcesses()
    {
        var lister = new FakeDotNetProcessLister(
            new DotNetProcess(1234, "MyApp.Web", ""),
            new DotNetProcess(5678, "devenv", ""),
            new DotNetProcess(9012, "dotnet-dotmemory", ""),
            new DotNetProcess(3456, "MyApp.Worker", ""));
        var tool = new ListProcessesTool(lister, new ProcessFilter());

        var json = await tool.list_processes(ct: TestContext.Current.CancellationToken);
        var processes = JsonDocument.Parse(json).RootElement;

        var names = new List<string>();
        foreach (var proc in processes.EnumerateArray())
            names.Add(proc.GetProperty("Name").GetString()!);

        Assert.Contains("MyApp.Web", names);
        Assert.Contains("MyApp.Worker", names);
        Assert.DoesNotContain("devenv", names);
        Assert.DoesNotContain("dotnet-dotmemory", names);
    }

    [Fact]
    public async Task ListProcesses_WithFilter_NarrowsResults()
    {
        var lister = new FakeDotNetProcessLister(
            new DotNetProcess(1234, "MyApp.Web", ""),
            new DotNetProcess(3456, "MyApp.Worker", ""),
            new DotNetProcess(7890, "OtherApp.Service", ""));
        var tool = new ListProcessesTool(lister, new ProcessFilter());

        var json = await tool.list_processes(filter: "MyApp", ct: TestContext.Current.CancellationToken);
        var processes = JsonDocument.Parse(json).RootElement;

        Assert.Equal(2, processes.GetArrayLength());
    }

    /// <summary>
    /// Guards the defect this replaced: the tool shelled out to
    /// `dotnet dotmemory list-processes`. dotMemory Console has no process-listing
    /// command at all, so the tool could never work — and it also made an unrelated
    /// profiler install a prerequisite for merely listing processes.
    /// </summary>
    [Fact]
    public async Task ListProcesses_NeedsNoProfilerInstalled()
    {
        var lister = new FakeDotNetProcessLister(new DotNetProcess(1234, "MyApp.Web", ""));

        // No IProcessRunner and no DotMemoryToolManager: the tool cannot reach a CLI
        // even if one were installed.
        var tool = new ListProcessesTool(lister, new ProcessFilter());

        var json = await tool.list_processes(ct: TestContext.Current.CancellationToken);

        Assert.Equal(1, JsonDocument.Parse(json).RootElement.GetArrayLength());
    }

    [Fact]
    public async Task ListProcesses_NoDotNetProcesses_ReturnsEmptyArray()
    {
        var tool = new ListProcessesTool(new FakeDotNetProcessLister(), new ProcessFilter());

        var json = await tool.list_processes(ct: TestContext.Current.CancellationToken);

        Assert.Equal(0, JsonDocument.Parse(json).RootElement.GetArrayLength());
    }

    /// <summary>
    /// ProcessFilter excludes the server by the name "MemoryLens.Mcp", which does not
    /// match when it runs as `dotnet MemoryLens.Mcp.dll` — the process name is then
    /// just "dotnet". Excluding by pid is what actually holds.
    /// </summary>
    [Fact]
    public async Task ListProcesses_ExcludesItself_EvenWhenRunningAsPlainDotnet()
    {
        var lister = new FakeDotNetProcessLister(
            new DotNetProcess(Environment.ProcessId, "dotnet", "/usr/share/dotnet/dotnet"),
            new DotNetProcess(4321, "MyApp.Web", "/app/MyApp.Web"));
        var tool = new ListProcessesTool(lister, new ProcessFilter());

        var json = await tool.list_processes(ct: TestContext.Current.CancellationToken);
        var processes = JsonDocument.Parse(json).RootElement;

        var pids = processes.EnumerateArray().Select(p => p.GetProperty("Pid").GetInt32()).ToList();
        Assert.DoesNotContain(Environment.ProcessId, pids);
        Assert.Contains(4321, pids);
    }

    [Fact]
    public void GetRules_ReturnsAll10Rules()
    {
        var engine = new AnalysisEngine(new MemoryLensConfig());
        var tool = new GetRulesTool(engine);

        var json = tool.get_rules();
        var doc = JsonDocument.Parse(json);

        Assert.Equal(10, doc.RootElement.GetProperty("count").GetInt32());

        var rules = doc.RootElement.GetProperty("rules");
        Assert.Equal(10, rules.GetArrayLength());

        // Verify each rule has expected shape
        foreach (var rule in rules.EnumerateArray())
        {
            Assert.True(rule.TryGetProperty("Id", out _));
            Assert.True(rule.TryGetProperty("Title", out _));
            Assert.True(rule.TryGetProperty("Severity", out _));
            Assert.True(rule.TryGetProperty("Category", out _));
        }
    }

    [Fact]
    public void GetRules_WithDisabledRules_ReturnsFiltered()
    {
        var config = new MemoryLensConfig
        {
            Rules = new Dictionary<string, RuleOverride>
            {
                ["ML001"] = new() { Enabled = false },
                ["ML010"] = new() { Enabled = false },
            }
        };
        var engine = new AnalysisEngine(config);
        var tool = new GetRulesTool(engine);

        var json = tool.get_rules();
        var doc = JsonDocument.Parse(json);

        Assert.Equal(8, doc.RootElement.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Snapshot_ReturnsJsonResult()
    {
        var root = NewSnapshotRoot();
        try
        {
            var collector = new FakeHeapCollector();
            var store = new SnapshotStore(root);
            var filter = new ProcessFilter();
            var tool = new SnapshotTool(collector, store, filter);

            var json = await tool.snapshot(pid: 1234, ct: TestContext.Current.CancellationToken);
            var doc = JsonDocument.Parse(json);

            Assert.True(doc.RootElement.TryGetProperty("Success", out var success));
            Assert.True(success.GetBoolean());
            Assert.True(doc.RootElement.TryGetProperty("SnapshotId", out _));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Snapshot_ExcludedProcess_ReturnsError()
    {
        var root = NewSnapshotRoot();
        try
        {
            var collector = new FakeHeapCollector();
            var store = new SnapshotStore(root);
            var filter = new ProcessFilter();
            var tool = new SnapshotTool(collector, store, filter);

            var json = await tool.snapshot(pid: 1234, processName: "devenv", ct: TestContext.Current.CancellationToken);
            var doc = JsonDocument.Parse(json);

            Assert.False(doc.RootElement.GetProperty("Success").GetBoolean());
            Assert.Contains("excluded", doc.RootElement.GetProperty("Error").GetString());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Snapshot_MissingPid_ReturnsError()
    {
        var root = NewSnapshotRoot();
        try
        {
            var collector = new FakeHeapCollector();
            var store = new SnapshotStore(root);
            var filter = new ProcessFilter();
            var tool = new SnapshotTool(collector, store, filter);

            var json = await tool.snapshot(ct: TestContext.Current.CancellationToken);
            var doc = JsonDocument.Parse(json);

            Assert.False(doc.RootElement.GetProperty("Success").GetBoolean());
            Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("Error").GetString()));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    /// <summary>
    /// The bug this rewrite fixes reported "no memory issues found" on a leaking
    /// heap -- a collection failure must come back as a legible error, not be
    /// swallowed or left to propagate as an unhandled exception.
    /// </summary>
    [Fact]
    public async Task Snapshot_CollectionFails_ReturnsError()
    {
        var root = NewSnapshotRoot();
        try
        {
            var collector = new FakeHeapCollector(failureMessage: "process exited before collection completed");
            var store = new SnapshotStore(root);
            var filter = new ProcessFilter();
            var tool = new SnapshotTool(collector, store, filter);

            var json = await tool.snapshot(pid: 1234, ct: TestContext.Current.CancellationToken);
            var doc = JsonDocument.Parse(json);

            Assert.False(doc.RootElement.GetProperty("Success").GetBoolean());
            Assert.Equal(
                "process exited before collection completed",
                doc.RootElement.GetProperty("Error").GetString());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CompareSnapshots_ReturnsComparisonResult()
    {
        var root = NewSnapshotRoot();
        try
        {
            var collector = new FakeHeapCollector();
            var store = new SnapshotStore(root);
            var tool = new CompareSnapshotsTool(collector, store);

            var json = await tool.compare_snapshots(pid: 1234, delaySeconds: 0, ct: TestContext.Current.CancellationToken);
            var doc = JsonDocument.Parse(json);

            Assert.True(doc.RootElement.GetProperty("Success").GetBoolean());
            Assert.Equal(2, doc.RootElement.GetProperty("SnapshotCount").GetInt32());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CompareSnapshots_MissingPid_ReturnsError()
    {
        var root = NewSnapshotRoot();
        try
        {
            var collector = new FakeHeapCollector();
            var store = new SnapshotStore(root);
            var tool = new CompareSnapshotsTool(collector, store);

            var json = await tool.compare_snapshots(ct: TestContext.Current.CancellationToken);
            var doc = JsonDocument.Parse(json);

            Assert.False(doc.RootElement.GetProperty("Success").GetBoolean());
            Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("Error").GetString()));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    /// <summary>
    /// Same defect class as Snapshot_CollectionFails_ReturnsError: a collection
    /// failure during compare must come back as a legible error, not propagate.
    /// </summary>
    [Fact]
    public async Task CompareSnapshots_CollectionFails_ReturnsError()
    {
        var root = NewSnapshotRoot();
        try
        {
            var collector = new FakeHeapCollector(failureMessage: "diagnostics port unavailable");
            var store = new SnapshotStore(root);
            var tool = new CompareSnapshotsTool(collector, store);

            var json = await tool.compare_snapshots(pid: 1234, delaySeconds: 0, ct: TestContext.Current.CancellationToken);
            var doc = JsonDocument.Parse(json);

            Assert.False(doc.RootElement.GetProperty("Success").GetBoolean());
            Assert.Equal(
                "diagnostics port unavailable",
                doc.RootElement.GetProperty("Error").GetString());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static string NewSnapshotRoot() =>
        Path.Combine(Path.GetTempPath(), "memorylens-tool-tests-" + Guid.NewGuid().ToString("N"));
}
