namespace MemoryLens.Mcp.Rules.BuiltIn;

public class ML004_LargeObjectHeapFragmentation : IRule
{
    public string Id => "ML004";
    public string Title => "Large Object Heap fragmentation";
    public string Severity => "high";
    public string Category => "fragmentation";

    private const long DefaultMinBytes = 52_428_800; // 50MB
    private const int MinLargeObjectCount = 10;

    public Task<IReadOnlyList<RuleFinding>> EvaluateAsync(SnapshotAnalysisContext context, CancellationToken ct = default)
    {
        var findings = new List<RuleFinding>();

        if (context.Data is null)
            return Task.FromResult<IReadOnlyList<RuleFinding>>(findings);

        var heap = context.Data.Heap;

        if (heap.LargeObjectHeapBytes >= DefaultMinBytes && heap.LargeObjectCount >= MinLargeObjectCount)
        {
            findings.Add(new RuleFinding(
                Id, Severity, Category, Title,
                $"Large Object Heap contains {heap.LargeObjectCount} objects totaling {heap.LargeObjectHeapBytes:N0} bytes " +
                $"({heap.LargeObjectHeapBytes / 1_048_576.0:F1} MB) — this can cause heap fragmentation and GC pauses",
                new RuleEvidence("LOH", heap.LargeObjectHeapBytes, heap.LargeObjectCount, null),
                null));
        }

        // Also flag individual large types
        foreach (var type in context.Data.Types)
        {
            if (type.IsLargeObjectHeap && type.TotalBytes > DefaultMinBytes / 2)
            {
                findings.Add(new RuleFinding(
                    Id, Severity, Category, Title,
                    $"Type '{type.FullName}' has {type.InstanceCount} instances on the Large Object Heap " +
                    $"({type.TotalBytes:N0} bytes) — consider using ArrayPool<T> or reducing allocation size below 85KB",
                    new RuleEvidence(type.FullName, type.TotalBytes, type.InstanceCount, null),
                    null));
            }
        }

        return Task.FromResult<IReadOnlyList<RuleFinding>>(findings);
    }
}
