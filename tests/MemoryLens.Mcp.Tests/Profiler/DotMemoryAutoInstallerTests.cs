using MemoryLens.Mcp.Profiler;
using Xunit;

namespace MemoryLens.Mcp.Tests.Profiler;

public class DotMemoryAutoInstallerTests
{
    [Fact]
    public void GetRid_ReturnsNonNull_OnCurrentPlatform()
    {
        var rid = DotMemoryAutoInstaller.GetRid();
        Assert.NotNull(rid);
    }

    [Theory]
    [InlineData(false, false, "linux-x64")]
    [InlineData(false, true, "linux-musl-x64")]
    [InlineData(true, false, "linux-arm64")]
    [InlineData(true, true, "linux-musl-arm64")]
    public void BuildLinuxRid_ReturnsCorrectSuffix(bool isArm64, bool isMusl, string expected)
    {
        Assert.Equal(expected, DotMemoryAutoInstaller.BuildLinuxRid(isArm64, isMusl));
    }

    [Fact]
    public void GetUnsupportedPlatformMessage_ReturnsNull_OnCurrentPlatform()
    {
        var http = new System.Net.Http.HttpClient();
        var installer = new DotMemoryAutoInstaller(http);
        Assert.Null(installer.GetUnsupportedPlatformMessage());
    }

    [Fact]
    public async Task GetCachedPath_ReturnsNull_WhenNoCacheDir()
    {
        var installer = new DotMemoryAutoInstaller(
            new System.Net.Http.HttpClient(),
            cacheRoot: Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var result = await installer.GetCachedPathAsync(TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetCachedPath_ReturnsNull_WhenCurrentTxtMissing()
    {
        var cacheRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cacheRoot);
        try
        {
            var installer = new DotMemoryAutoInstaller(new System.Net.Http.HttpClient(), cacheRoot);
            var result = await installer.GetCachedPathAsync(TestContext.Current.CancellationToken);
            Assert.Null(result);
        }
        finally { Directory.Delete(cacheRoot, recursive: true); }
    }

    [Fact]
    public async Task GetCachedPath_ReturnsPath_WhenExecutableExists()
    {
        var cacheRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var versionDir = Path.Combine(cacheRoot, "2026.1.0");
        var toolsDir = Path.Combine(versionDir, "tools");
        Directory.CreateDirectory(toolsDir);

        var exeName = OperatingSystem.IsWindows() ? "dotMemory.exe" : "dotMemory.sh";
        var exePath = Path.Combine(toolsDir, exeName);
        File.WriteAllText(exePath, "fake");
        await File.WriteAllTextAsync(Path.Combine(cacheRoot, "current.txt"), "2026.1.0");

        try
        {
            var installer = new DotMemoryAutoInstaller(new System.Net.Http.HttpClient(), cacheRoot);
            var result = await installer.GetCachedPathAsync(TestContext.Current.CancellationToken);
            Assert.Equal(exePath, result);
        }
        finally { Directory.Delete(cacheRoot, recursive: true); }
    }

    [Fact]
    public async Task FetchLatestVersion_ReturnsLastVersion_FromJson()
    {
        var json = """{"versions":["2025.3.0","2026.1.0"]}""";
        var http = new HttpClient(new FakeHttpMessageHandler(json));
        var installer = new DotMemoryAutoInstaller(http, Path.GetTempPath());

        var version = await installer.FetchLatestVersionAsync(
            "jetbrains.dotmemory.console.windows-x64", TestContext.Current.CancellationToken);

        Assert.Equal("2026.1.0", version);
    }
}
