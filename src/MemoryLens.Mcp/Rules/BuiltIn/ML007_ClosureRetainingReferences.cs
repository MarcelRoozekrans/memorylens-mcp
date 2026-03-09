using System.Text.RegularExpressions;

namespace MemoryLens.Mcp.Rules.BuiltIn;

public partial class ML007_ClosureRetainingReferences : IRule
{
    public string Id => "ML007";
    public string Title => "Closure retaining unexpected references";
    public string Severity => "medium";
    public string Category => "retention";

    private const int ClosureInstanceThreshold = 50;
    private const long ClosureByteThreshold = 102_400; // 100KB

    // Compiler-generated closure class patterns
    [GeneratedRegex(@"<>c__DisplayClass|<.*>d__\d+|<>c\b", RegexOptions.NonBacktracking)]
    private static partial Regex ClosureTypePattern();

    public Task<IReadOnlyList<RuleFinding>> EvaluateAsync(SnapshotAnalysisContext context, CancellationToken ct = default)
    {
        var findings = new List<RuleFinding>();

        if (context.IsComparison && context.Comparison is not null)
        {
            foreach (var delta in context.Comparison.Deltas)
            {
                if (!IsClosureType(delta.FullName))
                    continue;

                if (delta.InstanceDelta > 0 && delta.InstancesAfter > ClosureInstanceThreshold)
                {
                    findings.Add(CreateFinding(delta.FullName, delta.InstancesAfter, delta.BytesAfter,
                        $"Compiler-generated closure '{delta.FullName}' grew by {delta.InstanceDelta} instances " +
                        $"(now {delta.InstancesAfter}, {delta.BytesAfter:N0} bytes) — captured variables may hold " +
                        $"references longer than expected"));
                }
            }
        }
        else if (context.Data is not null)
        {
            foreach (var type in context.Data.Types)
            {
                if (!IsClosureType(type.FullName))
                    continue;

                if (type.InstanceCount > ClosureInstanceThreshold && type.TotalBytes > ClosureByteThreshold)
                {
                    findings.Add(CreateFinding(type.FullName, type.InstanceCount, type.TotalBytes,
                        $"Found {type.InstanceCount} instances of closure type '{type.FullName}' " +
                        $"({type.TotalBytes:N0} bytes) — lambdas/closures may be capturing and retaining " +
                        $"variables unnecessarily"));
                }
            }
        }

        return Task.FromResult<IReadOnlyList<RuleFinding>>(findings);
    }

    private static bool IsClosureType(string typeName) =>
        ClosureTypePattern().IsMatch(typeName);

    private RuleFinding CreateFinding(string typeName, int count, long bytes, string description) =>
        new(Id, Severity, Category, Title, description,
            new RuleEvidence(typeName, bytes, count, null), null);
}
