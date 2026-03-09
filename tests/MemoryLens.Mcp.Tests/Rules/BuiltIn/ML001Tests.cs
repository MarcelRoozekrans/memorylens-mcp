using MemoryLens.Mcp.Analysis;
using MemoryLens.Mcp.Rules;
using MemoryLens.Mcp.Rules.BuiltIn;
using Xunit;

namespace MemoryLens.Mcp.Tests.Rules.BuiltIn;

public class ML001Tests
{
    private readonly ML001_EventHandlerLeak _rule = new();

    [Fact]
    public void Id_ReturnsML001()
    {
        Assert.Equal("ML001", _rule.Id);
    }

    [Fact]
    public void Severity_ReturnsCritical()
    {
        Assert.Equal("critical", _rule.Severity);
    }

    [Fact]
    public void Category_ReturnsLeak()
    {
        Assert.Equal("leak", _rule.Category);
    }

    [Fact]
    public void Title_ReturnsExpected()
    {
        Assert.Equal("Event handler leak detected", _rule.Title);
    }

    [Fact]
    public async Task EvaluateAsync_ReturnsEmpty_WhenNoData()
    {
        var context = new SnapshotAnalysisContext("snap1", null, null, null, false, null);
        var findings = await _rule.EvaluateAsync(context, TestContext.Current.CancellationToken);
        Assert.Empty(findings);
    }

    [Fact]
    public async Task EvaluateAsync_DetectsHighEventHandlerCount()
    {
        var data = new SnapshotData
        {
            Types =
            [
                new TypeInfo { FullName = "System.EventHandler", InstanceCount = 100, TotalBytes = 5000 },
                new TypeInfo { FullName = "System.String", InstanceCount = 200, TotalBytes = 10000 },
            ]
        };
        var context = new SnapshotAnalysisContext("snap1", null, null, null, false, null) { Data = data };

        var findings = await _rule.EvaluateAsync(context, TestContext.Current.CancellationToken);

        Assert.Single(findings);
        Assert.Equal("ML001", findings[0].RuleId);
        Assert.Contains("EventHandler", findings[0].Description);
    }

    [Fact]
    public async Task EvaluateAsync_IgnoresLowEventHandlerCount()
    {
        var data = new SnapshotData
        {
            Types =
            [
                new TypeInfo { FullName = "System.EventHandler", InstanceCount = 5, TotalBytes = 250 },
            ]
        };
        var context = new SnapshotAnalysisContext("snap1", null, null, null, false, null) { Data = data };

        var findings = await _rule.EvaluateAsync(context, TestContext.Current.CancellationToken);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task EvaluateAsync_DetectsGrowingDelegatesInComparison()
    {
        var comparison = new ComparisonData
        {
            Before = new SnapshotData(),
            After = new SnapshotData(),
            Deltas =
            [
                new TypeDelta { FullName = "System.EventHandler`1[[MyApp.DataChangedEventArgs]]", InstancesBefore = 10, InstancesAfter = 100, BytesBefore = 500, BytesAfter = 5000 },
            ]
        };
        var context = new SnapshotAnalysisContext("snap1", null, "/before", "/after", true, null) { Comparison = comparison };

        var findings = await _rule.EvaluateAsync(context, TestContext.Current.CancellationToken);

        Assert.Single(findings);
        Assert.Contains("grew from 10 to 100", findings[0].Description);
    }
}
