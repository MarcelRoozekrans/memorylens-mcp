namespace MemoryLens.Mcp.Rules.BuiltIn;

public class ML010_StringInterningOpportunity : IRule
{
    public string Id => "ML010";
    public string Title => "String interning opportunity";
    public string Severity => "low";
    public string Category => "pattern";

    // High string counts with disproportionate memory usage suggest duplicate strings
    private const int StringCountThreshold = 10_000;
    private const long StringByteThreshold = 5_242_880; // 5MB
    private const int AverageStringBytesThreshold = 200; // Low avg size = many small duplicates

    public Task<IReadOnlyList<RuleFinding>> EvaluateAsync(SnapshotAnalysisContext context, CancellationToken ct = default)
    {
        var findings = new List<RuleFinding>();

        if (context.Data is null)
            return Task.FromResult<IReadOnlyList<RuleFinding>>(findings);

        var stringType = context.Data.Types
            .FirstOrDefault(t => string.Equals(t.FullName, "System.String", StringComparison.Ordinal));

        if (stringType is null)
            return Task.FromResult<IReadOnlyList<RuleFinding>>(findings);

        if (stringType.InstanceCount > StringCountThreshold && stringType.TotalBytes > StringByteThreshold)
        {
            var avgBytes = stringType.TotalBytes / stringType.InstanceCount;

            // Many small strings suggest duplication that could benefit from interning
            if (avgBytes < AverageStringBytesThreshold)
            {
                findings.Add(new RuleFinding(
                    Id, Severity, Category, Title,
                    $"Found {stringType.InstanceCount:N0} string instances consuming {stringType.TotalBytes:N0} bytes " +
                    $"(avg {avgBytes} bytes/string) — many small strings may indicate duplicates that " +
                    $"could benefit from String.Intern() or a StringPool",
                    new RuleEvidence("System.String", stringType.TotalBytes, stringType.InstanceCount, null),
                    null));
            }
        }

        return Task.FromResult<IReadOnlyList<RuleFinding>>(findings);
    }
}
