namespace MemoryLens.Mcp.Rules.BuiltIn;

public class ML008_ArrayResizingWithoutCapacity : IRule
{
    public string Id => "ML008";
    public string Title => "Array/list resizing without capacity hint";
    public string Severity => "low";
    public string Category => "allocation";

    public Task<IReadOnlyList<RuleFinding>> EvaluateAsync(SnapshotAnalysisContext context, CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<RuleFinding>>([]);
    }
}
