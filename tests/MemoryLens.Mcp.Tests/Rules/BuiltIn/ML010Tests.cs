using MemoryLens.Mcp.Analysis;
using MemoryLens.Mcp.Rules;
using MemoryLens.Mcp.Rules.BuiltIn;
using Xunit;

namespace MemoryLens.Mcp.Tests.Rules.BuiltIn;

public class ML010Tests
{
    private readonly ML010_StringInterningOpportunity _rule = new();

    [Fact]
    public async Task EvaluateAsync_DetectsStringInterningOpportunity()
    {
        var data = new SnapshotData
        {
            Types =
            [
                // 50000 strings at 6MB, avg 120 bytes — many small strings
                new TypeInfo { FullName = "System.String", InstanceCount = 50_000, TotalBytes = 6_000_000 },
            ]
        };
        var context = new SnapshotAnalysisContext("snap1", null, null, null, false, null) { Data = data };

        var findings = await _rule.EvaluateAsync(context, TestContext.Current.CancellationToken);

        Assert.Single(findings);
        Assert.Equal("ML010", findings[0].RuleId);
        Assert.Contains("String.Intern", findings[0].Description);
    }

    [Fact]
    public async Task EvaluateAsync_IgnoresLargeStrings()
    {
        var data = new SnapshotData
        {
            Types =
            [
                // 100 strings at 10MB, avg 100KB — large strings, not duplication
                new TypeInfo { FullName = "System.String", InstanceCount = 100, TotalBytes = 10_000_000 },
            ]
        };
        var context = new SnapshotAnalysisContext("snap1", null, null, null, false, null) { Data = data };

        var findings = await _rule.EvaluateAsync(context, TestContext.Current.CancellationToken);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task EvaluateAsync_IgnoresLowStringCount()
    {
        var data = new SnapshotData
        {
            Types =
            [
                new TypeInfo { FullName = "System.String", InstanceCount = 100, TotalBytes = 5000 },
            ]
        };
        var context = new SnapshotAnalysisContext("snap1", null, null, null, false, null) { Data = data };

        var findings = await _rule.EvaluateAsync(context, TestContext.Current.CancellationToken);

        Assert.Empty(findings);
    }
}
