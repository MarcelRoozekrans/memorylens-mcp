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
    // GcDumpReportParser stays around unwired (Task 5 deletes it); reused here purely as a
    // convenient text-to-SnapshotData fixture builder so these fixtures don't need to be
    // hand-written as TypeInfo lists.
    private const string LeakyAppReport = """
                  MT    Count    TotalSize Class Name
        00007ff8a1000010    30000     3600000 System.String
        00007ff8a1000020      100      250000 System.EventHandler`1[[MyApp.DataChanged]]
        00007ff8a1000030       80      400000 System.IO.StreamReader
        00007ff8a1000040       60      180000 MyApp.Handlers.RequestHandler+<>c__DisplayClass2_0
        Total    30240     4430000
        """;

    private static string NewRoot() =>
        Path.Combine(Path.GetTempPath(), "memorylens-analyze-tool-tests-" + Guid.NewGuid().ToString("N"));

    private static async Task<(SnapshotStore Store, string Id)> SaveReportAsync(
        string root, string report, CancellationToken ct)
    {
        var store = new SnapshotStore(root);
        var id = await store.SaveAsync(GcDumpReportParser.Parse(report), ct).ConfigureAwait(false);
        return (store, id);
    }

    [Fact]
    public async Task Analyze_ReturnsJsonWithFindings()
    {
        var root = NewRoot();
        try
        {
            var (store, id) = await SaveReportAsync(root, LeakyAppReport, TestContext.Current.CancellationToken);
            var engine = new AnalysisEngine(new MemoryLensConfig(), new SnapshotReader(store));
            var tool = new AnalyzeTool(engine);

            var json = await tool.analyze("snap-123", snapshotPath: id, ct: TestContext.Current.CancellationToken);

            // Should be valid JSON
            var doc = JsonDocument.Parse(json);
            var root2 = doc.RootElement;

            // Should have findings array and count
            Assert.True(root2.TryGetProperty("findings", out var findings));
            Assert.True(root2.TryGetProperty("count", out var count));
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
        var beforeReport = """
                      MT    Count    TotalSize Class Name
            00007ff8a1000010     5000      600000 System.String
            00007ff8a1000020       10       25000 System.EventHandler
            Total     5010      625000
            """;

        var afterReport = """
                      MT    Count    TotalSize Class Name
            00007ff8a1000010    25000     3000000 System.String
            00007ff8a1000020       80      200000 System.EventHandler
            Total    25080     3200000
            """;

        var root = NewRoot();
        try
        {
            var store = new SnapshotStore(root);
            var beforeId = await store.SaveAsync(GcDumpReportParser.Parse(beforeReport), TestContext.Current.CancellationToken);
            var afterId = await store.SaveAsync(GcDumpReportParser.Parse(afterReport), TestContext.Current.CancellationToken);
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

    [Fact]
    public async Task Analyze_NoPath_ReturnsEmptyFindings()
    {
        // No snapshot path provided, nothing to read
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
            var (store, id) = await SaveReportAsync(root, LeakyAppReport, TestContext.Current.CancellationToken);
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
