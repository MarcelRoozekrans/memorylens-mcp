namespace MemoryLens.Mcp.Rules.BuiltIn;

public class ML001_EventHandlerLeak : IRule
{
    public string Id => "ML001";
    public string Title => "Event handler leak detected";
    public string Severity => "critical";
    public string Category => "leak";

    public Task<IReadOnlyList<RuleFinding>> EvaluateAsync(SnapshotAnalysisContext context, CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<RuleFinding>>([]);
    }
}
