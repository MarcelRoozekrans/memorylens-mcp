# Design: dotMemory Auto-Install via NuGet Download

**Date:** 2026-04-14
**Status:** Approved

## Problem

`MemoryLens.Mcp` originally assumed `dotnet-dotmemory` existed as a NuGet global tool. It does not — JetBrains does not publish dotMemory CLI that way. PR #42 added `DOTMEMORY_PATH` and PATH discovery as a workaround, but still requires users to manually install dotMemory and configure the path.

## Goal

Make dotMemory work out of the box with zero user setup on all supported platforms, by automatically downloading the official `JetBrains.dotMemory.Console.*` redistributable NuGet packages on first use.

## Approach: Download on First Use

When `ensure_dotmemory` is called and no dotMemory binary is found via existing discovery modes, automatically download and cache the correct platform package from NuGet. On subsequent runs, use the cached binary — no network call needed.

---

## Section 1: Architecture

A new `DotMemoryCommandKind.AutoInstalled` is added. `DotMemoryToolManager.ResolveCommandAsync` gains a new step `ResolveAutoInstalledAsync` inserted after `DOTMEMORY_PATH` but before PATH discovery:

```
DOTMEMORY_PATH → AutoInstalled (cached) → PATH → LocalTool → GlobalTool
```

The download logic lives in a new `DotMemoryAutoInstaller` class, injected into `DotMemoryToolManager` via `IDotMemoryAutoInstaller`. Downloads are only triggered from `EnsureInstalledAsync` — never from a bare `ResolveCommandAsync` call.

Cache location: `~/.memorylens/tools/dotmemory/{version}/`

Multiple versions coexist in the cache. A `current.txt` file in `~/.memorylens/tools/dotmemory/` points to the active version directory.

---

## Section 2: Platform Mapping

Runtime detection uses `RuntimeInformation.OSArchitecture` + `OperatingSystem.Is*()`:

| Runtime              | NuGet package suffix   |
|----------------------|------------------------|
| Windows x64          | `windows-x64`          |
| Windows x86          | `windows-x86`          |
| Windows ARM64        | `windows-arm64`        |
| Linux x64            | `linux-x64`            |
| Linux ARM64          | `linux-arm64`          |
| Linux ARM            | `linux-arm`            |
| Linux musl x64       | `linux-musl-x64`       |
| Linux musl ARM64     | `linux-musl-arm64`     |
| macOS x64            | `macos-x64`            |
| macOS ARM64          | `macos-arm64`          |

musl detection: check for `/lib/ld-musl-x86_64.so.1` (x64) or `/lib/ld-musl-aarch64.so.1` (ARM64) — standard approach since .NET does not expose this via `RuntimeInformation`.

Platforms not in the table (e.g. FreeBSD, win-x86 if ever dropped by JetBrains) return `null` from `ResolveAutoInstalledAsync` and fall through to the existing discovery chain. The README documents supported platforms and directs unsupported ones to `DOTMEMORY_PATH`.

---

## Section 3: Download & Extraction

NuGet packages are ZIP files. The flow inside `InstallLatestAsync`:

1. Query latest version from NuGet v3 flat container API:
   `https://api.nuget.org/v3-flatcontainer/jetbrains.dotmemory.console.{rid}/index.json`
2. Download `.nupkg` to a temp file:
   `https://api.nuget.org/v3-flatcontainer/jetbrains.dotmemory.console.{rid}/{version}/jetbrains.dotmemory.console.{rid}.{version}.nupkg`
3. Extract into `~/.memorylens/tools/dotmemory/{version}/` using `ZipFile.ExtractToDirectory`
4. Locate the executable under `tools/` inside the extracted package
5. On Linux/macOS, set executable permission via `File.SetUnixFileMode`
6. Write `current.txt` pointing to the new version directory

If extraction is interrupted, the version directory is deleted before retrying to avoid serving a corrupt binary.

---

## Section 4: Version Management

- **`EnsureInstalledAsync`**: always queries NuGet for the latest version. If newer than cached, downloads alongside the old version, then updates `current.txt` atomically. Old version directories are left in place — not auto-deleted.
- **`ResolveAutoInstalledAsync`**: reads `current.txt` to find the active binary — no network call.
- Future cleanup is out of scope for this iteration (can be a `clean_dotmemory` MCP tool or CLI flag).

---

## Section 5: Error Handling

All error scenarios must be documented in the README.

| Scenario | Behaviour |
|---|---|
| No internet / NuGet unreachable | `EnsureInstalledAsync` catches `HttpRequestException`, returns `ToolStatus(false)` with message suggesting `DOTMEMORY_PATH`. Falls through to existing discovery modes. |
| Unsupported platform | Returns `ToolStatus(false)` naming the platform and linking to README manual setup section. |
| Partial/corrupt extraction | Detected by checking executable exists after extraction. Delete version directory, retry once. On second failure, fall through to existing discovery modes. |
| `chmod +x` failure | Surfaced as a clear error — do not silently return a non-executable binary. |
| Cancellation | `OperationCanceledException` always re-thrown, never swallowed. |

---

## Section 6: Testing

`DotMemoryAutoInstaller` is hidden behind `IDotMemoryAutoInstaller`:

```csharp
public interface IDotMemoryAutoInstaller
{
    Task<string?> GetCachedPathAsync(CancellationToken ct);
    Task<string?> InstallLatestAsync(CancellationToken ct);
    string? GetUnsupportedPlatformMessage();
}
```

Unit test coverage:

- `ResolveAutoInstalledAsync` returns cached path when `GetCachedPathAsync` returns a value
- `EnsureInstalledAsync` calls `InstallLatestAsync` when cache is empty
- `EnsureInstalledAsync` falls through gracefully when `InstallLatestAsync` returns `null`
- `EnsureInstalledAsync` returns unsupported platform message correctly
- `DotMemoryAutoInstaller` platform mapping (no I/O)
- `DotMemoryAutoInstaller` musl detection logic

The real `DotMemoryAutoInstaller` (HTTP + filesystem) is excluded from CI unit tests — it requires network access and is covered by manual integration testing.

---

## Out of Scope

- Cache cleanup / `clean_dotmemory` tool
- Version pinning via config
- Proxy support for NuGet downloads
- Offline/airgap installs (use `DOTMEMORY_PATH`)
