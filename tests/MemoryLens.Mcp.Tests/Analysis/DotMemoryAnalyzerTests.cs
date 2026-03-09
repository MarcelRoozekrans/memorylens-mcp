using MemoryLens.Mcp.Analysis;
using MemoryLens.Mcp.Tests.Profiler;
using Xunit;

namespace MemoryLens.Mcp.Tests.Analysis;

public class DotMemoryAnalyzerTests
{
    private const string SampleGcDumpOutput = """
                  MT    Count    TotalSize Class Name
        00007ff8a1b2c3d4      500       25000 System.String
        00007ff8a1b2c3e0       50        5000 System.IO.FileStream
        Total      550       30000
        """;

    [Fact]
    public async Task AnalyzeSnapshot_ParsesGcDumpReport()
    {
        var runner = new FakeProcessRunner(exitCode: 0, output: SampleGcDumpOutput);
        var analyzer = new DotMemoryAnalyzer(runner);

        var data = await analyzer.AnalyzeSnapshotAsync("/some/snapshot.gcdump", TestContext.Current.CancellationToken);

        Assert.Equal(2, data.Types.Count);
        Assert.Equal(30000, data.Heap.TotalBytes);
    }

    [Fact]
    public async Task AnalyzeSnapshot_ReturnsEmpty_WhenToolFails()
    {
        var runner = new FakeProcessRunner(exitCode: 1, output: "");
        var analyzer = new DotMemoryAnalyzer(runner);

        var data = await analyzer.AnalyzeSnapshotAsync("/some/snapshot.dmw", TestContext.Current.CancellationToken);

        Assert.Empty(data.Types);
    }

    [Fact]
    public async Task CompareSnapshots_ComputesDeltas()
    {
        var beforeOutput = """
                  MT    Count    TotalSize Class Name
        00007ff8a1b2c3d4      100       10000 System.String
        00007ff8a1b2c3e0       10        1000 System.IO.FileStream
        Total      110       11000
        """;

        var afterOutput = """
                  MT    Count    TotalSize Class Name
        00007ff8a1b2c3d4      300       30000 System.String
        00007ff8a1b2c3e0       25        2500 System.IO.FileStream
        00007ff8a1b2c3f0        5         500 MyApp.NewType
        Total      330       33000
        """;

        var runner = new FakeProcessRunner(exitCode: 0, output: beforeOutput);
        runner.SetNextResult(exitCode: 0, output: afterOutput);
        var analyzer = new DotMemoryAnalyzer(runner);

        var comparison = await analyzer.CompareSnapshotsAsync("/before.gcdump", "/after.gcdump", TestContext.Current.CancellationToken);

        Assert.Equal(2, comparison.Before.Types.Count);
        Assert.Equal(3, comparison.After.Types.Count);

        // All three types changed
        Assert.Equal(3, comparison.Deltas.Count);

        var stringDelta = comparison.Deltas.First(d => d.FullName == "System.String");
        Assert.Equal(100, stringDelta.InstancesBefore);
        Assert.Equal(300, stringDelta.InstancesAfter);
        Assert.Equal(200, stringDelta.InstanceDelta);
        Assert.Equal(20000, stringDelta.BytesDelta);

        // New type has 0 before
        var newTypeDelta = comparison.Deltas.First(d => d.FullName == "MyApp.NewType");
        Assert.Equal(0, newTypeDelta.InstancesBefore);
        Assert.Equal(5, newTypeDelta.InstancesAfter);
    }
}
