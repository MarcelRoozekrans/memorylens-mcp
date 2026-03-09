#pragma warning disable MA0048 // File name must match type name - intentional companion types
namespace MemoryLens.Mcp.Analysis;

/// <summary>
/// Parsed memory snapshot data extracted from profiler output.
/// </summary>
public class SnapshotData
{
    public IList<TypeInfo> Types { get; init; } = [];
    public HeapInfo Heap { get; init; } = new();
}

public class TypeInfo
{
    public required string FullName { get; init; }
    public int InstanceCount { get; init; }
    public long TotalBytes { get; init; }

    /// <summary>
    /// True if this type implements IDisposable (detected by name heuristics or metadata).
    /// </summary>
    public bool ImplementsIDisposable { get; init; }

    /// <summary>
    /// True if this type has a finalizer (detected by ~ClassName() pattern in metadata).
    /// </summary>
    public bool HasFinalizer { get; init; }

    /// <summary>
    /// GC generation where the majority of instances reside (0, 1, 2).
    /// -1 if unknown.
    /// </summary>
    public int DominantGeneration { get; init; } = -1;

    /// <summary>
    /// True if instances reside on the Large Object Heap (objects > 85KB).
    /// </summary>
    public bool IsLargeObjectHeap { get; init; }
}

public class HeapInfo
{
    public long TotalBytes { get; init; }
    public long LargeObjectHeapBytes { get; init; }
    public int LargeObjectCount { get; init; }
}

/// <summary>
/// Comparison data from two snapshots taken at different times.
/// </summary>
public class ComparisonData
{
    public SnapshotData Before { get; init; } = new();
    public SnapshotData After { get; init; } = new();
    public IList<TypeDelta> Deltas { get; init; } = [];
}

public class TypeDelta
{
    public required string FullName { get; init; }
    public int InstancesBefore { get; init; }
    public int InstancesAfter { get; init; }
    public long BytesBefore { get; init; }
    public long BytesAfter { get; init; }

    public int InstanceDelta => InstancesAfter - InstancesBefore;
    public long BytesDelta => BytesAfter - BytesBefore;
}
