using MemoryLens.Mcp.Analysis;
using MemoryLens.Mcp.Profiler;

namespace MemoryLens.Mcp.Tests.Profiler;

/// <summary>
/// Test double for IHeapCollector. Avoids real EventPipe collection in unit tests
/// (that is exercised by MemoryLens.Mcp.IntegrationTests instead).
/// </summary>
public class FakeHeapCollector(string? failureMessage = null) : IHeapCollector
{
    private static readonly SnapshotData Default = new()
    {
        Types = [new TypeInfo { FullName = "System.String", InstanceCount = 10, TotalBytes = 200 }],
    };

    public Task<SnapshotData> CollectAsync(int pid, CancellationToken ct)
    {
        if (failureMessage is not null)
            throw new HeapCollectionException(failureMessage);

        return Task.FromResult(Default);
    }
}
