namespace MemoryLens.Mcp.Rules.BuiltIn;

public class ML006_ExcessiveAllocations : IRule
{
    public string Id => "ML006";
    public string Title => "Excessive allocations in hot path";
    public string Severity => "medium";
    public string Category => "allocation";

    private const int InstanceCountThreshold = 10_000;

    // Types where high allocation counts indicate boxing or string concat issues
    private static readonly string[] BoxingIndicators =
    [
        "System.Int32",
        "System.Int64",
        "System.Double",
        "System.Boolean",
        "System.Byte",
        "System.Char",
        "System.Single",
        "System.Decimal",
    ];

    public Task<IReadOnlyList<RuleFinding>> EvaluateAsync(SnapshotAnalysisContext context, CancellationToken ct = default)
    {
        var findings = new List<RuleFinding>();

        if (context.IsComparison && context.Comparison is not null)
        {
            // In comparison mode: look for types with rapid allocation growth
            foreach (var delta in context.Comparison.Deltas)
            {
                if (delta.InstanceDelta <= InstanceCountThreshold)
                    continue;

                var suggestion = GetSuggestion(delta.FullName);
                findings.Add(new RuleFinding(
                    Id, Severity, Category, Title,
                    $"Type '{delta.FullName}' allocated {delta.InstanceDelta:N0} additional instances " +
                    $"(+{delta.BytesDelta:N0} bytes) between snapshots{suggestion}",
                    new RuleEvidence(delta.FullName, delta.BytesAfter, delta.InstancesAfter, null),
                    null));
            }
        }
        else if (context.Data is not null)
        {
            foreach (var type in context.Data.Types)
            {
                if (type.InstanceCount <= InstanceCountThreshold)
                    continue;

                // Skip system internals
                if (type.FullName.StartsWith("System.Runtime", StringComparison.Ordinal))
                    continue;

                var suggestion = GetSuggestion(type.FullName);
                findings.Add(new RuleFinding(
                    Id, Severity, Category, Title,
                    $"Type '{type.FullName}' has {type.InstanceCount:N0} instances " +
                    $"({type.TotalBytes:N0} bytes){suggestion}",
                    new RuleEvidence(type.FullName, type.TotalBytes, type.InstanceCount, null),
                    null));
            }
        }

        return Task.FromResult<IReadOnlyList<RuleFinding>>(findings);
    }

    private static string GetSuggestion(string typeName)
    {
        if (BoxingIndicators.Any(b => string.Equals(typeName, b, StringComparison.Ordinal)))
            return " — this may indicate boxing in hot paths; consider using generics to avoid boxing";

        if (string.Equals(typeName, "System.String", StringComparison.Ordinal))
            return " — consider using StringBuilder, string interpolation, or StringPool for high-frequency string operations";

        return " — review allocation patterns in hot code paths";
    }
}
