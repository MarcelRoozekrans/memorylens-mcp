# dotMemory Auto-Install Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Automatically download and cache the correct `JetBrains.dotMemory.Console.*` NuGet package on first `ensure_dotmemory` call, so dotMemory works out of the box with zero user setup.

**Architecture:** A new `IDotMemoryAutoInstaller` / `DotMemoryAutoInstaller` class handles platform detection, NuGet download, ZIP extraction, and cache management under `~/.memorylens/tools/dotmemory/`. `DotMemoryToolManager` gains a new `ResolveAutoInstalledAsync` step (position: after DOTMEMORY_PATH, before PATH discovery) and calls `InstallLatestAsync` from `EnsureInstalledAsync` when the cache is empty.

**Tech Stack:** .NET 10, `System.IO.Compression.ZipFile`, `HttpClient`, `System.Runtime.InteropServices.RuntimeInformation`, xUnit

---

### Task 1: Interface + Fake

**Files:**
- Create: `src/MemoryLens.Mcp/Profiler/IDotMemoryAutoInstaller.cs`
- Create: `tests/MemoryLens.Mcp.Tests/Profiler/FakeDotMemoryAutoInstaller.cs`

**Step 1: Create the interface**

```csharp
// src/MemoryLens.Mcp/Profiler/IDotMemoryAutoInstaller.cs
namespace MemoryLens.Mcp.Profiler;

public interface IDotMemoryAutoInstaller
{
    /// <summary>Returns the path to the cached dotMemory executable, or null if not cached.</summary>
    Task<string?> GetCachedPathAsync(CancellationToken ct);

    /// <summary>Downloads and caches the latest dotMemory for the current platform.
    /// Returns the executable path on success, null on failure (network error, unsupported platform).</summary>
    Task<string?> InstallLatestAsync(CancellationToken ct);

    /// <summary>Returns a human-readable message if the current platform is not supported by
    /// JetBrains dotMemory Console, or null if the platform is supported.</summary>
    string? GetUnsupportedPlatformMessage();
}
```

**Step 2: Create the fake**

```csharp
// tests/MemoryLens.Mcp.Tests/Profiler/FakeDotMemoryAutoInstaller.cs
namespace MemoryLens.Mcp.Tests.Profiler;

public class FakeDotMemoryAutoInstaller(
    string? cachedPath = null,
    string? installPath = null,
    string? unsupportedMessage = null) : IDotMemoryAutoInstaller
{
    public Task<string?> GetCachedPathAsync(CancellationToken ct) =>
        Task.FromResult(cachedPath);

    public Task<string?> InstallLatestAsync(CancellationToken ct) =>
        Task.FromResult(installPath);

    public string? GetUnsupportedPlatformMessage() => unsupportedMessage;
}
```

**Step 3: Build to verify no compile errors**

```bash
dotnet build src/MemoryLens.Mcp/MemoryLens.Mcp.csproj -c Release --nologo -q
dotnet build tests/MemoryLens.Mcp.Tests/MemoryLens.Mcp.Tests.csproj -c Release --nologo -q
```

Expected: no errors.

**Step 4: Commit**

```bash
git add src/MemoryLens.Mcp/Profiler/IDotMemoryAutoInstaller.cs
git add tests/MemoryLens.Mcp.Tests/Profiler/FakeDotMemoryAutoInstaller.cs
git commit -m "feat: add IDotMemoryAutoInstaller interface and fake"
```

---

### Task 2: Wire IDotMemoryAutoInstaller into DotMemoryToolManager

**Files:**
- Modify: `src/MemoryLens.Mcp/Profiler/DotMemoryToolManager.cs`
- Modify: `src/MemoryLens.Mcp/Program.cs`
- Modify: `tests/MemoryLens.Mcp.Tests/Profiler/DotMemoryToolManagerTests.cs`

**Step 1: Write failing tests for new wiring**

