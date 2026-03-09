using MemoryLens.Mcp.Analysis;
using MemoryLens.Mcp.Rules;
using MemoryLens.Mcp.Rules.BuiltIn;
using Xunit;

namespace MemoryLens.Mcp.Tests.Rules.BuiltIn;

public class ML009Tests
{
    private readonly ML009_FinalizerWithoutDispose _rule = new();

    [Fact]
    public async Task EvaluateAsync_DetectsFinalizerWithoutDispose()
    {
        var data = new SnapshotData
        {
            Types =
            [
                new TypeInfo { FullName = "MyApp.NativeWrapper", InstanceCount = 5, TotalBytes = 500, HasFinalizer = true, ImplementsIDisposable = false },
            ]
        };
        var context = new SnapshotAnalysisContext("snap1", null, null, null, false, null) { Data = data };

        var findings = await _rule.EvaluateAsync(context, TestContext.Current.CancellationToken);

        Assert.Single(findings);
        Assert.Equal("ML009", findings[0].RuleId);
        Assert.Contains("Dispose pattern", findings[0].Description);
    }

    [Fact]
    public async Task EvaluateAsync_IgnoresProperDisposePattern()
    {
        var data = new SnapshotData
        {
            Types =
            [
                new TypeInfo { FullName = "MyApp.NativeWrapper", InstanceCount = 5, TotalBytes = 500, HasFinalizer = true, ImplementsIDisposable = true },
            ]
        };
        var context = new SnapshotAnalysisContext("snap1", null, null, null, false, null) { Data = data };

        var findings = await _rule.EvaluateAsync(context, TestContext.Current.CancellationToken);

        Assert.Empty(findings);
    }
}
