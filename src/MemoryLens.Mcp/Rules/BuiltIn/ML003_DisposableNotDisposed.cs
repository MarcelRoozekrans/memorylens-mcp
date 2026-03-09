namespace MemoryLens.Mcp.Rules.BuiltIn;

public class ML003_DisposableNotDisposed : IRule
{
    public string Id => "ML003";
    public string Title => "Disposable object not disposed";
    public string Severity => "high";
    public string Category => "leak";

    private const int InstanceThreshold = 10;
    private const long ByteThreshold = 102_400; // 100KB

    public Task<IReadOnlyList<RuleFinding>> EvaluateAsync(SnapshotAnalysisContext context, CancellationToken ct = default)
    {
        var findings = new List<RuleFinding>();

        if (context.IsComparison && context.Comparison is not null)
        {
            var afterTypesByName = context.Comparison.After.Types
                .ToDictionary(t => t.FullName, StringComparer.Ordinal);

            foreach (var delta in context.Comparison.Deltas)
            {
                if (!afterTypesByName.TryGetValue(delta.FullName, out var afterType) || !afterType.ImplementsIDisposable)
                    continue;

                if (delta.InstanceDelta > 0 && delta.InstancesAfter > InstanceThreshold)
                {
                    findings.Add(CreateFinding(delta.FullName, delta.InstancesAfter, delta.BytesAfter,
                        $"Disposable type '{delta.FullName}' grew by {delta.InstanceDelta} instances " +
                        $"(now {delta.InstancesAfter}) — objects may not be disposed properly"));
                }
            }
        }
        else if (context.Data is not null)
        {
            foreach (var type in context.Data.Types)
            {
                if (!type.ImplementsIDisposable)
                    continue;

                if (type.InstanceCount > InstanceThreshold && type.TotalBytes > ByteThreshold)
                {
                    findings.Add(CreateFinding(type.FullName, type.InstanceCount, type.TotalBytes,
                        $"Found {type.InstanceCount} live instances of disposable type '{type.FullName}' " +
                        $"({type.TotalBytes:N0} bytes) — ensure Dispose() is called"));
                }
            }
        }

        return Task.FromResult<IReadOnlyList<RuleFinding>>(findings);
    }

    private RuleFinding CreateFinding(string typeName, int count, long bytes, string description) =>
        new(Id, Severity, Category, Title, description,
            new RuleEvidence(typeName, bytes, count, null), null);
}