Add to `DotMemoryToolManagerTests.cs`:

```csharp
[Fact]
public async Task ResolveCommand_ReturnsAutoInstalled_WhenCacheHit()
{
    var tempFile = Path.GetTempFileName();
    try
    {
        var autoInstaller = new FakeDotMemoryAutoInstaller(cachedPath: tempFile);
        var manager = new DotMemoryToolManager(new FakeProcessRunner(exitCode: 1, output: ""), autoInstaller);

        var command = await manager.ResolveCommandAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(command);
        Assert.Equal(tempFile, command.FileName);
        Assert.Equal(DotMemoryCommandKind.AutoInstalled, command.Kind);
    }
    finally
    {
        File.Delete(tempFile);
    }
}

[Fact]
public async Task EnsureInstalled_CallsInstallLatest_WhenCacheMiss()
{
    var tempFile = Path.GetTempFileName();
    try
    {
        var autoInstaller = new FakeDotMemoryAutoInstaller(cachedPath: null, installPath: tempFile);
        var manager = new DotMemoryToolManager(new FakeProcessRunner(exitCode: 1, output: ""), autoInstaller);

        var result = await manager.EnsureInstalledAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsInstalled);
        Assert.Equal(DotMemoryCommandKind.AutoInstalled, result.Kind);
    }
    finally
    {
        File.Delete(tempFile);
    }
}

[Fact]
public async Task EnsureInstalled_FallsThrough_WhenInstallLatestFails()
{
    var autoInstaller = new FakeDotMemoryAutoInstaller(cachedPath: null, installPath: null);
    var runner = new FakeProcessRunner(exitCode: 1, output: "");
    var manager = new DotMemoryToolManager(runner, autoInstaller);

    var result = await manager.EnsureInstalledAsync(TestContext.Current.CancellationToken);

    Assert.False(result.IsInstalled);
}

[Fact]
public async Task EnsureInstalled_ReturnsUnsupportedMessage_WhenPlatformNotSupported()
{
    var autoInstaller = new FakeDotMemoryAutoInstaller(
        unsupportedMessage: "Platform freebsd-x64 is not supported.");
    var runner = new FakeProcessRunner(exitCode: 1, output: "");
    var manager = new DotMemoryToolManager(runner, autoInstaller);

    var result = await manager.EnsureInstalledAsync(TestContext.Current.CancellationToken);

    Assert.False(result.IsInstalled);
    Assert.Contains("freebsd-x64", result.Message);
}
```

Note: `ToolStatus` needs a `Kind` property — add `DotMemoryCommandKind? Kind` to the record.

**Step 2: Run tests to verify they fail**

```bash
dotnet test tests/MemoryLens.Mcp.Tests -c Release --nologo -q
```

Expected: compilation errors / failures on the new tests.

**Step 3: Add `DotMemoryCommandKind.AutoInstalled` to the enum**

In `DotMemoryToolManager.cs`, add `AutoInstalled` to the enum:

```csharp
public enum DotMemoryCommandKind
{
    AutoInstalled,   // add this
    ExplicitPath,
    PathDiscovery,
    LocalTool,
    GlobalTool
}
```

**Step 4: Add `Kind` to `ToolStatus`**

```csharp
public record ToolStatus(bool IsInstalled, string? Version, string Message, DotMemoryCommandKind? Kind = null);
```

**Step 5: Add `IDotMemoryAutoInstaller` parameter to `DotMemoryToolManager`**

Change the class declaration from:
```csharp
public class DotMemoryToolManager(IProcessRunner processRunner)
```
to:
```csharp
public class DotMemoryToolManager(IProcessRunner processRunner, IDotMemoryAutoInstaller? autoInstaller = null)
```

**Step 6: Add `ResolveAutoInstalledAsync` and wire it into `ResolveCommandAsync`**

Add the new method:

