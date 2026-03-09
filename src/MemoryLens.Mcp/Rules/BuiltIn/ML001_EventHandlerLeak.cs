namespace MemoryLens.Mcp.Rules.BuiltIn;

public class ML001_EventHandlerLeak : IRule
{
    public string Id => "ML001";
    public string Title => "Event handler leak detected";
    public string Severity => "critical";
    public string Category => "leak";

    // Delegate/EventHandler types that indicate potential event leaks when instance count is high
    private static readonly string[] EventHandlerPatterns =
    [
        "EventHandler",
        "Action`",
        "Func`",
        "System.Delegate",
    ];

    private const int SingleSnapshotThreshold = 50;

    public Task<IReadOnlyList<RuleFinding>> EvaluateAsync(SnapshotAnalysisContext context, CancellationToken ct = default)
    {
        var findings = new List<RuleFinding>();

        if (context.IsComparison && context.Comparison is not null)
        {
            // In comparison mode: look for delegate types that are growing
            foreach (var delta in context.Comparison.Deltas)
            {
                if (!IsEventHandlerType(delta.FullName))
                    continue;

                if (delta.InstanceDelta > 0 && delta.InstancesAfter > SingleSnapshotThreshold)
                {
                    findings.Add(new RuleFinding(
                        Id, Severity, Category, Title,
                        $"Delegate type '{delta.FullName}' grew from {delta.InstancesBefore} to {delta.InstancesAfter} instances " +
                        $"(+{delta.InstanceDelta}), suggesting event handlers are being subscribed but not unsubscribed",
                        new RuleEvidence(delta.FullName, delta.BytesAfter, delta.InstancesAfter, null),
                        null));
                }
            }
        }
        else if (context.Data is not null)
        {
            // In single snapshot mode: flag unusually high delegate counts
            foreach (var type in context.Data.Types)
            {
                if (!IsEventHandlerType(type.FullName))
                    continue;

                if (type.InstanceCount > SingleSnapshotThreshold)
                {
                    findings.Add(new RuleFinding(
                        Id, Severity, Category, Title,
                        $"Found {type.InstanceCount} instances of delegate type '{type.FullName}' " +
                        $"({type.TotalBytes:N0} bytes) — may indicate event handlers not being unsubscribed",
                        new RuleEvidence(type.FullName, type.TotalBytes, type.InstanceCount, null),
                        null));
                }
            }
        }

        return Task.FromResult<IReadOnlyList<RuleFinding>>(findings);
    }

    private static bool IsEventHandlerType(string typeName) =>
        EventHandlerPatterns.Any(p => typeName.Contains(p, StringComparison.Ordinal));
}
