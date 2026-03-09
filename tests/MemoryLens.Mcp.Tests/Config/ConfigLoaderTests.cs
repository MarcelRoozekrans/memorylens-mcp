using MemoryLens.Mcp.Config;
using Xunit;

namespace MemoryLens.Mcp.Tests.Config;

public class ConfigLoaderTests
{
    [Fact]
    public void Load_WithOverrides_MergesCorrectly()
    {
        var json = """
        {
            "rules": {
                "ML001": { "severity": "low", "enabled": true },
                "ML010": { "enabled": false }
            },
            "ignore": [ "System.*.dll" ]
        }
        """;

        var config = ConfigLoader.Parse(json);

        Assert.Equal(2, config.Rules.Count);
        Assert.True(config.Rules.ContainsKey("ML001"));
        Assert.Equal("low", config.Rules["ML001"].Severity);
        Assert.True(config.Rules["ML001"].Enabled);
        Assert.False(config.Rules["ML010"].Enabled);
        Assert.Single(config.Ignore);
        Assert.Equal("System.*.dll", config.Ignore[0]);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var config = ConfigLoader.LoadFromPath("/nonexistent/.memorylens.json");

        Assert.Empty(config.Rules);
        Assert.Empty(config.Ignore);
    }
}
