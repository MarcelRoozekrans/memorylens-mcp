using MemoryLens.Mcp.Analysis;
using MemoryLens.Mcp.Profiler;
using Xunit;

namespace MemoryLens.Mcp.Tests.Analysis;

public class SnapshotReaderTests
{
    private sealed class FakeSnapshotStore(SnapshotData before, SnapshotData after) : ISnapshotStore
    {
        public Task<string> SaveAsync(SnapshotData data, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SnapshotData> LoadAsync(string idOrPath, CancellationToken ct) =>
            Task.FromResult(idOrPath == "before" ? before : after);
    }

    [Fact]
    public async Task CompareAsync_ComputesDeltas()
    {
        var before = new SnapshotData
        {
            Types =
            [
                new TypeInfo { FullName = "System.String", InstanceCount = 100, TotalBytes = 10000 },
                new TypeInfo { FullName = "System.IO.FileStream", InstanceCount = 10, TotalBytes = 1000 },
            ],
        };

        var after = new SnapshotData
        {
            Types =
            [
                new TypeInfo { FullName = "System.String", InstanceCount = 300, TotalBytes = 30000 },
                new TypeInfo { FullName = "System.IO.FileStream", InstanceCount = 25, TotalBytes = 2500 },
                new TypeInfo { FullName = "MyApp.NewType", InstanceCount = 5, TotalBytes = 500 },
            ],
        };

        var reader = new SnapshotReader(new FakeSnapshotStore(before, after));

        var comparison = await reader.CompareAsync("before", "after", TestContext.Current.CancellationToken);

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
