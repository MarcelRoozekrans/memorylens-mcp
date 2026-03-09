using MemoryLens.Mcp.Analysis;
using MemoryLens.Mcp.Config;
using MemoryLens.Mcp.Rules;
using Xunit;

namespace MemoryLens.Mcp.Tests.Analysis;

public class AnalysisEngineTests
{
    [Fact]
    public async Task Analyze_RunsAllEnabledRules()
    {
        var config = new MemoryLensConfig();
        var engine = new AnalysisEngine(config);
        // No data => no findings from any rule
        var context = new SnapshotAnalysisContext("snap1", null, null, null, false, null);

        var findings = await engine.AnalyzeAsync(context, TestContext.Current.CancellationToken);

        Assert.Empty(findings);
        Assert.Equal(10, engine.GetActiveRules().Count);
    }

    [Fact]
    public async Task Analyze_SkipsDisabledRules()
    {
        var config = new MemoryLensConfig
        {
            Rules = new Dictionary<string, RuleOverride>
            {
                ["ML001"] = new() { Enabled = false },
                ["ML005"] = new() { Enabled = false }
            }
        };
        var engine = new AnalysisEngine(config);
        var context = new SnapshotAnalysisContext("snap1", null, null, null, false, null);

        var findings = await engine.AnalyzeAsync(context, TestContext.Current.CancellationToken);

        Assert.Empty(findings);
        Assert.Equal(8, engine.GetActiveRules().Count);
        Assert.DoesNotContain(engine.GetActiveRules(), r => r.Id == "ML001");
        Assert.DoesNotContain(engine.GetActiveRules(), r => r.Id == "ML005");
    }

    [Fact]
    public void GetActiveRules_RespectsConfig()
    {
        var config = new MemoryLensConfig
        {
            Rules = new Dictionary<string, RuleOverride>
            {
                ["ML010"] = new() { Enabled = false }
            }
        };
        var engine = new AnalysisEngine(config);

        var activeRules = engine.GetActiveRules();

        Assert.Equal(9, activeRules.Count);
        Assert.DoesNotContain(activeRules, r => r.Id == "ML010");
    }

    [Fact]
    public async Task Analyze_ProducesFindings_WhenDataHasIssues()
    {
        var config = new MemoryLensConfig();
        var engine = new AnalysisEngine(config);

        var data = new SnapshotData
        {
            Types =
            [
                new TypeInfo { FullName = "System.EventHandler", InstanceCount = 100, TotalBytes = 5000 },
            ]
        };
        var context = new SnapshotAnalysisContext("snap1", null, null, null, false, null) { Data = data };

        var findings = await engine.AnalyzeAsync(context, TestContext.Current.CancellationToken);

        Assert.NotEmpty(findings);
        Assert.Contains(findings, f => f.RuleId == "ML001");
    }

    [Fact]
    public async Task Analyze_AppliesSeverityOverride()
    {
        var config = new MemoryLensConfig
        {
            Rules = new Dictionary<string, RuleOverride>
            {
                ["ML001"] = new() { Severity = "low" }
            }
        };
        var engine = new AnalysisEngine(config);

        var data = new SnapshotData
        {
            Types =
            [
                new TypeInfo { FullName = "System.EventHandler", InstanceCount = 100, TotalBytes = 5000 },
            ]
        };
        var context = new SnapshotAnalysisContext("snap1", null, null, null, false, null) { Data = data };

        var findings = await engine.AnalyzeAsync(context, TestContext.Current.CancellationToken);

        var ml001Finding = findings.First(f => f.RuleId == "ML001");
        Assert.Equal("low", ml001Finding.Severity);
    }
}
