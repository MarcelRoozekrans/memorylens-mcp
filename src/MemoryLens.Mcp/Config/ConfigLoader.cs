using System.Text.Json;

namespace MemoryLens.Mcp.Config;

public static class ConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static MemoryLensConfig Parse(string json)
    {
        return JsonSerializer.Deserialize<MemoryLensConfig>(json, JsonOptions) ?? new MemoryLensConfig();
    }

    public static MemoryLensConfig LoadFromPath(string path)
    {
        if (!File.Exists(path))
            return new MemoryLensConfig();

        var json = File.ReadAllText(path);
        return Parse(json);
    }
}
