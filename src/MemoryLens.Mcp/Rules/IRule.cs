namespace MemoryLens.Mcp.Rules;

public interface IRule
{
    string Id { get; }
    string Title { get; }
    string Severity { get; }
    string Category { get; }
    Task<IReadOnlyList<RuleFinding>> EvaluateAsync(SnapshotAnalysisContext context, CancellationToken ct = default);
}
