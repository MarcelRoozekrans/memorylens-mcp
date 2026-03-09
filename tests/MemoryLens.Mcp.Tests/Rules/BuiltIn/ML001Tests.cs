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
    public async Task EvaluateAsync_ReturnsEmptyList()
    {
        var context = new SnapshotAnalysisContext("snap1", null, null, null, false, null);
        var findings = await _rule.EvaluateAsync(context);
        Assert.Empty(findings);
    }
}
