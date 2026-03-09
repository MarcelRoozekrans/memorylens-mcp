using MemoryLens.Mcp.Analysis;
using MemoryLens.Mcp.Rules;
using MemoryLens.Mcp.Rules.BuiltIn;
using Xunit;

namespace MemoryLens.Mcp.Tests.Rules.BuiltIn;

public class ML006Tests
{
    private readonly ML006_ExcessiveAllocations _rule = new();

    [Fact]
    public async Task EvaluateAsync_DetectsExcessiveStringAllocations()
    {
        var data = new SnapshotData
        {
            Types =
            [
                new TypeInfo { FullName = "System.String", InstanceCount = 50_000, TotalBytes = 2_500_000 },
            ]
        };
        var context = new SnapshotAnalysisContext("snap1", null, null, null, false, null) { Data = data };

        var findings = await _rule.EvaluateAsync(context, TestContext.Current.CancellationToken);

        Assert.Single(findings);
        Assert.Contains("StringBuilder", findings[0].Description);
    }

    [Fact]
    public async Task EvaluateAsync_DetectsBoxing()
    {
        var data = new SnapshotData
        {
            Types =
            [
                new TypeInfo { FullName = "System.Int32", InstanceCount = 20_000, TotalBytes = 80_000 },
            ]
        };
        var context = new SnapshotAnalysisContext("snap1", null, null, null, false, null) { Data = data };

        var findings = await _rule.EvaluateAsync(context, TestContext.Current.CancellationToken);

        Assert.Single(findings);
        Assert.Contains("boxing", findings[0].Description);
    }
}
