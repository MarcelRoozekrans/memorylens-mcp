using MemoryLens.Mcp.Profiler;
using Xunit;

namespace MemoryLens.Mcp.Tests.Profiler;

public class DotMemoryToolManagerTests
{
    [Fact]
    public async Task EnsureInstalled_ReturnsStatus_WhenToolExists()
    {
        var manager = new DotMemoryToolManager(new FakeProcessRunner(
            exitCode: 0,
            output: "dotnet-dotmemory  2024.3.0  dotnet-dotmemory"));

        var result = await manager.EnsureInstalledAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsInstalled);
        Assert.Contains("2024.3.0", result.Version);
    }

    [Fact]
    public async Task EnsureInstalled_InstallsTool_WhenNotFound()
    {
        var runner = new FakeProcessRunner(exitCode: 1, output: "");
        runner.SetNextResult(exitCode: 0, output: "Tool 'dotnet-dotmemory' was successfully installed.");
        var manager = new DotMemoryToolManager(runner);

        var result = await manager.EnsureInstalledAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsInstalled);
    }
}
