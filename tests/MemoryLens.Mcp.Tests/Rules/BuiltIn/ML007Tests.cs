using MemoryLens.Mcp.Analysis;
using MemoryLens.Mcp.Rules;
using MemoryLens.Mcp.Rules.BuiltIn;
using Xunit;

namespace MemoryLens.Mcp.Tests.Rules.BuiltIn;

public class ML007Tests
{
    private readonly ML007_ClosureRetainingReferences _rule = new();

    [Fact]
    public async Task EvaluateAsync_DetectsClosureTypes()
    {
        var data = new SnapshotData
        {
            Types =
            [
                new TypeInfo { FullName = "MyApp.Services.UserService+<>c__DisplayClass5_0", InstanceCount = 100, TotalBytes = 200_000 },
            ]
        };
        var context = new SnapshotAnalysisContext("snap1", null, null, null, false, null) { Data = data };

        var findings = await _rule.EvaluateAsync(context, TestContext.Current.CancellationToken);

        Assert.Single(findings);
        Assert.Equal("ML007", findings[0].RuleId);
        Assert.Contains("closure", findings[0].Description);
    }

    [Fact]
    public async Task EvaluateAsync_IgnoresNormalTypes()
    {
        var data = new SnapshotData
        {
            Types =
            [
                new TypeInfo { FullName = "MyApp.Services.UserService", InstanceCount = 100, TotalBytes = 200_000 },
            ]
        };
        var context = new SnapshotAnalysisContext("snap1", null, null, null, false, null) { Data = data };

        var findings = await _rule.EvaluateAsync(context, TestContext.Current.CancellationToken);

        Assert.Empty(findings);
    }
}
