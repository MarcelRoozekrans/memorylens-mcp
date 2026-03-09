namespace MemoryLens.Mcp.Rules;

public record RuleFinding(
    string RuleId,
    string Severity,
    string Category,
    string Title,
    string Description,
    RuleEvidence Evidence,
    CodeSuggestion? Suggestion);

public record RuleEvidence(
    string Type,
    long RetainedBytes,
    int InstanceCount,
    string? RetentionPath);
