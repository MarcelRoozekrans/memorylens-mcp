using System.Text.Json;
using MemoryLens.Mcp.Analysis;
using MemoryLens.Mcp.Config;
using MemoryLens.Mcp.Rules;
using MemoryLens.Mcp.Tests.Profiler;
using Xunit;

namespace MemoryLens.Mcp.Tests.Integration;

/// <summary>
/// Integration tests that exercise the full analysis pipeline:
/// FakeProcessRunner → DotMemoryAnalyzer → GcDumpReportParser → AnalysisEngine → Rules → Findings
/// </summary>
public class AnalysisPipelineTests
{
    /// <summary>
    /// Simulates a realistic gcdump report from a .NET web app with memory issues.
    /// </summary>
    private const string RealisticGcDumpReport = """
                  MT    Count    TotalSize Class Name
        00007ff8a1000010    55000     6600000 System.String
        00007ff8a1000020      200      500000 System.EventHandler
        00007ff8a1000030      150     2000000 System.IO.FileStream
        00007ff8a1000040     5000     3000000 System.Object[]
        00007ff8a1000050      120      300000 System.Collections.Generic.List`1[[System.String]]
        00007ff8a1000060       80      250000 MyApp.Services.UserService+<>c__DisplayClass5_0
        00007ff8a1000070       10     1200000 System.Byte[]
        00007ff8a1000080       15      100000 System.Int32
        00007ff8a1000090        5         500 MyApp.NativeWrapper
        00007ff8a10000a0       30       50000 System.Threading.Timer
        Total    60610    13900500
        """;

    [Fact]
    public async Task FullPipeline_SingleSnapshot_DetectsMultipleIssues()
    {
        // Arrange: wire up the full pipeline with fake process runner
        var runner = new FakeProcessRunner(exitCode: 0, output: RealisticGcDumpReport);
        var analyzer = new DotMemoryAnalyzer(runner);
        var config = new MemoryLensConfig();
        var engine = new AnalysisEngine(config, analyzer);

        var context = new SnapshotAnalysisContext(
            "snap-abc123", "/tmp/snapshot.gcdump", null, null, false, null);

        // Act
        var findings = await engine.AnalyzeAsync(context, TestContext.Current.CancellationToken);

        // Assert: multiple rules should fire
        Assert.NotEmpty(findings);

        // ML001: 200 EventHandler instances > threshold of 50
        Assert.Contains(findings, f => f.RuleId == "ML001");

        // ML003: FileStream is disposable with 150 instances
        Assert.Contains(findings, f => f.RuleId == "ML003");

        // ML006: System.String has 55000 instances
        Assert.Contains(findings, f => f.RuleId == "ML006");

        // ML008: System.Object[] has 5000 instances > 1000 threshold
        Assert.Contains(findings, f => f.RuleId == "ML008");

        // ML010: 55000 strings at 6.6MB, avg ~120 bytes → interning opportunity
        Assert.Contains(findings, f => f.RuleId == "ML010");

        // ML007: Closure type with 80 instances > 50 threshold, 250KB > 100KB threshold
        Assert.Contains(findings, f => f.RuleId == "ML007");
    }

    [Fact]
    public async Task FullPipeline_SingleSnapshot_FindingsHaveCorrectStructure()
    {
        var runner = new FakeProcessRunner(exitCode: 0, output: RealisticGcDumpReport);
        var analyzer = new DotMemoryAnalyzer(runner);
        var engine = new AnalysisEngine(new MemoryLensConfig(), analyzer);

        var context = new SnapshotAnalysisContext(
            "snap-abc123", "/tmp/snapshot.gcdump", null, null, false, null);

        var findings = await engine.AnalyzeAsync(context, TestContext.Current.CancellationToken);

        // Every finding should have required fields populated
        foreach (var finding in findings)
        {
            Assert.False(string.IsNullOrEmpty(finding.RuleId));
            Assert.False(string.IsNullOrEmpty(finding.Severity));
            Assert.False(string.IsNullOrEmpty(finding.Category));
            Assert.False(string.IsNullOrEmpty(finding.Title));
            Assert.False(string.IsNullOrEmpty(finding.Description));
            Assert.NotNull(finding.Evidence);
            Assert.False(string.IsNullOrEmpty(finding.Evidence.Type));
        }
    }

