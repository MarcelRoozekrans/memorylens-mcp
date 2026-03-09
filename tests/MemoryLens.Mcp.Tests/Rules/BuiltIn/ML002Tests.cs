using MemoryLens.Mcp.Analysis;
using MemoryLens.Mcp.Rules;
using MemoryLens.Mcp.Rules.BuiltIn;
using Xunit;

namespace MemoryLens.Mcp.Tests.Rules.BuiltIn;

public class ML002Tests
{
    private readonly ML002_StaticCollectionGrowing _rule = new();

    [Fact]
    public async Task EvaluateAsync_DetectsGrowingCollections_InComparison()
    {
        var comparison = new ComparisonData
        {
            Deltas =
            [
                new TypeDelta
                {
                    FullName = "System.Collections.Generic.List`1[[System.String]]",
                    InstancesBefore = 10, InstancesAfter = 20,
                    BytesBefore = 500, BytesAfter = 1000
                },
            ]
        };
        var context = new SnapshotAnalysisContext("snap1", null, "/b", "/a", true, null) { Comparison = comparison };

        var findings = await _rule.EvaluateAsync(context, TestContext.Current.CancellationToken);

        Assert.Single(findings);
        Assert.Equal("ML002", findings[0].RuleId);
        Assert.Contains("grew from 10 to 20", findings[0].Description);
    }

    [Fact]
    public async Task EvaluateAsync_IgnoresSmallGrowth()
    {
        var comparison = new ComparisonData
        {
            Deltas =
            [
                new TypeDelta
                {
                    FullName = "System.Collections.Generic.Dictionary`2[[System.String,System.Object]]",
                    InstancesBefore = 10, InstancesAfter = 12,
                    BytesBefore = 500, BytesAfter = 600
                },
            ]
        };
        var context = new SnapshotAnalysisContext("snap1", null, "/b", "/a", true, null) { Comparison = comparison };

        var findings = await _rule.EvaluateAsync(context, TestContext.Current.CancellationToken);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task EvaluateAsync_DetectsHighCountCollections_InSingleSnapshot()
    {
        var data = new SnapshotData
        {
            Types =
            [
                new TypeInfo { FullName = "System.Collections.Concurrent.ConcurrentDictionary`2[[System.String,System.Object]]", InstanceCount = 200, TotalBytes = 2_000_000 },
            ]
        };
        var context = new SnapshotAnalysisContext("snap1", null, null, null, false, null) { Data = data };

        var findings = await _rule.EvaluateAsync(context, TestContext.Current.CancellationToken);

        Assert.Single(findings);
    }
}
