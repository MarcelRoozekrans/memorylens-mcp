#pragma warning disable MA0048 // File name must match type name - intentional companion types
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