```csharp
private async Task<DotMemoryCommand?> ResolveAutoInstalledAsync(CancellationToken ct)
{
    if (autoInstaller is null)
        return null;

    var path = await autoInstaller.GetCachedPathAsync(ct).ConfigureAwait(false);
    if (path is null)
        return null;

    return new DotMemoryCommand(
        path,
        "",
        $"auto-installed dotMemory ({path})",
        null,
        DotMemoryCommandKind.AutoInstalled);
}
```

In `ResolveCommandAsync`, insert the call after `ResolveConfiguredPath` and before `ResolveFromPathAsync`:

```csharp
public virtual async Task<DotMemoryCommand?> ResolveCommandAsync(CancellationToken ct = default)
{
    if (_cachedCommand is not null)
        return _cachedCommand;

    var configured = ResolveConfiguredPath();
    if (configured is not null) { _cachedCommand = configured; return configured; }

    var autoInstalled = await ResolveAutoInstalledAsync(ct).ConfigureAwait(false);
    if (autoInstalled is not null) { _cachedCommand = autoInstalled; return autoInstalled; }

    var fromPath = await ResolveFromPathAsync(ct).ConfigureAwait(false);
    if (fromPath is not null) { _cachedCommand = fromPath; return fromPath; }

    var localTool = await ResolveLocalToolAsync(ct).ConfigureAwait(false);
    if (localTool is not null) { _cachedCommand = localTool; return localTool; }

    var globalTool = await ResolveGlobalToolAsync(ct).ConfigureAwait(false);
    _cachedCommand = globalTool;
    return globalTool;
}
```

**Step 7: Wire `InstallLatestAsync` into `EnsureInstalledAsync`**

Replace the start of `EnsureInstalledAsync` so that after the command is not found via `ResolveCommandAsync`, we try auto-install before falling back to legacy `dotnet tool install`:

```csharp
public async Task<ToolStatus> EnsureInstalledAsync(CancellationToken ct = default)
{
    InvalidateCache();
    var command = await ResolveCommandAsync(ct).ConfigureAwait(false);
    if (command is not null)
    {
        if (command.Kind == DotMemoryCommandKind.GlobalTool)
        {
            var globalToolResult = await TryRunAsync("dotnet", "tool list -g", ct).ConfigureAwait(false);
            if (globalToolResult is not null && globalToolResult.ExitCode == 0 && ContainsTool(globalToolResult.Output))
            {
                var version = ParseVersion(globalToolResult.Output);
                await processRunner.RunAsync("dotnet", "tool update -g dotnet-dotmemory", ct).ConfigureAwait(false);
                return new ToolStatus(true, version, $"dotnet-dotmemory {version} is installed.", DotMemoryCommandKind.GlobalTool);
            }
        }

        return new ToolStatus(
            true,
            string.IsNullOrWhiteSpace(command.Version) ? null : command.Version,
            $"{command.DisplayName} is available.",
            command.Kind);
    }

    // Try auto-install before legacy fallback
    if (autoInstaller is not null)
    {
        var unsupportedMsg = autoInstaller.GetUnsupportedPlatformMessage();

        var autoPath = await autoInstaller.InstallLatestAsync(ct).ConfigureAwait(false);
        if (autoPath is not null)
        {
            InvalidateCache();
            command = await ResolveCommandAsync(ct).ConfigureAwait(false);
            if (command is not null)
                return new ToolStatus(true, command.Version, $"{command.DisplayName} is available.", command.Kind);
        }

        if (unsupportedMsg is not null)
            return new ToolStatus(false, null, unsupportedMsg);
    }

    // Legacy compatibility path
    var installResult = await processRunner
        .RunAsync("dotnet", "tool install -g dotnet-dotmemory", ct)
        .ConfigureAwait(false);

    if (installResult.ExitCode != 0)
    {
        var errorMessage = "dotMemory CLI was not found. " +
            "Set DOTMEMORY_PATH to dotMemory.exe/dotMemory.sh, " +
            "put dotMemory in PATH, or install dotnet-dotmemory.";

        if (!string.IsNullOrWhiteSpace(installResult.Error))
            errorMessage += $" Details: {installResult.Error}";

        return new ToolStatus(false, null, errorMessage);
    }

    command = await ResolveCommandAsync(ct).ConfigureAwait(false);
    if (command is not null)
        return new ToolStatus(true, command.Version, $"{command.DisplayName} is available.", command.Kind);

    return new ToolStatus(false, null,
        "dotnet-dotmemory was installed, but the executable could not be resolved. " +
        "Add the .NET tools directory to PATH or set DOTMEMORY_PATH explicitly.");
}
```

