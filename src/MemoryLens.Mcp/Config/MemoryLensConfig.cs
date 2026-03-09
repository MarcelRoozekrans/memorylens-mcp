#pragma warning disable MA0048 // File name must match type name - intentional companion types
namespace MemoryLens.Mcp.Config;

public class MemoryLensConfig
{
    public IDictionary<string, RuleOverride> Rules { get; set; } = new Dictionary<string, RuleOverride>(StringComparer.Ordinal);
    public IList<string> Ignore { get; set; } = [];
}

public class RuleOverride
{
    public string? Severity { get; set; }
    public bool Enabled { get; set; } = true;
    public IDictionary<string, object>? Threshold { get; set; }
}
