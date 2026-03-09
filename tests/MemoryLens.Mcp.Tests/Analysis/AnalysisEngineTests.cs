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
        var context = new SnapshotAnalysisContext("snap1", null, null, null, false, null);

        var findings = await engine.AnalyzeAsync(context);

        // All rules are stubs returning empty lists, so no findings expected
        Assert.Empty(findings);
        // But all 10 rules should be active
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

        var findings = await engine.AnalyzeAsync(context);

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
}
