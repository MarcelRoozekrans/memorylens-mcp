using MemoryLens.Mcp.Profiler;
using Xunit;

namespace MemoryLens.Mcp.Tests.Profiler;

public class SnapshotManagerTests
{
    [Fact]
    public async Task TakeSnapshot_ByPid_ReturnsSnapshotId()
    {
        var runner = new FakeProcessRunner(exitCode: 0, output: "Snapshot saved");
        var filter = new ProcessFilter();
        var manager = new SnapshotManager(runner, filter);

        var result = await manager.TakeSnapshotAsync(pid: 1234);

        Assert.True(result.Success);
        Assert.NotNull(result.SnapshotId);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task TakeSnapshot_ExcludedProcess_ReturnsError()
    {
        var runner = new FakeProcessRunner(exitCode: 0, output: "");
        var filter = new ProcessFilter();
        var manager = new SnapshotManager(runner, filter);

        var result = await manager.TakeSnapshotAsync(processName: "devenv");

        Assert.False(result.Success);
        Assert.Contains("excluded", result.Error!);
    }

    [Fact]
    public async Task TakeSnapshot_ByProcessName_ReturnsSnapshotId()
    {
        var runner = new FakeProcessRunner(exitCode: 0, output: "Snapshot saved");
        var filter = new ProcessFilter();
        var manager = new SnapshotManager(runner, filter);

        var result = await manager.TakeSnapshotAsync(processName: "MyApp");

        Assert.True(result.Success);
        Assert.NotNull(result.SnapshotId);
    }

    [Fact]
    public async Task TakeSnapshot_WithCommand_ReturnsSnapshotId()
    {
        var runner = new FakeProcessRunner(exitCode: 0, output: "Snapshot saved");
        var filter = new ProcessFilter();
        var manager = new SnapshotManager(runner, filter);

        var result = await manager.TakeSnapshotAsync(command: "dotnet run --project MyApp");

        Assert.True(result.Success);
        Assert.NotNull(result.SnapshotId);
    }

    [Fact]
    public async Task TakeSnapshot_ProcessRunnerFails_ReturnsError()
    {
        var runner = new FakeProcessRunner(exitCode: 1, output: "");
        var filter = new ProcessFilter();
        var manager = new SnapshotManager(runner, filter);

        var result = await manager.TakeSnapshotAsync(pid: 1234);

        Assert.False(result.Success);
        Assert.Contains("dotnet-dotmemory failed", result.Error!);
    }
}
