using MemoryLens.Mcp.Analysis;
using MemoryLens.Mcp.Rules;
using MemoryLens.Mcp.Rules.BuiltIn;
using Xunit;

namespace MemoryLens.Mcp.Tests.Rules.BuiltIn;

public class ML003Tests
{
    private readonly ML003_DisposableNotDisposed _rule = new();

    [Fact]
    public async Task EvaluateAsync_DetectsDisposableWithHighCount()
    {
        var data = new SnapshotData
        {
            Types =
            [
                new TypeInfo { FullName = "System.IO.FileStream", InstanceCount = 50, TotalBytes = 200_000, ImplementsIDisposable = true },
            ]
        };
        var context = new SnapshotAnalysisContext("snap1", null, null, null, false, null) { Data = data };

        var findings = await _rule.EvaluateAsync(context, TestContext.Current.CancellationToken);

        Assert.Single(findings);
        Assert.Equal("ML003", findings[0].RuleId);
        Assert.Contains("FileStream", findings[0].Description);
    }

    [Fact]
    public async Task EvaluateAsync_IgnoresNonDisposableTypes()
    {
        var data = new SnapshotData
        {
            Types =
            [
                new TypeInfo { FullName = "System.String", InstanceCount = 10000, TotalBytes = 500_000, ImplementsIDisposable = false },
            ]
        };
        var context = new SnapshotAnalysisContext("snap1", null, null, null, false, null) { Data = data };

        var findings = await _rule.EvaluateAsync(context, TestContext.Current.CancellationToken);

        Assert.Empty(findings);
    }
}
