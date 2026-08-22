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
            new TypeInfo
            {
                FullName = "System.String",
                InstanceCount = 1000,
                TotalBytes = 40000,
                ImplementsIDisposable = false,
                HasFinalizer = false,
                DominantGeneration = 2,
                IsLargeObjectHeap = false,
            },
            new TypeInfo
            {
                FullName = "MyApp.Thing",
                InstanceCount = 7,
                TotalBytes = 168,
                ImplementsIDisposable = true,
                HasFinalizer = true,
                DominantGeneration = 1,
                IsLargeObjectHeap = true,
            },
        ],
        Heap = new HeapInfo { TotalBytes = 40168, LargeObjectHeapBytes = 168, LargeObjectCount = 1 },
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
            Assert.False(loaded.Types[0].ImplementsIDisposable);
            Assert.False(loaded.Types[0].HasFinalizer);
            Assert.Equal(2, loaded.Types[0].DominantGeneration);
            Assert.False(loaded.Types[0].IsLargeObjectHeap);

            Assert.Equal("MyApp.Thing", loaded.Types[1].FullName);
            Assert.Equal(7, loaded.Types[1].InstanceCount);
            Assert.Equal(168, loaded.Types[1].TotalBytes);
            Assert.True(loaded.Types[1].ImplementsIDisposable);
            Assert.True(loaded.Types[1].HasFinalizer);
            Assert.Equal(1, loaded.Types[1].DominantGeneration);
            Assert.True(loaded.Types[1].IsLargeObjectHeap);

            Assert.Equal(40168, loaded.Heap.TotalBytes);
            Assert.Equal(168, loaded.Heap.LargeObjectHeapBytes);
            Assert.Equal(1, loaded.Heap.LargeObjectCount);
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
