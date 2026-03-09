namespace MemoryLens.Mcp.Rules.BuiltIn;

public class ML010_StringInterningOpportunity : IRule
{
    public string Id => "ML010";
    public string Title => "String interning opportunity";
    public string Severity => "low";
    public string Category => "pattern";

    public Task<IReadOnlyList<RuleFinding>> EvaluateAsync(SnapshotAnalysisContext context, CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<RuleFinding>>([]);
    }
}
