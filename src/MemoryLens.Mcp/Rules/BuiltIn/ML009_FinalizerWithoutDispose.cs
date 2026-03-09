namespace MemoryLens.Mcp.Rules.BuiltIn;

public class ML009_FinalizerWithoutDispose : IRule
{
    public string Id => "ML009";
    public string Title => "Finalizer without Dispose pattern";
    public string Severity => "low";
    public string Category => "pattern";

    public Task<IReadOnlyList<RuleFinding>> EvaluateAsync(SnapshotAnalysisContext context, CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<RuleFinding>>([]);
    }
}
