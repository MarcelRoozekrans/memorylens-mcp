namespace MemoryLens.Mcp.Rules.BuiltIn;

public class ML006_ExcessiveAllocations : IRule
{
    public string Id => "ML006";
    public string Title => "Excessive allocations in hot path";
    public string Severity => "medium";
    public string Category => "allocation";

    public Task<IReadOnlyList<RuleFinding>> EvaluateAsync(SnapshotAnalysisContext context, CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<RuleFinding>>([]);
    }
}
