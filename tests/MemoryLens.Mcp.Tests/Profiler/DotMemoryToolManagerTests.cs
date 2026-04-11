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

    [Fact]
    public async Task ResolveCommand_Prefers_DOTMEMORY_Path_Over_Other_Modes()
    {
        Environment.SetEnvironmentVariable("DOTMEMORY_PATH", "/custom/path/dotMemory.sh");
        var manager = new DotMemoryToolManager(new FakeProcessRunner(exitCode: 0, output: ""));

        // Invalidate cache to force re-resolution
        manager.InvalidateCache();

        var command = await manager.ResolveCommandAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(command);
        Assert.Contains("dotMemory.sh", command.FileName);
        Environment.SetEnvironmentVariable("DOTMEMORY_PATH", null);
    }

    [Fact]
    public async Task InvalidateCache_ClearsCachedCommand()
    {
        var fakeRunner = new FakeProcessRunner(exitCode: 0, output: "");
        var manager = new FakeDotMemoryToolManager(fakeRunner, new DotMemoryCommand(
            "dotMemory.sh", "", "Test CLI", "1.0.0"));
        var firstCommand = await manager.ResolveCommandAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(firstCommand);

        manager.InvalidateCache();
        var secondCommand = await manager.ResolveCommandAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(secondCommand);
    }
}
