namespace MemoryLens.Mcp.Rules.BuiltIn;

public class ML003_DisposableNotDisposed : IRule
{
    public string Id => "ML003";
    public string Title => "Disposable object not disposed";
    public string Severity => "high";
    public string Category => "leak";

    public Task<IReadOnlyList<RuleFinding>> EvaluateAsync(SnapshotAnalysisContext context, CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<RuleFinding>>([]);
    }
}
