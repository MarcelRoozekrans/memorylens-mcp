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
        var configuredPath = Path.GetTempFileName();
        try
        {
            Environment.SetEnvironmentVariable("DOTMEMORY_PATH", configuredPath);
            var manager = new DotMemoryToolManager(new FakeProcessRunner(exitCode: 0, output: ""));

            // Invalidate cache to force re-resolution
            manager.InvalidateCache();

            var command = await manager.ResolveCommandAsync(TestContext.Current.CancellationToken);

            Assert.NotNull(command);
            Assert.Equal(configuredPath, command.FileName);
            Assert.Equal(DotMemoryCommandKind.ExplicitPath, command.Kind);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTMEMORY_PATH", null);
            File.Delete(configuredPath);
        }
    }

    [Fact]
    public async Task InvalidateCache_ClearsCachedCommand()
    {
        var fakeRunner = new FakeProcessRunner(exitCode: 0, output: "");
        var manager = new DotMemoryToolManager(fakeRunner);

        var tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var firstConfiguredPath = Path.Combine(tempDirectory, "first-dotMemory.sh");
        var secondConfiguredPath = Path.Combine(tempDirectory, "second-dotMemory.sh");

        Directory.CreateDirectory(tempDirectory);
        File.WriteAllText(firstConfiguredPath, string.Empty);
        File.WriteAllText(secondConfiguredPath, string.Empty);

        try
        {
            Environment.SetEnvironmentVariable("DOTMEMORY_PATH", firstConfiguredPath);

            var firstCommand = await manager.ResolveCommandAsync(TestContext.Current.CancellationToken);
            Assert.NotNull(firstCommand);
            Assert.Equal(firstConfiguredPath, firstCommand.FileName);
            Assert.Equal(DotMemoryCommandKind.ExplicitPath, firstCommand.Kind);

            Environment.SetEnvironmentVariable("DOTMEMORY_PATH", secondConfiguredPath);
            manager.InvalidateCache();

            var secondCommand = await manager.ResolveCommandAsync(TestContext.Current.CancellationToken);
            Assert.NotNull(secondCommand);
            Assert.Equal(secondConfiguredPath, secondCommand.FileName);
            Assert.Equal(DotMemoryCommandKind.ExplicitPath, secondCommand.Kind);
            Assert.NotEqual(firstCommand.FileName, secondCommand.FileName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTMEMORY_PATH", null);
            if (File.Exists(firstConfiguredPath))
            {
                File.Delete(firstConfiguredPath);
            }

            if (File.Exists(secondConfiguredPath))
            {
                File.Delete(secondConfiguredPath);
            }

            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory);
            }
        }
    }

    [Fact]
    public async Task EnsureInstalled_DoesNotUpdate_WhenNotGlobalTool()
    {
        var fakeRunner = new FakeProcessRunner(exitCode: 0, output: "");
        var manager = new FakeDotMemoryToolManager(fakeRunner, new DotMemoryCommand(
            "/path/to/dotMemory.sh", "", "Explicit Path CLI", "1.0.0", DotMemoryCommandKind.ExplicitPath));

        var result = await manager.EnsureInstalledAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsInstalled);
        Assert.Contains("Explicit Path CLI", result.Message);
    }

    [Fact]
    public async Task ResolveCommand_ReturnsAutoInstalled_WhenCacheHit()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var autoInstaller = new FakeDotMemoryAutoInstaller(cachedPath: tempFile);
            var manager = new DotMemoryToolManager(new FakeProcessRunner(exitCode: 1, output: ""), autoInstaller);

            var command = await manager.ResolveCommandAsync(TestContext.Current.CancellationToken);

            Assert.NotNull(command);
            Assert.Equal(tempFile, command.FileName);
            Assert.Equal(DotMemoryCommandKind.AutoInstalled, command.Kind);
        }
        finally { File.Delete(tempFile); }
    }

    [Fact]
    public async Task EnsureInstalled_CallsInstallLatest_WhenCacheMiss()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var autoInstaller = new FakeDotMemoryAutoInstaller(cachedPath: null, installPath: tempFile);
            var manager = new DotMemoryToolManager(new FakeProcessRunner(exitCode: 1, output: ""), autoInstaller);

            var result = await manager.EnsureInstalledAsync(TestContext.Current.CancellationToken);

            Assert.True(result.IsInstalled);
            Assert.Equal(DotMemoryCommandKind.AutoInstalled, result.Kind);
        }
        finally { File.Delete(tempFile); }
    }

    [Fact]
    public async Task EnsureInstalled_FallsThrough_WhenInstallLatestFails()
    {
        var autoInstaller = new FakeDotMemoryAutoInstaller(cachedPath: null, installPath: null);
        var runner = new FakeProcessRunner(exitCode: 1, output: "");
        var manager = new DotMemoryToolManager(runner, autoInstaller);

        var result = await manager.EnsureInstalledAsync(TestContext.Current.CancellationToken);

        Assert.False(result.IsInstalled);
    }

    [Fact]
    public async Task EnsureInstalled_ReturnsUnsupportedMessage_WhenPlatformNotSupported()
    {
        var autoInstaller = new FakeDotMemoryAutoInstaller(
            unsupportedMessage: "Platform freebsd-x64 is not supported.");
        var runner = new FakeProcessRunner(exitCode: 1, output: "");
        var manager = new DotMemoryToolManager(runner, autoInstaller);

        var result = await manager.EnsureInstalledAsync(TestContext.Current.CancellationToken);

        Assert.False(result.IsInstalled);
        Assert.Contains("freebsd-x64", result.Message);
        Assert.Equal(0, autoInstaller.InstallLatestAsyncCallCount);
    }
}
