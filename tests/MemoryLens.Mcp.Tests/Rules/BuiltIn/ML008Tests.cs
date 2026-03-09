using MemoryLens.Mcp.Analysis;
using MemoryLens.Mcp.Rules;
using MemoryLens.Mcp.Rules.BuiltIn;
using Xunit;

namespace MemoryLens.Mcp.Tests.Rules.BuiltIn;

public class ML008Tests
{
    private readonly ML008_ArrayResizingWithoutCapacity _rule = new();

    [Fact]
    public async Task EvaluateAsync_DetectsHighArrayCount()
    {
        var data = new SnapshotData
        {
            Types =
            [
                new TypeInfo { FullName = "System.Object[]", InstanceCount = 5_000, TotalBytes = 2_000_000 },
            ]
        };
        var context = new SnapshotAnalysisContext("snap1", null, null, null, false, null) { Data = data };

        var findings = await _rule.EvaluateAsync(context, TestContext.Current.CancellationToken);

        Assert.Single(findings);
        Assert.Equal("ML008", findings[0].RuleId);
        Assert.Contains("capacity", findings[0].Description);
    }

    [Fact]
    public async Task EvaluateAsync_IgnoresLowArrayCount()
    {
        var data = new SnapshotData
        {
            Types =
            [
                new TypeInfo { FullName = "System.Object[]", InstanceCount = 50, TotalBytes = 5000 },
            ]
        };
        var context = new SnapshotAnalysisContext("snap1", null, null, null, false, null) { Data = data };

        var findings = await _rule.EvaluateAsync(context, TestContext.Current.CancellationToken);

        Assert.Empty(findings);
    }
}
