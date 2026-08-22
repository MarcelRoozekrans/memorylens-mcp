using MemoryLens.Mcp.Analysis;
using MemoryLens.Mcp.Profiler;

namespace MemoryLens.Mcp.Tests.Profiler;

/// <summary>
/// Test double for IHeapCollector. Avoids real EventPipe collection in unit tests
/// (that is exercised by MemoryLens.Mcp.IntegrationTests instead).
/// </summary>
public class FakeHeapCollector(SnapshotData? data = null) : IHeapCollector
{
    private readonly SnapshotData _data = data ?? new SnapshotData
    {
        Types = [new TypeInfo { FullName = "System.String", InstanceCount = 10, TotalBytes = 200 }],
    };

    public Task<SnapshotData> CollectAsync(int pid, CancellationToken ct) =>
        Task.FromResult(_data);
}
