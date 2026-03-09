namespace MemoryLens.Mcp.Rules.BuiltIn;

public class ML002_StaticCollectionGrowing : IRule
{
    public string Id => "ML002";
    public string Title => "Static collection growing unbounded";
    public string Severity => "critical";
    public string Category => "leak";

    public Task<IReadOnlyList<RuleFinding>> EvaluateAsync(SnapshotAnalysisContext context, CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<RuleFinding>>([]);
    }
}
