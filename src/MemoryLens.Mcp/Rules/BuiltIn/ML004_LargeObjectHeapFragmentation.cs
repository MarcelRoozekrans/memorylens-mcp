namespace MemoryLens.Mcp.Rules.BuiltIn;

public class ML004_LargeObjectHeapFragmentation : IRule
{
    public string Id => "ML004";
    public string Title => "Large Object Heap fragmentation";
    public string Severity => "high";
    public string Category => "fragmentation";

    public Task<IReadOnlyList<RuleFinding>> EvaluateAsync(SnapshotAnalysisContext context, CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<RuleFinding>>([]);
    }
}
