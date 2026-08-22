using System.Text.Json;
using MemoryLens.Mcp.Analysis;
using MemoryLens.Mcp.Config;
using MemoryLens.Mcp.Profiler;
using MemoryLens.Mcp.Tools;
using Xunit;

namespace MemoryLens.Mcp.Tests.Integration;

/// <summary>
/// Integration tests that exercise the MCP tool layer end-to-end:
/// AnalyzeTool → AnalysisEngine → SnapshotReader → Rules → JSON output
/// </summary>
public class AnalyzeToolIntegrationTests
{
    // Fixtures are hand-built SnapshotData rather than data parsed from an external
    // tool's output, so these tests exercise the pipeline directly with no parser
    // dependency.
    private static SnapshotData LeakyAppSnapshot() => new()
    {
        Types =
        [
            new TypeInfo { FullName = "System.String", InstanceCount = 30000, TotalBytes = 3600000 },
            new TypeInfo
            {
                FullName = "System.EventHandler`1[[MyApp.DataChanged]]",
                InstanceCount = 100,
                TotalBytes = 250000,
                ImplementsIDisposable = true,
            },
            new TypeInfo
            {
                FullName = "System.IO.StreamReader",
                InstanceCount = 80,
                TotalBytes = 400000,
                ImplementsIDisposable = true,
            },
            new TypeInfo
            {
                FullName = "MyApp.Handlers.RequestHandler+<>c__DisplayClass2_0",
                InstanceCount = 60,
                TotalBytes = 180000,
                ImplementsIDisposable = true,
            },
        ],
        Heap = new HeapInfo { TotalBytes = 4430000 },
    };

    private static string NewRoot() =>
        Path.Combine(Path.GetTempPath(), "memorylens-analyze-tool-tests-" + Guid.NewGuid().ToString("N"));

    private static async Task<(SnapshotStore Store, string Id)> SaveAsync(
        string root, SnapshotData data, CancellationToken ct)
    {
        var store = new SnapshotStore(root);
        var id = await store.SaveAsync(data, ct).ConfigureAwait(false);
        return (store, id);
    }

