using System.Text.Json;
using MemoryLens.Mcp.Analysis;
using MemoryLens.Mcp.Config;
using MemoryLens.Mcp.Profiler;
using MemoryLens.Mcp.Tests.Profiler;
using MemoryLens.Mcp.Tools;
using Xunit;

namespace MemoryLens.Mcp.Tests.Integration;

/// <summary>
/// Integration tests for other MCP tools (ensure_dotmemory, list_processes, get_rules).
/// </summary>
public class ToolIntegrationTests
{
    [Fact]
    public async Task EnsureDotMemory_ReturnsInstalledMessage()
    {
        var runner = new FakeProcessRunner(
            exitCode: 0,
            output: "dotnet-dotmemory  2024.3.5  dotnet-dotmemory");
        var manager = new DotMemoryToolManager(runner);
        var tool = new EnsureDotMemoryTool(manager);

        var result = await tool.ensure_dotmemory(TestContext.Current.CancellationToken);

        Assert.Contains("2024.3.5", result);
    }

    [Fact]
    public async Task ListProcesses_FiltersExcludedProcesses()
    {
        var processOutput = """
            1234 MyApp.Web
            5678 devenv
            9012 dotnet-dotmemory
            3456 MyApp.Worker
            """;

        var runner = new FakeProcessRunner(exitCode: 0, output: processOutput);
        var filter = new ProcessFilter();
        var tool = new ListProcessesTool(runner, filter);

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
        var processOutput = """
            1234 MyApp.Web
            3456 MyApp.Worker
            7890 OtherApp.Service
            """;

        var runner = new FakeProcessRunner(exitCode: 0, output: processOutput);
        var filter = new ProcessFilter();
        var tool = new ListProcessesTool(runner, filter);

        var json = await tool.list_processes(filter: "MyApp", ct: TestContext.Current.CancellationToken);
        var processes = JsonDocument.Parse(json).RootElement;

        Assert.Equal(2, processes.GetArrayLength());
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
        var runner = new FakeProcessRunner(exitCode: 0, output: "Snapshot taken");
        var filter = new ProcessFilter();
        var manager = new SnapshotManager(runner, filter);
        var tool = new SnapshotTool(manager);

        var json = await tool.snapshot(pid: 1234, ct: TestContext.Current.CancellationToken);
        var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.TryGetProperty("Success", out var success));
        Assert.True(success.GetBoolean());
        Assert.True(doc.RootElement.TryGetProperty("SnapshotId", out _));
    }

    [Fact]
    public async Task Snapshot_ExcludedProcess_ReturnsError()
    {
        var runner = new FakeProcessRunner(exitCode: 0, output: "");
        var filter = new ProcessFilter();
        var manager = new SnapshotManager(runner, filter);
        var tool = new SnapshotTool(manager);

        var json = await tool.snapshot(processName: "devenv", ct: TestContext.Current.CancellationToken);
        var doc = JsonDocument.Parse(json);

        Assert.False(doc.RootElement.GetProperty("Success").GetBoolean());
        Assert.Contains("excluded", doc.RootElement.GetProperty("Error").GetString());
    }

    [Fact]
    public async Task CompareSnapshots_ReturnsComparisonResult()
    {
        var runner = new FakeProcessRunner(exitCode: 0, output: "Before snapshot taken");
        runner.SetNextResult(exitCode: 0, output: "After snapshot taken");
        var filter = new ProcessFilter();
        var manager = new SnapshotManager(runner, filter);
        var tool = new CompareSnapshotsTool(manager);

        var json = await tool.compare_snapshots(pid: 1234, delaySeconds: 0, ct: TestContext.Current.CancellationToken);
        var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.GetProperty("Success").GetBoolean());
        Assert.Equal(2, doc.RootElement.GetProperty("SnapshotCount").GetInt32());
    }
}
