using MemoryLens.Mcp.Profiler;
using MemoryLens.Mcp.TestSupport;
using Xunit;

namespace MemoryLens.Mcp.IntegrationTests;

/// <summary>
/// Regression guard for #118. ZipFile.ExtractToDirectory discards Unix permission
/// bits, so everything lands 0644; the installer must restore the execute bit on
/// every file the OS has to exec, decided by leading bytes rather than by name.
/// </summary>
public class ExecuteBitTests
{
    private static bool IsExecutable(string path) =>
        File.GetUnixFileMode(path).HasFlag(UnixFileMode.UserExecute);

    [Fact]
    public void Extraction_LandsEverythingNonExecutable()
    {
        if (OperatingSystem.IsWindows())
            return; // Unix file modes are not meaningful here.

        using var dir = new TempDir();
        PackageFixtureBuilder.ExtractSampleTo(dir.Path);

        // This is the bug's premise: without intervention, nothing is executable.
        Assert.False(IsExecutable(Path.Combine(dir.Path, PackageFixtureBuilder.ShebangEntry)));
        Assert.False(IsExecutable(Path.Combine(dir.Path, PackageFixtureBuilder.ElfEntry)));
    }

    [Fact]
    public void MakeToolsExecutable_SetsBitOnEveryExecFormat()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var dir = new TempDir();
        PackageFixtureBuilder.ExtractSampleTo(dir.Path);

        DotMemoryAutoInstaller.MakeToolsExecutable(dir.Path);

        Assert.True(IsExecutable(Path.Combine(dir.Path, PackageFixtureBuilder.ShebangEntry)));
        Assert.True(IsExecutable(Path.Combine(dir.Path, PackageFixtureBuilder.NestedShebangEntry)));
        Assert.True(IsExecutable(Path.Combine(dir.Path, PackageFixtureBuilder.ElfEntry)));
        Assert.True(IsExecutable(Path.Combine(dir.Path, PackageFixtureBuilder.MachOEntry)));
    }

    [Fact]
    public void MakeToolsExecutable_LeavesNonExecFormatsAlone()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var dir = new TempDir();
        PackageFixtureBuilder.ExtractSampleTo(dir.Path);

        DotMemoryAutoInstaller.MakeToolsExecutable(dir.Path);

        Assert.False(IsExecutable(Path.Combine(dir.Path, PackageFixtureBuilder.ManagedEntry)));
        Assert.False(IsExecutable(Path.Combine(dir.Path, PackageFixtureBuilder.TextEntry)));
    }

    [Fact]
    public void MakeToolsExecutable_IsIdempotent()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var dir = new TempDir();
        PackageFixtureBuilder.ExtractSampleTo(dir.Path);

        DotMemoryAutoInstaller.MakeToolsExecutable(dir.Path);
        DotMemoryAutoInstaller.MakeToolsExecutable(dir.Path); // cache-hit path

        Assert.True(IsExecutable(Path.Combine(dir.Path, PackageFixtureBuilder.NestedShebangEntry)));
        Assert.False(IsExecutable(Path.Combine(dir.Path, PackageFixtureBuilder.TextEntry)));
    }

    /// <summary>
    /// The tests above call MakeToolsExecutable directly, which proves the helper works
    /// but not that anything calls it. Delete the call inside GetCachedPathAsync and they
    /// all still pass while #118 fully regresses on every cache hit. This drives the real
    /// resolution path instead: seed a cache the way a pre-#118 extraction left it (a zip
    /// unpacked through ZipFile.ExtractToDirectory, so 0644 throughout), then resolve it
    /// and require that resolution itself restored the second-hop script's execute bit.
    /// </summary>
    [Fact]
    public async Task GetCachedPathAsync_RestoresExecuteBitsOnTheCacheHitPath()
    {
        if (OperatingSystem.IsWindows())
            return; // No execute-bit concept; the chmod is a no-op there by design.

        using var cacheRoot = new TempDir();

        const string version = "2025.1.4";
        var versionDir = Path.Combine(cacheRoot.Path, version);
        PackageFixtureBuilder.ExtractSampleTo(versionDir);

        // How the installer records which extracted version is current.
        await File.WriteAllTextAsync(
            Path.Combine(cacheRoot.Path, "current.txt"), version,
            TestContext.Current.CancellationToken);

        var nested = Path.Combine(versionDir, PackageFixtureBuilder.NestedShebangEntry);
        Assert.False(IsExecutable(nested)); // the bug's premise, before resolution runs

        using var httpClient = new HttpClient();
        var installer = new DotMemoryAutoInstaller(httpClient, cacheRoot.Path);

        var resolved = await installer.GetCachedPathAsync(TestContext.Current.CancellationToken);

        // The fixture must actually resolve, or the assertion below would be vacuous.
        Assert.Equal(Path.Combine(versionDir, PackageFixtureBuilder.ShebangEntry), resolved);

        // #118: the entry point resolving is not enough -- the script it execs must run too.
        Assert.True(IsExecutable(nested));
    }

    [Fact]
    public void NeedsExecuteBit_ClassifiesByLeadingBytes_NotByName()
    {
        using var dir = new TempDir();
        PackageFixtureBuilder.ExtractSampleTo(dir.Path);

        // Runs on every OS: this is pure byte inspection, no file modes involved.
        Assert.True(DotMemoryAutoInstaller.NeedsExecuteBit(
            Path.Combine(dir.Path, PackageFixtureBuilder.ElfEntry)));
        Assert.True(DotMemoryAutoInstaller.NeedsExecuteBit(
            Path.Combine(dir.Path, PackageFixtureBuilder.MachOEntry)));
        Assert.False(DotMemoryAutoInstaller.NeedsExecuteBit(
            Path.Combine(dir.Path, PackageFixtureBuilder.ManagedEntry)));
        Assert.False(DotMemoryAutoInstaller.NeedsExecuteBit(
            Path.Combine(dir.Path, PackageFixtureBuilder.TextEntry)));
    }
}
