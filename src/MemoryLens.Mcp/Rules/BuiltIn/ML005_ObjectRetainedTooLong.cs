namespace MemoryLens.Mcp.Rules.BuiltIn;

public class ML005_ObjectRetainedTooLong : IRule
{
    public string Id => "ML005";
    public string Title => "Object retained longer than expected";
    public string Severity => "medium";
    public string Category => "retention";

    public Task<IReadOnlyList<RuleFinding>> EvaluateAsync(SnapshotAnalysisContext context, CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<RuleFinding>>([]);
    }
}
