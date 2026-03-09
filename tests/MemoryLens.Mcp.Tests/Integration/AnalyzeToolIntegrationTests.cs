using System.Text.Json;
using MemoryLens.Mcp.Analysis;
using MemoryLens.Mcp.Config;
using MemoryLens.Mcp.Tests.Profiler;
using MemoryLens.Mcp.Tools;
using Xunit;

namespace MemoryLens.Mcp.Tests.Integration;

/// <summary>
/// Integration tests that exercise the MCP tool layer end-to-end:
/// AnalyzeTool → AnalysisEngine → DotMemoryAnalyzer → Rules → JSON output
/// </summary>
public class AnalyzeToolIntegrationTests
{
    private const string LeakyAppReport = """
                  MT    Count    TotalSize Class Name
        00007ff8a1000010    30000     3600000 System.String
        00007ff8a1000020      100      250000 System.EventHandler`1[[MyApp.DataChanged]]
        00007ff8a1000030       80      400000 System.IO.StreamReader
        00007ff8a1000040       60      180000 MyApp.Handlers.RequestHandler+<>c__DisplayClass2_0
        Total    30240     4430000
        """;

    [Fact]
    public async Task Analyze_ReturnsJsonWithFindings()
    {
        var runner = new FakeProcessRunner(exitCode: 0, output: LeakyAppReport);
        var analyzer = new DotMemoryAnalyzer(runner);
        var engine = new AnalysisEngine(new MemoryLensConfig(), analyzer);
        var tool = new AnalyzeTool(engine);

        var json = await tool.analyze("snap-123", snapshotPath: "/tmp/snap.gcdump", ct: TestContext.Current.CancellationToken);

        // Should be valid JSON
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Should have findings array and count
        Assert.True(root.TryGetProperty("findings", out var findings));
        Assert.True(root.TryGetProperty("count", out var count));
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

        var runner = new FakeProcessRunner(exitCode: 0, output: beforeReport);
        runner.SetNextResult(exitCode: 0, output: afterReport);
        var analyzer = new DotMemoryAnalyzer(runner);
        var engine = new AnalysisEngine(new MemoryLensConfig(), analyzer);
        var tool = new AnalyzeTool(engine);

        var json = await tool.analyze(
            "cmp-456",
            beforePath: "/tmp/before.gcdump",
            afterPath: "/tmp/after.gcdump",
            ct: TestContext.Current.CancellationToken);

        var doc = JsonDocument.Parse(json);
        var count = doc.RootElement.GetProperty("count").GetInt32();
        Assert.True(count > 0);

        // Should detect EventHandler growth (10 → 80, 8x)
        Assert.Contains("EventHandler", json);
    }

    [Fact]
    public async Task Analyze_NoPath_ReturnsEmptyFindings()
    {
        // No snapshot path provided, analyzer can't parse anything
        var engine = new AnalysisEngine(new MemoryLensConfig());
        var tool = new AnalyzeTool(engine);

        var json = await tool.analyze("snap-empty", ct: TestContext.Current.CancellationToken);

        var doc = JsonDocument.Parse(json);
        Assert.Equal(0, doc.RootElement.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Analyze_WithConfig_RespectsDisabledRules()
    {
        var runner = new FakeProcessRunner(exitCode: 0, output: LeakyAppReport);
        var analyzer = new DotMemoryAnalyzer(runner);
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
        var engine = new AnalysisEngine(config, analyzer);
        var tool = new AnalyzeTool(engine);

        var json = await tool.analyze("snap-123", snapshotPath: "/tmp/snap.gcdump", ct: TestContext.Current.CancellationToken);

        var doc = JsonDocument.Parse(json);
        var findings = doc.RootElement.GetProperty("findings");
        foreach (var finding in findings.EnumerateArray())
        {
            var ruleId = finding.GetProperty("RuleId").GetString();
            Assert.DoesNotContain(ruleId, new[] { "ML001", "ML003", "ML006", "ML007", "ML008", "ML010" });
        }
    }
}
