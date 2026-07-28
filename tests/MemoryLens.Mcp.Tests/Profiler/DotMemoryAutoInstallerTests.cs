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

    public static TheoryData<string, byte[], bool> ExecutableHeaders => new()
    {
        // Shebang scripts — dotMemory.sh and the runtime-dotnet.sh it execs.
        { "runtime-dotnet.sh", "#!/bin/sh\nexit 0\n"u8.ToArray(), true },
        // Native ELF helper with a dotted name, which a filename rule misses.
        { "JetBrains.Profiler.PdbServer", [0x7F, 0x45, 0x4C, 0x46, 0x02, 0x01], true },
        // Mach-O 64-bit, for the macOS packages.
        { "dotmemory", [0xCF, 0xFA, 0xED, 0xFE, 0x0C, 0x00], true },
        // Managed assembly: "MZ" header, launched through the host.
        { "JetBrains.Lifetimes.dll", [0x4D, 0x5A, 0x90, 0x00], false },
        { "dotmemory_clt_license.md", "# License\n"u8.ToArray(), false },
        { "linux-x64.rel.Common.symref", "abc123"u8.ToArray(), false },
        { "empty-file", [], false },
    };

    [Theory]
    [MemberData(nameof(ExecutableHeaders))]
    public void NeedsExecuteBit_DecidesFromFileHeader_NotFileName(
        string fileName, byte[] contents, bool expected)
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        File.WriteAllBytes(path, contents);

        try
        {
            Assert.Equal(expected, DotMemoryAutoInstaller.NeedsExecuteBit(path));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>
    /// Guards the defect this replaced: only the entry point was chmodded, so the
    /// runtime-dotnet.sh it execs failed with "Permission denied" on Linux and macOS.
    /// </summary>
    [Fact]
    public void MakeToolsExecutable_SetsBitOnNestedScripts_NotJustTheEntryPoint()
    {
        if (OperatingSystem.IsWindows())
            return; // Unix file modes are not meaningful here.

        var versionDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var toolsDir = Path.Combine(versionDir, "tools");
        var nativeDir = Path.Combine(toolsDir, "linux-x64");
        Directory.CreateDirectory(nativeDir);

        var entryPoint = Path.Combine(toolsDir, "dotMemory.sh");
        var chainedScript = Path.Combine(toolsDir, "runtime-dotnet.sh");
        var nativeHelper = Path.Combine(nativeDir, "JetBrains.Profiler.PdbServer");
        var managedAssembly = Path.Combine(toolsDir, "JetBrains.Lifetimes.dll");

        File.WriteAllText(entryPoint, "#!/bin/sh\nexec ./runtime-dotnet.sh\n");
        File.WriteAllText(chainedScript, "#!/bin/sh\nexit 0\n");
        File.WriteAllBytes(nativeHelper, [0x7F, 0x45, 0x4C, 0x46, 0x02, 0x01]);
        File.WriteAllBytes(managedAssembly, [0x4D, 0x5A, 0x90, 0x00]);

        foreach (var path in new[] { entryPoint, chainedScript, nativeHelper, managedAssembly })
        {
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite |
                UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        }

        try
        {
            DotMemoryAutoInstaller.MakeToolsExecutable(versionDir);

            Assert.True(File.GetUnixFileMode(entryPoint).HasFlag(UnixFileMode.UserExecute));
            Assert.True(File.GetUnixFileMode(chainedScript).HasFlag(UnixFileMode.UserExecute));
            Assert.True(File.GetUnixFileMode(nativeHelper).HasFlag(UnixFileMode.UserExecute));
            Assert.False(File.GetUnixFileMode(managedAssembly).HasFlag(UnixFileMode.UserExecute));
        }
        finally { Directory.Delete(versionDir, recursive: true); }
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
