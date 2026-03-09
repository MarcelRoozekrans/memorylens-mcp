namespace MemoryLens.Mcp.Rules.BuiltIn;

public class ML007_ClosureRetainingReferences : IRule
{
    public string Id => "ML007";
    public string Title => "Closure retaining unexpected references";
    public string Severity => "medium";
    public string Category => "retention";

    public Task<IReadOnlyList<RuleFinding>> EvaluateAsync(SnapshotAnalysisContext context, CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<RuleFinding>>([]);
    }
}
