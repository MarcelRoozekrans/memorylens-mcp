using MemoryLens.Mcp.Config;
using MemoryLens.Mcp.TestSupport;
using Xunit;

namespace MemoryLens.Mcp.IntegrationTests;

/// <summary>
/// Program.cs reads .memorylens.json from the current working directory. Config
/// that silently fails to load is invisible -- the server starts fine and quietly
/// uses defaults -- so this round-trips a real file on a real filesystem.
/// </summary>
public class ConfigLoadingTests
{
    [Fact]
    public void LoadFromPath_MissingFile_ReturnsDefaultsRatherThanThrowing()
    {
        using var dir = new TempDir();
        var missing = Path.Combine(dir.Path, ".memorylens.json");

        var config = ConfigLoader.LoadFromPath(missing);

        // The server must start without a config file present.
        Assert.NotNull(config);
    }

    [Fact]
    public void LoadFromPath_RealFileOnDisk_IsActuallyApplied()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, ".memorylens.json");

        File.WriteAllText(path, """
            {
              "rules": {
                "ML001": { "severity": "info", "enabled": false }
              },
              "ignore": ["System.String"]
            }
            """);

        var config = ConfigLoader.LoadFromPath(path);

        Assert.True(config.Rules.ContainsKey("ML001"));

        // Enabled defaults to true, so false here can ONLY come from the file.
        Assert.False(config.Rules["ML001"].Enabled);
        Assert.Equal("info", config.Rules["ML001"].Severity);
        Assert.Contains("System.String", config.Ignore);
    }

    [Fact]
    public void LoadFromPath_DefaultsDifferFromTheFixture_SoTheTestCannotBeVacuous()
    {
        // Guards the test above: if defaults ever changed to match the fixture,
        // LoadFromPath_RealFileOnDisk_IsActuallyApplied would pass without reading
        // anything. This pins the defaults it relies on.
        var defaults = ConfigLoader.Parse("{}");

        Assert.Empty(defaults.Rules);
        Assert.Empty(defaults.Ignore);
        Assert.True(new RuleOverride().Enabled);
    }
}