    [Fact]
    public async Task FullPipeline_Comparison_DetectsGrowth()
    {
        var beforeReport = """
                      MT    Count    TotalSize Class Name
            00007ff8a1000010    10000     1200000 System.String
            00007ff8a1000020       20       50000 System.EventHandler
            00007ff8a1000030       50      100000 System.Collections.Generic.Dictionary`2[[System.String,System.Object]]
            Total    10070     1350000
            """;

        var afterReport = """
                      MT    Count    TotalSize Class Name
            00007ff8a1000010    35000     4200000 System.String
            00007ff8a1000020      150      375000 System.EventHandler
            00007ff8a1000030      120      500000 System.Collections.Generic.Dictionary`2[[System.String,System.Object]]
            Total    35270     5075000
            """;

        var runner = new FakeProcessRunner(exitCode: 0, output: beforeReport);
        runner.SetNextResult(exitCode: 0, output: afterReport);
        var analyzer = new DotMemoryAnalyzer(runner);
        var engine = new AnalysisEngine(new MemoryLensConfig(), analyzer);

        var context = new SnapshotAnalysisContext(
            "cmp-abc123", null, "/tmp/before.gcdump", "/tmp/after.gcdump", true, null);

        var findings = await engine.AnalyzeAsync(context, TestContext.Current.CancellationToken);

        Assert.NotEmpty(findings);

        // ML001: EventHandler grew from 20 to 150 (7.5x)
        Assert.Contains(findings, f => f.RuleId == "ML001");

        // ML002: Dictionary grew from 50 to 120 (2.4x > 1.5x threshold)
        Assert.Contains(findings, f => f.RuleId == "ML002");

        // ML006: String count jumped by 25000 in comparison mode
        Assert.Contains(findings, f => f.RuleId == "ML006");
    }

    [Fact]
    public async Task FullPipeline_DisabledRules_AreSkipped()
    {
        var runner = new FakeProcessRunner(exitCode: 0, output: RealisticGcDumpReport);
        var analyzer = new DotMemoryAnalyzer(runner);
        var config = new MemoryLensConfig
        {
            Rules = new Dictionary<string, RuleOverride>
            {
                ["ML001"] = new() { Enabled = false },
                ["ML006"] = new() { Enabled = false },
                ["ML010"] = new() { Enabled = false },
            }
        };
        var engine = new AnalysisEngine(config, analyzer);

        var context = new SnapshotAnalysisContext(
            "snap-abc123", "/tmp/snapshot.gcdump", null, null, false, null);

        var findings = await engine.AnalyzeAsync(context, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(findings, f => f.RuleId == "ML001");
        Assert.DoesNotContain(findings, f => f.RuleId == "ML006");
        Assert.DoesNotContain(findings, f => f.RuleId == "ML010");
    }

    [Fact]
    public async Task FullPipeline_SeverityOverride_IsApplied()
    {
        var runner = new FakeProcessRunner(exitCode: 0, output: RealisticGcDumpReport);
        var analyzer = new DotMemoryAnalyzer(runner);
        var config = new MemoryLensConfig
        {
            Rules = new Dictionary<string, RuleOverride>
            {
                ["ML001"] = new() { Severity = "low" },
            }
        };
        var engine = new AnalysisEngine(config, analyzer);

        var context = new SnapshotAnalysisContext(
            "snap-abc123", "/tmp/snapshot.gcdump", null, null, false, null);

        var findings = await engine.AnalyzeAsync(context, TestContext.Current.CancellationToken);

        var ml001 = findings.Where(f => f.RuleId == "ML001").ToList();
        Assert.NotEmpty(ml001);
        Assert.All(ml001, f => Assert.Equal("low", f.Severity));
    }

    [Fact]
    public async Task FullPipeline_AnalyzerFailure_ReturnsNoFindings()
    {
        // gcdump tool fails — analyzer returns empty data, no rules fire
        var runner = new FakeProcessRunner(exitCode: 1, output: "Tool not found");
        var analyzer = new DotMemoryAnalyzer(runner);
        var engine = new AnalysisEngine(new MemoryLensConfig(), analyzer);

        var context = new SnapshotAnalysisContext(
            "snap-abc123", "/tmp/snapshot.gcdump", null, null, false, null);

        var findings = await engine.AnalyzeAsync(context, TestContext.Current.CancellationToken);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task FullPipeline_CleanApp_NoFindings()
    {
        // Small, healthy app — nothing should trigger
        var cleanReport = """
                      MT    Count    TotalSize Class Name
            00007ff8a1000010      100       10000 System.String
            00007ff8a1000020        5         500 System.Object[]
            00007ff8a1000030        2         200 System.Int32
            Total      107       10700
            """;

        var runner = new FakeProcessRunner(exitCode: 0, output: cleanReport);
        var analyzer = new DotMemoryAnalyzer(runner);
        var engine = new AnalysisEngine(new MemoryLensConfig(), analyzer);

        var context = new SnapshotAnalysisContext(
            "snap-clean", "/tmp/clean.gcdump", null, null, false, null);

        var findings = await engine.AnalyzeAsync(context, TestContext.Current.CancellationToken);

        Assert.Empty(findings);
    }
}
