namespace MemoryLens.Mcp.Rules;

public record CodeSuggestion(string File, int Line, string Old, string New);
