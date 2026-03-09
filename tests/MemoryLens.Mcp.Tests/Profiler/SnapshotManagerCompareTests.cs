using MemoryLens.Mcp.Profiler;
using Xunit;

namespace MemoryLens.Mcp.Tests.Profiler;

public class SnapshotManagerCompareTests
{
    [Fact]
    public async Task CompareSnapshots_TakesTwoSnapshots_ReturnsDelta()
    {
        var runner = new FakeProcessRunner(exitCode: 0, output: "Before snapshot saved");
        runner.SetNextResult(exitCode: 0, output: "After snapshot saved");
        var filter = new ProcessFilter();
        var manager = new SnapshotManager(runner, filter);

        var result = await manager.CompareSnapshotsAsync(pid: 1234, delaySeconds: 0, ct: TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.NotNull(result.SnapshotId);
        Assert.Equal(2, result.SnapshotCount);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task CompareSnapshots_ExcludedProcess_ReturnsError()
    {
        var runner = new FakeProcessRunner(exitCode: 0, output: "");
        var filter = new ProcessFilter();
        var manager = new SnapshotManager(runner, filter);

        var result = await manager.CompareSnapshotsAsync(processName: "devenv", delaySeconds: 0, ct: TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("excluded", result.Error!);
        Assert.Equal(0, result.SnapshotCount);
    }

    [Fact]
    public async Task CompareSnapshots_BeforeFails_ReturnsError()
    {
        var runner = new FakeProcessRunner(exitCode: 1, output: "");
        var filter = new ProcessFilter();
        var manager = new SnapshotManager(runner, filter);

        var result = await manager.CompareSnapshotsAsync(pid: 1234, delaySeconds: 0, ct: TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Before snapshot failed", result.Error!);
        Assert.Equal(0, result.SnapshotCount);
    }

    [Fact]
    public async Task CompareSnapshots_AfterFails_ReturnsPartialResult()
    {
        var runner = new FakeProcessRunner(exitCode: 0, output: "Before saved");
        runner.SetNextResult(exitCode: 1, output: "");
        var filter = new ProcessFilter();
        var manager = new SnapshotManager(runner, filter);

        var result = await manager.CompareSnapshotsAsync(pid: 1234, delaySeconds: 0, ct: TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("After snapshot failed", result.Error!);
        Assert.Equal(1, result.SnapshotCount);
    }

    [Fact]
    public async Task CompareSnapshots_WithCommand_TakesTwoSnapshots()
    {
        var runner = new FakeProcessRunner(exitCode: 0, output: "Before snapshot saved");
        runner.SetNextResult(exitCode: 0, output: "After snapshot saved");
        var filter = new ProcessFilter();
        var manager = new SnapshotManager(runner, filter);

        var result = await manager.CompareSnapshotsAsync(command: "dotnet run --project MyApp", delaySeconds: 0, ct: TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(2, result.SnapshotCount);
    }
}
