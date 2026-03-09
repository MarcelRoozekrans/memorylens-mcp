namespace MemoryLens.Mcp.Rules.BuiltIn;

public class ML009_FinalizerWithoutDispose : IRule
{
    public string Id => "ML009";
    public string Title => "Finalizer without Dispose pattern";
    public string Severity => "low";
    public string Category => "pattern";

    public Task<IReadOnlyList<RuleFinding>> EvaluateAsync(SnapshotAnalysisContext context, CancellationToken ct = default)
    {
        var findings = new List<RuleFinding>();

        if (context.Data is null)
            return Task.FromResult<IReadOnlyList<RuleFinding>>(findings);

        foreach (var type in context.Data.Types)
        {
            if (!type.HasFinalizer)
                continue;

            // Types with finalizers but not implementing IDisposable
            if (!type.ImplementsIDisposable)
            {
                findings.Add(new RuleFinding(
                    Id, Severity, Category, Title,
                    $"Type '{type.FullName}' has a finalizer but does not implement IDisposable " +
                    $"({type.InstanceCount} instances, {type.TotalBytes:N0} bytes) — implement the Dispose pattern " +
                    $"to allow deterministic cleanup and suppress finalization",
                    new RuleEvidence(type.FullName, type.TotalBytes, type.InstanceCount, null),
                    null));
            }
        }

        return Task.FromResult<IReadOnlyList<RuleFinding>>(findings);
    }
}
