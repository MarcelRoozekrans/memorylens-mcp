using MemoryLens.Mcp.Profiler;
using Xunit;

namespace MemoryLens.Mcp.Tests.Profiler;

public class ProcessFilterTests
{
    [Theory]
    [InlineData("devenv", true)]
    [InlineData("rider64", true)]
    [InlineData("Code", true)]
    [InlineData("code-insiders", true)]
    [InlineData("ServiceHub.Host", true)]
    [InlineData("dotnet-dotmemory", true)]
    [InlineData("MyApp", false)]
    [InlineData("WebApi", false)]
    public void IsExcluded_FiltersCorrectly(string processName, bool expectedExcluded)
    {
        var filter = new ProcessFilter();
        Assert.Equal(expectedExcluded, filter.IsExcluded(processName, ""));
    }

    [Fact]
    public void IsExcluded_FiltersRoslynCodegraph_ByCommandLine()
    {
        var filter = new ProcessFilter();
        Assert.True(filter.IsExcluded("dotnet",
            "dotnet run --project roslyn-codegraph-mcp"));
    }
}