    [Fact]
    public async Task Analyze_ReturnsJsonWithFindings()
    {
        var root = NewRoot();
        try
        {
            var (store, id) = await SaveAsync(root, LeakyAppSnapshot(), TestContext.Current.CancellationToken);
            var engine = new AnalysisEngine(new MemoryLensConfig(), new SnapshotReader(store));
            var tool = new AnalyzeTool(engine);

            var json = await tool.analyze("snap-123", snapshotPath: id, ct: TestContext.Current.CancellationToken);

            // Should be valid JSON
            var doc = JsonDocument.Parse(json);
            var resultRoot = doc.RootElement;

            // Should have findings array and count
            Assert.True(resultRoot.TryGetProperty("findings", out var findings));
            Assert.True(resultRoot.TryGetProperty("count", out var count));
            Assert.True(count.GetInt32() > 0);
            Assert.True(findings.GetArrayLength() > 0);

            // Each finding should have the expected shape (PascalCase from record properties)
            foreach (var finding in findings.EnumerateArray())
            {
                Assert.True(finding.TryGetProperty("RuleId", out _));
                Assert.True(finding.TryGetProperty("Severity", out _));
                Assert.True(finding.TryGetProperty("Category", out _));
                Assert.True(finding.TryGetProperty("Title", out _));
                Assert.True(finding.TryGetProperty("Description", out _));
                Assert.True(finding.TryGetProperty("Evidence", out _));
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Analyze_ComparisonMode_ReturnsGrowthFindings()
    {
        var before = new SnapshotData
        {
            Types =
            [
                new TypeInfo { FullName = "System.String", InstanceCount = 5000, TotalBytes = 600000 },
                new TypeInfo
                {
                    FullName = "System.EventHandler",
                    InstanceCount = 10,
                    TotalBytes = 25000,
                    ImplementsIDisposable = true,
                },
            ],
            Heap = new HeapInfo { TotalBytes = 625000 },
        };

        var after = new SnapshotData
        {
            Types =
            [
                new TypeInfo { FullName = "System.String", InstanceCount = 25000, TotalBytes = 3000000 },
                new TypeInfo
                {
                    FullName = "System.EventHandler",
                    InstanceCount = 80,
                    TotalBytes = 200000,
                    ImplementsIDisposable = true,
                },
            ],
            Heap = new HeapInfo { TotalBytes = 3200000 },
        };

        var root = NewRoot();
        try
        {
            var store = new SnapshotStore(root);
            var beforeId = await store.SaveAsync(before, TestContext.Current.CancellationToken);
            var afterId = await store.SaveAsync(after, TestContext.Current.CancellationToken);
            var engine = new AnalysisEngine(new MemoryLensConfig(), new SnapshotReader(store));
            var tool = new AnalyzeTool(engine);

            var json = await tool.analyze(
                "cmp-456",
                beforePath: beforeId,
                afterPath: afterId,
                ct: TestContext.Current.CancellationToken);

            var doc = JsonDocument.Parse(json);
            var count = doc.RootElement.GetProperty("count").GetInt32();
            Assert.True(count > 0);

            // Should detect EventHandler growth (10 → 80, 8x)
            Assert.Contains("EventHandler", json);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    /// <summary>
    /// The documented workflow, verbatim: <c>snapshot</c> hands back an id, and
    /// <c>analyze</c> is called with that id and nothing else — no snapshotPath.
    /// Every other test in this file passes snapshotPath explicitly or builds the
    /// context by hand, which is exactly why a bare id silently producing
    /// <c>{"count":0}</c> on a leaking heap went unnoticed.
    /// </summary>
    [Fact]
    public async Task Analyze_WithBareSnapshotIdAndNoPath_ReturnsFindings()
    {
        var root = NewRoot();
        try
        {
            var (store, id) = await SaveAsync(root, LeakyAppSnapshot(), TestContext.Current.CancellationToken);
            var engine = new AnalysisEngine(new MemoryLensConfig(), new SnapshotReader(store));
            var tool = new AnalyzeTool(engine);

            // Deliberately no snapshotPath. This is what SKILL.md Step 4 tells the
            // agent to do and what SnapshotTool's own description promises.
            var json = await tool.analyze(id, ct: TestContext.Current.CancellationToken);

            var doc = JsonDocument.Parse(json);
            var count = doc.RootElement.GetProperty("count").GetInt32();
            var findings = doc.RootElement.GetProperty("findings");

            Assert.True(count > 0,
                $"analyze('{id}') with no snapshotPath reported {count} findings on a snapshot " +
                $"that is full of leaks. A bare snapshot id must resolve. Raw: {json}");
            Assert.True(findings.GetArrayLength() > 0);
            Assert.Equal(count, findings.GetArrayLength());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    /// <summary>
    /// An id that resolves to nothing must fail loudly. Returning zero findings for a
    /// snapshot that was never found is the "plausible but wrong" answer this pipeline
    /// exists to eliminate.
    /// </summary>
    [Fact]
    public async Task Analyze_WithUnknownSnapshotId_ThrowsRatherThanReportingNoFindings()
    {
        var root = NewRoot();
        try
        {
            Directory.CreateDirectory(root);
            var engine = new AnalysisEngine(new MemoryLensConfig(), new SnapshotReader(new SnapshotStore(root)));
            var tool = new AnalyzeTool(engine);

            await Assert.ThrowsAsync<FileNotFoundException>(
                () => tool.analyze("no-such-snapshot", ct: TestContext.Current.CancellationToken));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Analyze_WithNoSnapshotReader_ReturnsEmptyFindings()
    {
        // The engine has no reader wired up at all, so there is nothing to resolve the
        // id against and no data to enrich the context with. (This is not the "no path
        // given" case — a bare id resolves fine when a reader is present; see
        // Analyze_WithBareSnapshotIdAndNoPath_ReturnsFindings.)
        var engine = new AnalysisEngine(new MemoryLensConfig());
        var tool = new AnalyzeTool(engine);

        var json = await tool.analyze("snap-empty", ct: TestContext.Current.CancellationToken);

        var doc = JsonDocument.Parse(json);
        Assert.Equal(0, doc.RootElement.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Analyze_WithConfig_RespectsDisabledRules()
    {
        var root = NewRoot();
        try
        {
            var (store, id) = await SaveAsync(root, LeakyAppSnapshot(), TestContext.Current.CancellationToken);
            var config = ConfigLoader.Parse("""
                {
                    "rules": {
                        "ML001": { "enabled": false },
                        "ML003": { "enabled": false },
                        "ML006": { "enabled": false },
                        "ML007": { "enabled": false },
                        "ML008": { "enabled": false },
                        "ML010": { "enabled": false }
                    }
                }
                """);
            var engine = new AnalysisEngine(config, new SnapshotReader(store));
            var tool = new AnalyzeTool(engine);

            var json = await tool.analyze("snap-123", snapshotPath: id, ct: TestContext.Current.CancellationToken);

            var doc = JsonDocument.Parse(json);
            var findings = doc.RootElement.GetProperty("findings");
            foreach (var finding in findings.EnumerateArray())
            {
                var ruleId = finding.GetProperty("RuleId").GetString();
                Assert.DoesNotContain(ruleId, new[] { "ML001", "ML003", "ML006", "ML007", "ML008", "ML010" });
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
