namespace MemoryLens.Mcp.Rules.BuiltIn;

public class ML008_ArrayResizingWithoutCapacity : IRule
{
    public string Id => "ML008";
    public string Title => "Array/list resizing without capacity hint";
    public string Severity => "low";
    public string Category => "allocation";

    // High array instance counts with varying sizes suggest List<T> resizing
    private const int ArrayInstanceThreshold = 1_000;
    private const long ArrayByteThreshold = 1_048_576; // 1MB

    private static readonly string[] ResizableArrayPatterns =
    [
        "System.Object[]",
        "System.String[]",
        "System.Int32[]",
        "System.Byte[]",
    ];

    public Task<IReadOnlyList<RuleFinding>> EvaluateAsync(SnapshotAnalysisContext context, CancellationToken ct = default)
    {
        var findings = new List<RuleFinding>();

        if (context.Data is null)
            return Task.FromResult<IReadOnlyList<RuleFinding>>(findings);

        foreach (var type in context.Data.Types)
        {
            if (!IsResizableArray(type.FullName))
                continue;

            if (type.InstanceCount > ArrayInstanceThreshold && type.TotalBytes > ArrayByteThreshold)
            {
                findings.Add(new RuleFinding(
                    Id, Severity, Category, Title,
                    $"Found {type.InstanceCount:N0} instances of '{type.FullName}' ({type.TotalBytes:N0} bytes) " +
                    $"— many small arrays may indicate List<T>/Dictionary<K,V> resizing without initial capacity. " +
                    $"Consider providing capacity hints in constructors",
                    new RuleEvidence(type.FullName, type.TotalBytes, type.InstanceCount, null),
                    null));
            }
        }

        return Task.FromResult<IReadOnlyList<RuleFinding>>(findings);
    }

    private static bool IsResizableArray(string typeName) =>
        typeName.EndsWith("[]", StringComparison.Ordinal)
        || ResizableArrayPatterns.Any(p => string.Equals(typeName, p, StringComparison.Ordinal));
}
