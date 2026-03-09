namespace MemoryLens.Mcp.Config;

public class MemoryLensConfig
{
    public Dictionary<string, RuleOverride> Rules { get; set; } = new();
    public List<string> Ignore { get; set; } = [];
}

public class RuleOverride
{
    public string? Severity { get; set; }
    public bool Enabled { get; set; } = true;
    public Dictionary<string, object>? Threshold { get; set; }
}
