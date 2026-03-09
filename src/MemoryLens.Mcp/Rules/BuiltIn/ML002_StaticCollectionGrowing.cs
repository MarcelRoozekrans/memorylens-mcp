namespace MemoryLens.Mcp.Rules.BuiltIn;

public class ML002_StaticCollectionGrowing : IRule
{
    public string Id => "ML002";
    public string Title => "Static collection growing unbounded";
    public string Severity => "critical";
    public string Category => "leak";

    private static readonly string[] CollectionPatterns =
    [
        "System.Collections.Generic.List`",
        "System.Collections.Generic.Dictionary`",
        "System.Collections.Generic.HashSet`",
        "System.Collections.Generic.Queue`",
        "System.Collections.Generic.Stack`",
        "System.Collections.Generic.LinkedList`",
        "System.Collections.Generic.SortedDictionary`",
        "System.Collections.Generic.SortedSet`",
        "System.Collections.Concurrent.ConcurrentDictionary`",
        "System.Collections.Concurrent.ConcurrentQueue`",
        "System.Collections.Concurrent.ConcurrentBag`",
        "System.Collections.ArrayList",
        "System.Collections.Hashtable",
    ];

    private const double GrowthRatioThreshold = 1.5;
    private const int MinInstancesForSingleSnapshot = 100;

    public Task<IReadOnlyList<RuleFinding>> EvaluateAsync(SnapshotAnalysisContext context, CancellationToken ct = default)
    {
        var findings = new List<RuleFinding>();

        if (context.IsComparison && context.Comparison is not null)
        {
            foreach (var delta in context.Comparison.Deltas)
            {
                if (!IsCollectionType(delta.FullName))
                    continue;

                if (delta.InstancesBefore > 0 && delta.InstancesAfter > delta.InstancesBefore)
                {
                    var ratio = (double)delta.InstancesAfter / delta.InstancesBefore;
                    if (ratio >= GrowthRatioThreshold)
                    {
                        findings.Add(new RuleFinding(
                            Id, Severity, Category, Title,
                            $"Collection '{delta.FullName}' grew from {delta.InstancesBefore} to {delta.InstancesAfter} instances " +
                            $"({ratio:F1}x, +{delta.BytesDelta:N0} bytes) — may indicate unbounded static collection growth",
                            new RuleEvidence(delta.FullName, delta.BytesAfter, delta.InstancesAfter, null),
                            null));
                    }
                }
            }
        }
        else if (context.Data is not null)
        {
            // Single snapshot: flag collections with unusually high instance counts
            foreach (var type in context.Data.Types)
            {
                if (!IsCollectionType(type.FullName))
                    continue;

                if (type.InstanceCount > MinInstancesForSingleSnapshot && type.TotalBytes > 1_048_576)
                {
                    findings.Add(new RuleFinding(
                        Id, Severity, Category, Title,
                        $"Found {type.InstanceCount} instances of '{type.FullName}' consuming {type.TotalBytes:N0} bytes " +
                        $"— review if any are static and growing unbounded",
                        new RuleEvidence(type.FullName, type.TotalBytes, type.InstanceCount, null),
                        null));
                }
            }
        }

        return Task.FromResult<IReadOnlyList<RuleFinding>>(findings);
    }

    private static bool IsCollectionType(string typeName) =>
        CollectionPatterns.Any(p => typeName.Contains(p, StringComparison.Ordinal));
}
