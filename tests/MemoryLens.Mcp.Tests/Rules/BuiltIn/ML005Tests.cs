using MemoryLens.Mcp.Analysis;
using MemoryLens.Mcp.Rules;
using MemoryLens.Mcp.Rules.BuiltIn;
using Xunit;

namespace MemoryLens.Mcp.Tests.Rules.BuiltIn;

public class ML005Tests
{
    private readonly ML005_ObjectRetainedTooLong _rule = new();

    [Fact]
    public async Task EvaluateAsync_DetectsGen2Retention()
    {
        var data = new SnapshotData
        {
            Types =
            [
                new TypeInfo { FullName = "MyApp.Services.CacheEntry", InstanceCount = 100, TotalBytes = 600_000, DominantGeneration = 2 },
            ]
        };
        var context = new SnapshotAnalysisContext("snap1", null, null, null, false, null) { Data = data };

        var findings = await _rule.EvaluateAsync(context, TestContext.Current.CancellationToken);

        Assert.Single(findings);
        Assert.Equal("ML005", findings[0].RuleId);
        Assert.Contains("Gen2", findings[0].Description);
    }

    [Fact]
    public async Task EvaluateAsync_IgnoresExpectedLongLived()
    {
        var data = new SnapshotData
        {
            Types =
            [
                new TypeInfo { FullName = "System.RuntimeType", InstanceCount = 500, TotalBytes = 1_000_000, DominantGeneration = 2 },
            ]
        };
        var context = new SnapshotAnalysisContext("snap1", null, null, null, false, null) { Data = data };

        var findings = await _rule.EvaluateAsync(context, TestContext.Current.CancellationToken);

        Assert.Empty(findings);
    }
}
