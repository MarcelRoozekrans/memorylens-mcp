using MemoryLens.Mcp.Analysis;
using MemoryLens.Mcp.Profiler;
using Xunit;

namespace MemoryLens.Mcp.Tests.Profiler;

public class SnapshotStoreTests
{
    private static string NewRoot() =>
        Path.Combine(Path.GetTempPath(), "memorylens-store-" + Guid.NewGuid().ToString("N"));

    private static SnapshotData Sample() => new()
    {
        Types =
        [
            new TypeInfo { FullName = "System.String", InstanceCount = 1000, TotalBytes = 40000 },
            new TypeInfo { FullName = "MyApp.Thing", InstanceCount = 7, TotalBytes = 168, ImplementsIDisposable = true },
        ],
        Heap = new HeapInfo { TotalBytes = 40168, LargeObjectHeapBytes = 0, LargeObjectCount = 0 },
    };

    [Fact]
    public async Task SaveThenLoad_RoundTripsAllFields()
    {
        var root = NewRoot();
        try
        {
            var store = new SnapshotStore(root);
            var id = await store.SaveAsync(Sample(), TestContext.Current.CancellationToken);

            var loaded = await store.LoadAsync(id, TestContext.Current.CancellationToken);

            Assert.Equal(2, loaded.Types.Count);
            Assert.Equal("System.String", loaded.Types[0].FullName);
            Assert.Equal(1000, loaded.Types[0].InstanceCount);
            Assert.Equal(40000, loaded.Types[0].TotalBytes);
            Assert.True(loaded.Types[1].ImplementsIDisposable);
            Assert.Equal(40168, loaded.Heap.TotalBytes);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task LoadAsync_AcceptsAFullPath()
    {
        var root = NewRoot();
        try
        {
            var store = new SnapshotStore(root);
            var id = await store.SaveAsync(Sample(), TestContext.Current.CancellationToken);

            var loaded = await store.LoadAsync(store.PathFor(id), TestContext.Current.CancellationToken);

            Assert.Equal(2, loaded.Types.Count);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task LoadAsync_UnknownId_ThrowsWithTheIdInTheMessage()
    {
        var root = NewRoot();
        try
        {
            var store = new SnapshotStore(root);

            var ex = await Assert.ThrowsAsync<FileNotFoundException>(
                () => store.LoadAsync("nosuchid", TestContext.Current.CancellationToken));

            Assert.Contains("nosuchid", ex.Message, StringComparison.Ordinal);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task SaveAsync_ReturnsDistinctIdsForEachSnapshot()
    {
        var root = NewRoot();
        try
        {
            var store = new SnapshotStore(root);
            var a = await store.SaveAsync(Sample(), TestContext.Current.CancellationToken);
            var b = await store.SaveAsync(Sample(), TestContext.Current.CancellationToken);

            Assert.NotEqual(a, b);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