**Step 8: Register `IDotMemoryAutoInstaller` in DI**

In `Program.cs`, add before `AddSingleton<DotMemoryToolManager>`:

```csharp
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IDotMemoryAutoInstaller, DotMemoryAutoInstaller>();
```

(This will fail to compile until `DotMemoryAutoInstaller` exists — that's fine, leave a `// TODO` comment and add it in Task 3.)

**Step 9: Run tests**

```bash
dotnet test tests/MemoryLens.Mcp.Tests -c Release --nologo -q
```

Expected: all 86 existing tests pass + 4 new tests pass.

**Step 10: Commit**

```bash
git add src/MemoryLens.Mcp/Profiler/DotMemoryToolManager.cs
git add src/MemoryLens.Mcp/Program.cs
git add tests/MemoryLens.Mcp.Tests/Profiler/DotMemoryToolManagerTests.cs
git commit -m "feat: wire IDotMemoryAutoInstaller into DotMemoryToolManager"
```

---

### Task 3: DotMemoryAutoInstaller — Platform Mapping

**Files:**
- Create: `src/MemoryLens.Mcp/Profiler/DotMemoryAutoInstaller.cs`
- Create: `tests/MemoryLens.Mcp.Tests/Profiler/DotMemoryAutoInstallerTests.cs`

**Step 1: Write failing tests for platform mapping**

```csharp
// tests/MemoryLens.Mcp.Tests/Profiler/DotMemoryAutoInstallerTests.cs
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
    [InlineData(true, false, "linux-musl-x64")]
    public void IsMusl_AffectsLinuxRid(bool hasMuslFile, bool isArm64, string expectedSuffix)
    {
        // This tests the logic branching — actual file detection tested via GetRid()
        var rid = DotMemoryAutoInstaller.BuildLinuxRid(isArm64: isArm64, isMusl: hasMuslFile);
        Assert.Equal(expectedSuffix, rid);
    }

    [Theory]
    [InlineData(false, false, "linux-x64")]
    [InlineData(false, true, "linux-arm64")]
    [InlineData(true, false, "linux-musl-x64")]
    [InlineData(true, true, "linux-musl-arm64")]
    public void BuildLinuxRid_ReturnsCorrectSuffix(bool isMusl, bool isArm64, string expected)
    {
        Assert.Equal(expected, DotMemoryAutoInstaller.BuildLinuxRid(isArm64, isMusl));
    }
}
```

**Step 2: Run to verify they fail**

```bash
dotnet test tests/MemoryLens.Mcp.Tests -c Release --nologo -q --filter "DotMemoryAutoInstallerTests"
```

Expected: compilation error — `DotMemoryAutoInstaller` does not exist.

**Step 3: Implement the class with platform mapping**

```csharp
// src/MemoryLens.Mcp/Profiler/DotMemoryAutoInstaller.cs
using System.Net.Http;
using System.Runtime.InteropServices;

namespace MemoryLens.Mcp.Profiler;

public class DotMemoryAutoInstaller(HttpClient httpClient) : IDotMemoryAutoInstaller
{
    private static readonly string CacheRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".memorylens", "tools", "dotmemory");

    // --- Platform detection ---

    public static string? GetRid()
    {
        if (OperatingSystem.IsWindows())
        {
            return RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64  => "windows-x64",
                Architecture.X86  => "windows-x86",
                Architecture.Arm64 => "windows-arm64",
                _ => null
            };
        }

        if (OperatingSystem.IsLinux())
        {
            bool isArm64 = RuntimeInformation.OSArchitecture == Architecture.Arm64;
            bool isArm   = RuntimeInformation.OSArchitecture == Architecture.Arm;
            bool isMusl  = IsMusl();

            if (isArm) return "linux-arm";
            return BuildLinuxRid(isArm64, isMusl);
        }

        if (OperatingSystem.IsMacOS())
        {
            return RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64  => "macos-x64",
                Architecture.Arm64 => "macos-arm64",
                _ => null
            };
        }

        return null;
    }

    public static string BuildLinuxRid(bool isArm64, bool isMusl)
    {
        var arch = isArm64 ? "arm64" : "x64";
        return isMusl ? $"linux-musl-{arch}" : $"linux-{arch}";
    }

    internal static bool IsMusl() =>
        File.Exists("/lib/ld-musl-x86_64.so.1") ||
        File.Exists("/lib/ld-musl-aarch64.so.1");

    public string? GetUnsupportedPlatformMessage()
    {
        if (GetRid() is not null)
            return null;

        var current = $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})";
        return $"Platform '{current}' is not supported by JetBrains dotMemory Console. " +
               "Set DOTMEMORY_PATH to point to your dotMemory installation. " +
               "See README for details.";
    }

    // Stubs — implemented in Task 4 and 5
    public Task<string?> GetCachedPathAsync(CancellationToken ct) => Task.FromResult<string?>(null);
    public Task<string?> InstallLatestAsync(CancellationToken ct) => Task.FromResult<string?>(null);
}
```

**Step 4: Run tests**

```bash
dotnet test tests/MemoryLens.Mcp.Tests -c Release --nologo -q --filter "DotMemoryAutoInstallerTests"
```

Expected: all new platform mapping tests pass.

**Step 5: Commit**

```bash
git add src/MemoryLens.Mcp/Profiler/DotMemoryAutoInstaller.cs
git add tests/MemoryLens.Mcp.Tests/Profiler/DotMemoryAutoInstallerTests.cs
git commit -m "feat: add DotMemoryAutoInstaller with platform mapping"
```

---

### Task 4: DotMemoryAutoInstaller — Cache Reading

**Files:**
- Modify: `src/MemoryLens.Mcp/Profiler/DotMemoryAutoInstaller.cs`
- Modify: `tests/MemoryLens.Mcp.Tests/Profiler/DotMemoryAutoInstallerTests.cs`

**Step 1: Write failing tests**

Add to `DotMemoryAutoInstallerTests`:

```csharp
[Fact]
public async Task GetCachedPath_ReturnsNull_WhenNoCacheDir()
{
    var installer = new DotMemoryAutoInstaller(new HttpClient(), cacheRoot: Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
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
        var installer = new DotMemoryAutoInstaller(new HttpClient(), cacheRoot);
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
        var installer = new DotMemoryAutoInstaller(new HttpClient(), cacheRoot);
        var result = await installer.GetCachedPathAsync(TestContext.Current.CancellationToken);
        Assert.Equal(exePath, result);
    }
    finally { Directory.Delete(cacheRoot, recursive: true); }
}
```

Note: `DotMemoryAutoInstaller` needs a `cacheRoot` parameter for testing (injected, not hardcoded).

**Step 2: Run to verify they fail**

```bash
dotnet test tests/MemoryLens.Mcp.Tests -c Release --nologo -q --filter "GetCachedPath"
```

**Step 3: Implement `GetCachedPathAsync` and `FindExecutable`**

Update `DotMemoryAutoInstaller` to accept `cacheRoot` as an optional parameter (default is `~/.memorylens/tools/dotmemory`):

```csharp
public class DotMemoryAutoInstaller(HttpClient httpClient, string? cacheRoot = null) : IDotMemoryAutoInstaller
{
    private readonly string _cacheRoot = cacheRoot ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".memorylens", "tools", "dotmemory");

    public async Task<string?> GetCachedPathAsync(CancellationToken ct)
    {
        var currentFile = Path.Combine(_cacheRoot, "current.txt");
        if (!File.Exists(currentFile))
            return null;

        var version = (await File.ReadAllTextAsync(currentFile, ct).ConfigureAwait(false)).Trim();
        var versionDir = Path.Combine(_cacheRoot, version);
        var exePath = FindExecutable(versionDir);
        return exePath is not null && File.Exists(exePath) ? exePath : null;
    }

    private static string? FindExecutable(string versionDir)
    {
        var toolsDir = Path.Combine(versionDir, "tools");
        if (!Directory.Exists(toolsDir))
            return null;

        var candidates = OperatingSystem.IsWindows()
            ? new[] { "dotMemory.exe" }
            : new[] { "dotMemory.sh", "dotMemory" };

        foreach (var name in candidates)
        {
            var path = Path.Combine(toolsDir, name);
            if (File.Exists(path))
                return path;
        }

        return null;
    }
}
```

**Step 4: Run tests**

```bash
dotnet test tests/MemoryLens.Mcp.Tests -c Release --nologo -q --filter "GetCachedPath"
```

Expected: all 3 new tests pass.

**Step 5: Commit**

```bash
git add src/MemoryLens.Mcp/Profiler/DotMemoryAutoInstaller.cs
git add tests/MemoryLens.Mcp.Tests/Profiler/DotMemoryAutoInstallerTests.cs
git commit -m "feat: implement GetCachedPathAsync in DotMemoryAutoInstaller"
```

---

### Task 5: DotMemoryAutoInstaller — Download & Install

**Files:**
- Modify: `src/MemoryLens.Mcp/Profiler/DotMemoryAutoInstaller.cs`
- Modify: `tests/MemoryLens.Mcp.Tests/Profiler/DotMemoryAutoInstallerTests.cs`

**Step 1: Write failing tests**

Add to `DotMemoryAutoInstallerTests`:

```csharp
[Fact]
public async Task InstallLatest_ReturnsNull_WhenPlatformUnsupported()
{
    // Use a subclass that reports unsupported platform
    var installer = new UnsupportedPlatformInstaller(new HttpClient());
    var result = await installer.InstallLatestAsync(TestContext.Current.CancellationToken);
    Assert.Null(result);
}

[Fact]
public async Task FetchLatestVersion_ReturnsVersion_FromJson()
{
    var json = """{"versions":["2025.3.0","2026.1.0"]}""";
    var http = new HttpClient(new FakeHttpMessageHandler(json));
    var installer = new DotMemoryAutoInstaller(http, Path.GetTempPath());

    var version = await installer.FetchLatestVersionAsync("jetbrains.dotmemory.console.windows-x64", CancellationToken.None);

    Assert.Equal("2026.1.0", version);
}

// Helper for FakeHttpMessageHandler in tests/MemoryLens.Mcp.Tests/Profiler/FakeHttpMessageHandler.cs:
// public class FakeHttpMessageHandler(string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK)
//     : HttpMessageHandler
// {
//     protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
//         => Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(responseBody) });
// }
```

**Step 2: Create `FakeHttpMessageHandler`**

```csharp
// tests/MemoryLens.Mcp.Tests/Profiler/FakeHttpMessageHandler.cs
using System.Net;
using System.Net.Http;

namespace MemoryLens.Mcp.Tests.Profiler;

public class FakeHttpMessageHandler(string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK)
    : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(responseBody)
        });
}
```

**Step 3: Run to verify they fail**

```bash
dotnet test tests/MemoryLens.Mcp.Tests -c Release --nologo -q --filter "InstallLatest|FetchLatest"
```

**Step 4: Implement `FetchLatestVersionAsync` and `InstallLatestAsync`**

Replace the `InstallLatestAsync` and `GetCachedPathAsync` stubs in `DotMemoryAutoInstaller.cs` with:

```csharp
public async Task<string?> InstallLatestAsync(CancellationToken ct)
{
    var rid = GetRid();
    if (rid is null)
        return null;

    var packageId = $"jetbrains.dotmemory.console.{rid}";

    string? version;
    try
    {
        version = await FetchLatestVersionAsync(packageId, ct).ConfigureAwait(false);
    }
    catch (OperationCanceledException) { throw; }
    catch { return null; }

    if (version is null)
        return null;

    var versionDir = Path.Combine(_cacheRoot, version);

    if (!Directory.Exists(versionDir))
    {
        try
        {
            await DownloadAndExtractAsync(packageId, version, versionDir, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            if (Directory.Exists(versionDir))
                Directory.Delete(versionDir, recursive: true);
            return null;
        }
    }

    var exePath = FindExecutable(versionDir);
    if (exePath is null || !File.Exists(exePath))
    {
        Directory.Delete(versionDir, recursive: true);
        return null;
    }

    try { await MakeExecutableAsync(exePath).ConfigureAwait(false); }
    catch (OperationCanceledException) { throw; }
    catch { return null; } // chmod failure — can't execute

    Directory.CreateDirectory(_cacheRoot);
    await File.WriteAllTextAsync(Path.Combine(_cacheRoot, "current.txt"), version, ct).ConfigureAwait(false);

    return exePath;
}

internal async Task<string?> FetchLatestVersionAsync(string packageId, CancellationToken ct)
{
    var url = $"https://api.nuget.org/v3-flatcontainer/{packageId}/index.json";
    var json = await httpClient.GetStringAsync(url, ct).ConfigureAwait(false);
    using var doc = System.Text.Json.JsonDocument.Parse(json);
    var versions = doc.RootElement.GetProperty("versions");
    var last = versions[versions.GetArrayLength() - 1].GetString();
    return last;
}

private async Task DownloadAndExtractAsync(string packageId, string version, string versionDir, CancellationToken ct)
{
    var url = $"https://api.nuget.org/v3-flatcontainer/{packageId}/{version}/{packageId}.{version}.nupkg";
    var tempFile = Path.GetTempFileName();
    try
    {
        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var fs = File.Create(tempFile);
        await stream.CopyToAsync(fs, ct).ConfigureAwait(false);
        fs.Close();

        Directory.CreateDirectory(versionDir);
        System.IO.Compression.ZipFile.ExtractToDirectory(tempFile, versionDir, overwriteFiles: true);
    }
    finally
    {
        File.Delete(tempFile);
    }
}

private static Task MakeExecutableAsync(string path)
{
    if (OperatingSystem.IsWindows())
        return Task.CompletedTask;

    File.SetUnixFileMode(path,
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
        UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
        UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

    return Task.CompletedTask;
}
```

**Step 5: Run tests**

```bash
dotnet test tests/MemoryLens.Mcp.Tests -c Release --nologo -q
```

Expected: all tests pass.

**Step 6: Commit**

```bash
git add src/MemoryLens.Mcp/Profiler/DotMemoryAutoInstaller.cs
git add tests/MemoryLens.Mcp.Tests/Profiler/DotMemoryAutoInstallerTests.cs
git add tests/MemoryLens.Mcp.Tests/Profiler/FakeHttpMessageHandler.cs
git commit -m "feat: implement InstallLatestAsync with NuGet download and extraction"
```

---

### Task 6: DI Registration + Full Build

**Files:**
- Modify: `src/MemoryLens.Mcp/Program.cs`

**Step 1: Register `DotMemoryAutoInstaller` and `HttpClient`**

Replace the `// TODO` comment and update `Program.cs`:

```csharp
builder.Services.AddHttpClient<DotMemoryAutoInstaller>();
builder.Services.AddSingleton<IDotMemoryAutoInstaller, DotMemoryAutoInstaller>();
```

Add above the existing `AddSingleton<DotMemoryToolManager>()` line.

**Step 2: Build and run all tests**

```bash
dotnet build src/MemoryLens.Mcp/MemoryLens.Mcp.csproj -c Release --nologo -q
dotnet test tests/MemoryLens.Mcp.Tests -c Release --nologo -q
```

Expected: build succeeds, all tests pass.

**Step 3: Commit**

```bash
git add src/MemoryLens.Mcp/Program.cs
git commit -m "feat: register DotMemoryAutoInstaller in DI"
```

---

### Task 7: README Documentation

**Files:**
- Modify: `README.md`

**Step 1: Add a "dotMemory CLI" section to README**

After the existing Prerequisites section, add:

```markdown
## dotMemory CLI

MemoryLens MCP automatically downloads the official JetBrains dotMemory Console CLI on first use
by calling the `ensure_dotmemory` MCP tool. No manual setup required on supported platforms.

### Supported Platforms (auto-download)

| Platform        | Architecture       |
|-----------------|--------------------|
| Windows         | x64, x86, ARM64    |
| Linux (glibc)   | x64, ARM64, ARM    |
| Linux (musl)    | x64, ARM64         |
| macOS           | x64 (Intel), ARM64 (Apple Silicon) |

### Unsupported Platforms

Platforms not listed above (e.g. FreeBSD, Linux x86) cannot use auto-download.
Set `DOTMEMORY_PATH` to point to your dotMemory installation instead:

```bash
export DOTMEMORY_PATH="/path/to/dotMemory.sh"   # Linux/macOS
set DOTMEMORY_PATH=C:\path\to\dotMemory.exe      # Windows
```

Find your dotMemory CLI in your JetBrains Toolbox installation:
- **Linux:** `~/.local/share/JetBrains/Toolbox/apps/rider/tools/profiler/dotMemory.sh`
- **Windows:** `%LOCALAPPDATA%\JetBrains\Toolbox\apps\rider\tools\profiler\dotMemory.exe`

### Error Scenarios

| Error | Cause | Fix |
|---|---|---|
| `Platform '...' is not supported` | Unsupported OS/arch | Set `DOTMEMORY_PATH` |
| `Failed to download dotMemory` | No internet / NuGet unreachable | Set `DOTMEMORY_PATH` or retry |
| `chmod +x failed` | Read-only filesystem | Set `DOTMEMORY_PATH` to a writable location |
| `dotMemory CLI not found` | All discovery modes failed | Run `ensure_dotmemory` or set `DOTMEMORY_PATH` |

### Cache Location

Downloaded binaries are cached at `~/.memorylens/tools/dotmemory/{version}/`.
Old versions are not auto-removed. Delete the directory manually to free disk space.
```

**Step 2: Run full test suite one final time**

```bash
dotnet test tests/MemoryLens.Mcp.Tests -c Release --nologo -q
```

Expected: all tests pass.

**Step 3: Commit**

```bash
git add README.md
git commit -m "docs: document dotMemory auto-install, supported platforms, and error scenarios"
```

---

### Task 8: Open PR

```bash
git push origin fix/pr41-dotmemory-path
gh pr create \
  --base main \
  --title "feat: auto-download JetBrains dotMemory Console on first use" \
  --body "Automatically downloads the correct JetBrains.dotMemory.Console NuGet package on first \`ensure_dotmemory\` call. Zero user setup on Windows, Linux (glibc/musl), and macOS. Unsupported platforms fall back to DOTMEMORY_PATH with clear guidance."
```
