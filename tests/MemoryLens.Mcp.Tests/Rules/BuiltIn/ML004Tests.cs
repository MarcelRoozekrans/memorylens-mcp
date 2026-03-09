using MemoryLens.Mcp.Analysis;
using MemoryLens.Mcp.Rules;
using MemoryLens.Mcp.Rules.BuiltIn;
using Xunit;

namespace MemoryLens.Mcp.Tests.Rules.BuiltIn;

public class ML004Tests
{
    private readonly ML004_LargeObjectHeapFragmentation _rule = new();

    [Fact]
    public async Task EvaluateAsync_DetectsLargeObjectHeap()
    {
        var data = new SnapshotData
        {
            Heap = new HeapInfo { LargeObjectHeapBytes = 60_000_000, LargeObjectCount = 15 }
        };
        var context = new SnapshotAnalysisContext("snap1", null, null, null, false, null) { Data = data };

        var findings = await _rule.EvaluateAsync(context, TestContext.Current.CancellationToken);

        Assert.Single(findings);
        Assert.Equal("ML004", findings[0].RuleId);
        Assert.Contains("Large Object Heap", findings[0].Description);
    }

    [Fact]
    public async Task EvaluateAsync_IgnoresSmallLOH()
    {
        var data = new SnapshotData
        {
            Heap = new HeapInfo { LargeObjectHeapBytes = 1_000_000, LargeObjectCount = 2 }
        };
        var context = new SnapshotAnalysisContext("snap1", null, null, null, false, null) { Data = data };

        var findings = await _rule.EvaluateAsync(context, TestContext.Current.CancellationToken);

        Assert.Empty(findings);
    }
}
