namespace MemoryLens.Mcp.Rules.BuiltIn;

public class ML005_ObjectRetainedTooLong : IRule
{
    public string Id => "ML005";
    public string Title => "Object retained longer than expected";
    public string Severity => "medium";
    public string Category => "retention";

    private const int Gen2InstanceThreshold = 50;
    private const long Gen2ByteThreshold = 524_288; // 512KB

    // Types that are expected to be long-lived and should not trigger this rule
    private static readonly string[] ExpectedLongLivedPrefixes =
    [
        "System.RuntimeType",
        "System.Reflection.",
        "System.String",
        "System.Globalization.",
        "System.Threading.Thread",
    ];

    public Task<IReadOnlyList<RuleFinding>> EvaluateAsync(SnapshotAnalysisContext context, CancellationToken ct = default)
    {
        var findings = new List<RuleFinding>();

        if (context.Data is null)
            return Task.FromResult<IReadOnlyList<RuleFinding>>(findings);

        foreach (var type in context.Data.Types)
        {
            if (type.DominantGeneration < 2)
                continue;

            if (IsExpectedLongLived(type.FullName))
                continue;

            if (type.InstanceCount > Gen2InstanceThreshold && type.TotalBytes > Gen2ByteThreshold)
            {
                findings.Add(new RuleFinding(
                    Id, Severity, Category, Title,
                    $"Type '{type.FullName}' has {type.InstanceCount} instances in Gen2 " +
                    $"({type.TotalBytes:N0} bytes) — these objects survived multiple GC cycles and may be retained " +
                    $"longer than necessary",
                    new RuleEvidence(type.FullName, type.TotalBytes, type.InstanceCount, null),
                    null));
            }
        }

        return Task.FromResult<IReadOnlyList<RuleFinding>>(findings);
    }

    private static bool IsExpectedLongLived(string typeName) =>
        ExpectedLongLivedPrefixes.Any(p => typeName.StartsWith(p, StringComparison.Ordinal));
}
