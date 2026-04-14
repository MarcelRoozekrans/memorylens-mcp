<p align="center">
  <img src="icon.svg" width="128" height="128" alt="MemoryLens MCP">
</p>

<h1 align="center">MemoryLens MCP</h1>

<p align="center">
  <a href="https://www.nuget.org/packages/MemoryLens.Mcp"><img src="https://img.shields.io/nuget/v/MemoryLens.Mcp?style=flat-square&logo=nuget&color=blue" alt="NuGet"></a>
  <a href="https://www.nuget.org/packages/MemoryLens.Mcp"><img src="https://img.shields.io/nuget/dt/MemoryLens.Mcp?style=flat-square&color=green" alt="NuGet Downloads"></a>
  <a href="https://github.com/MarcelRoozekrans/memorylens-mcp/actions"><img src="https://img.shields.io/github/actions/workflow/status/MarcelRoozekrans/memorylens-mcp/ci.yml?branch=main&style=flat-square&logo=github" alt="Build Status"></a>
  <a href="https://github.com/MarcelRoozekrans/memorylens-mcp/blob/main/LICENSE"><img src="https://img.shields.io/github/license/MarcelRoozekrans/memorylens-mcp?style=flat-square" alt="License"></a>
</p>

<p align="center">
  On-demand .NET memory profiling with concrete, AI-actionable code fix suggestions — wraps JetBrains dotMemory with a heuristic-based rule engine.
</p>

<a href="https://glama.ai/mcp/servers/MarcelRoozekrans/memorylens-mcp">
  <img width="380" height="200" src="https://glama.ai/mcp/servers/MarcelRoozekrans/memorylens-mcp/badge" alt="memorylens-mcp MCP server" />
</a>

<!-- mcp-name: io.github.MarcelRoozekrans/memorylens-mcp -->

---

## Hosted deployment

A hosted deployment is available on [Fronteir AI](https://fronteir.ai/mcp/marcelroozekrans-memorylens-mcp).

## Quick Start

### VS Code / Visual Studio (via dnx)

Add to your MCP settings (`.vscode/mcp.json` or VS settings):

```json
{
  "servers": {
    "memorylens": {
      "type": "stdio",
      "command": "dnx",
      "args": ["MemoryLens.Mcp", "--yes"]
    }
  }
}
```

### Claude Code Plugin

```bash
claude install gh:MarcelRoozekrans/memorylens-mcp
```

### .NET Global Tool

```bash
dotnet tool install -g MemoryLens.Mcp
```

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later
- JetBrains dotMemory CLI (see below for installation options)

## dotMemory CLI Installation

MemoryLens MCP automatically downloads and caches the JetBrains dotMemory CLI on first use via the `ensure_dotmemory` tool — no manual installation required on supported platforms.

### Supported Platforms (auto-download)

| Platform | Architecture |
|---|---|
| Windows | x64, x86, ARM64 |
| Linux (glibc) | x64, ARM64, ARM |
| Linux (musl) | x64, ARM64 |
| macOS | x64 (Intel), ARM64 (Apple Silicon) |

### Cache Location

Downloaded binaries are cached at `~/.memorylens/tools/dotmemory/{version}/`. Old versions are not auto-removed — delete the directory manually to free disk space.

### Unsupported Platforms

Platforms not listed above (e.g. FreeBSD, Linux x86) cannot use auto-download. Set `DOTMEMORY_PATH` to point to an existing dotMemory CLI executable:

```bash
export DOTMEMORY_PATH="/path/to/dotMemory.sh"   # Linux/macOS
set DOTMEMORY_PATH=C:\path\to\dotMemory.exe      # Windows
```

Find dotMemory CLI in JetBrains Toolbox:
- Linux: `~/.local/share/JetBrains/Toolbox/apps/rider/tools/profiler/dotMemory.sh`
- Windows: `%LOCALAPPDATA%\JetBrains\Toolbox\apps\rider\tools\profiler\dotMemory.exe`

### Manual Fallback Discovery

If auto-download is unavailable, MemoryLens MCP falls back through these discovery modes in order:

1. **`DOTMEMORY_PATH` environment variable** — explicit path to the CLI executable
2. **System PATH** — searches for `dotMemory.sh` / `dotMemory` (Linux/macOS) or `dotMemory.exe` (Windows)
3. **Local .NET tool manifest** — `dotnet tool install dotnet-dotmemory --local`
4. **Global .NET tool** — `dotnet tool install -g dotnet-dotmemory` (legacy fallback)

### Error Scenarios

| Error | Cause | Fix |
|---|---|---|
| `Platform '...' is not supported` | Unsupported OS/arch | Set `DOTMEMORY_PATH` |
| Network/download failure | No internet / NuGet unreachable | Set `DOTMEMORY_PATH` or retry `ensure_dotmemory` |
| `chmod +x failed` | Read-only filesystem | Set `DOTMEMORY_PATH` to a writable location |
| `dotMemory CLI not found` | All discovery modes failed | Run `ensure_dotmemory` or set `DOTMEMORY_PATH` |

## Available MCP Tools

| Tool | Description |
|------|-------------|
| `ensure_dotmemory` | Downloads and verifies the JetBrains dotMemory CLI tool is available |
| `list_processes` | Lists running .NET processes available for profiling |
| `snapshot` | Captures a single memory snapshot of a target process |
| `compare_snapshots` | Captures two snapshots with configurable delay and compares them |
| `analyze` | Runs the rule engine against a captured snapshot and returns findings |
| `get_rules` | Lists all available analysis rules with their metadata |

## Built-in Rules

| ID | Severity | Category | Description |
|----|----------|----------|-------------|
| ML001 | critical | leak | Event handler leak detected |
| ML002 | critical | leak | Static collection growing unbounded |
| ML003 | high | leak | Disposable object not disposed |
| ML004 | high | fragmentation | Large Object Heap fragmentation |
| ML005 | medium | retention | Object retained longer than expected |
| ML006 | medium | allocation | Excessive allocations in hot path |
| ML007 | medium | retention | Closure retaining unexpected references |
| ML008 | low | allocation | Array/list resizing without capacity hint |
| ML009 | low | pattern | Finalizer without Dispose pattern |
| ML010 | low | pattern | String interning opportunity |

## Configuration

Create a `.memorylens.json` file in your project root to customize rule behavior:

```json
{
  "rules": {
    "ML001": { "enabled": true, "severity": "critical" },
    "ML002": { "enabled": true, "severity": "critical" },
    "ML003": { "enabled": true, "severity": "high" },
    "ML004": { "enabled": true, "severity": "high" },
    "ML005": { "enabled": true, "severity": "medium" },
    "ML006": { "enabled": true, "severity": "medium" },
    "ML007": { "enabled": true, "severity": "medium" },
    "ML008": { "enabled": true, "severity": "low" },
    "ML009": { "enabled": true, "severity": "low" },
    "ML010": { "enabled": true, "severity": "low" }
  }
}
```

## Usage Examples

### Single Snapshot

Capture a memory snapshot of a running process to inspect current memory state:

```
> /memorylens
> Take a snapshot of my running API (PID 12345)
```

Claude will call `ensure_dotmemory`, then `snapshot` with the target PID, then `analyze` the result and present findings ordered by severity.

### Before/After Comparison

Detect memory growth by comparing two snapshots taken with a delay:

```
> /memorylens
> Check if my app has a memory leak — compare before and after processing 1000 requests
```

Claude will call `compare_snapshots` with a configurable wait period, then analyze the diff to identify objects that grew between snapshots.

## License

[MIT](LICENSE)