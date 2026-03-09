# MemoryLens MCP — Design Document

**Date:** 2026-03-09
**Status:** Approved
**Author:** Marcel Roozekrans

## Overview

MemoryLens MCP is a .NET MCP server that wraps the `dotnet-dotmemory` CLI tool to provide on-demand memory profiling, analysis, and concrete code fix suggestions for .NET applications. It ships as a standalone Claude Code marketplace plugin with a bundled skill.

## Architecture

```
┌─────────────────────────────────────────────────┐
│  Claude Code                                    │
│  ┌───────────────┐    ┌──────────────────────┐  │
│  │ memorylens     │    │ memorylens-mcp       │  │
│  │ skill          │◄──►│ (.NET MCP Server)    │  │
│  │ (SKILL.md)     │    │                      │  │
│  └───────────────┘    │  ┌────────────────┐   │  │
│                        │  │ Tool Manager   │   │  │
│                        │  └────────────────┘   │  │
│                        │  ┌────────────────┐   │  │
│                        │  │ Profiler       │   │  │
│                        │  └────────────────┘   │  │
│                        │  ┌────────────────┐   │  │
│                        │  │ Rule Engine    │   │  │
│                        │  └────────────────┘   │  │
│                        │  ┌────────────────┐   │  │
│                        │  │ Suggestion     │   │  │
│                        │  │ Generator      │   │  │
│                        │  └────────────────┘   │  │
│                        └──────────────────────┘  │
│                                 │                │
│                        ┌────────▼───────────┐    │
│                        │ dotnet-dotmemory   │    │
│                        │ (CLI tool)         │    │
│                        └────────────────────┘    │
└─────────────────────────────────────────────────┘
```

### Key Components

- **Tool Manager** — ensures `dotnet-dotmemory` is installed and up-to-date via `dotnet tool install/update`.
- **Profiler** — orchestrates snapshot capture (single or comparison mode), with process filtering to avoid attaching to IDE/roslyn processes.
- **Rule Engine** — evaluates snapshots against built-in rules + user-defined rules from `.memorylens.json`.
- **Suggestion Generator** — maps rule violations to concrete code diffs with file, line, and before/after code.

### Approach

Direct dotMemory CLI wrapper. The MCP server shells out to `dotnet-dotmemory` for profiling, parses structured output, and runs analysis. This keeps the profiler isolated (no interference with IDE/roslyn-codegraph) and uses the supported CLI interface.

## MCP Tools

| Tool | Description | Parameters |
|---|---|---|
| `ensure_dotmemory` | Install/update `dotnet-dotmemory` global tool | none |
| `snapshot` | Take a single memory snapshot | `pid` or `processName` or `command`, `duration` (optional, seconds) |
| `compare_snapshots` | Take two snapshots with an action in between | `pid` or `processName` or `command`, `waitBefore` (ms), `waitAfter` (ms) |
| `analyze` | Run rules against a snapshot/comparison result | `snapshotId`, `rulesPath` (optional) |
| `list_processes` | List running .NET processes (filtered) | `filter` (optional) |
| `get_rules` | List all active rules (built-in + user) | `rulesPath` (optional) |

### Workflow: Single Snapshot

1. `ensure_dotmemory` — confirms tool is ready
2. `list_processes` — user picks target
3. `snapshot(pid: 1234)` — returns `snapshotId`
4. `analyze(snapshotId)` — returns findings with code diffs

### Workflow: Comparison

1. `ensure_dotmemory` — confirms tool is ready
2. `compare_snapshots(command: "dotnet run", waitBefore: 5000, waitAfter: 10000)` — launches app, snapshots at 5s, snapshots again at 15s, returns comparison `snapshotId`
3. `analyze(snapshotId)` — returns delta findings (what grew, what leaked)

## Built-in Rules

| Rule ID | Severity | Category | Detects |
|---|---|---|---|
| `ML001` | critical | leak | Event handler not unsubscribed (growing delegate chains) |
| `ML002` | critical | leak | Static collections that grow unbounded |
| `ML003` | high | leak | Disposable objects not disposed (IDisposable leak) |
| `ML004` | high | fragmentation | Large Object Heap fragmentation (objects > 85KB) |
| `ML005` | medium | retention | Objects retained longer than expected (gen2 promotion) |
| `ML006` | medium | allocation | Excessive allocations in hot paths (boxing, string concat) |
| `ML007` | medium | retention | Captured variables in closures holding references |
| `ML008` | low | allocation | Unnecessary array/list resizing (missing capacity hint) |
| `ML009` | low | pattern | Finalizer without Dispose pattern |
| `ML010` | low | pattern | String interning opportunities |

### Rule Output Format

```json
{
  "ruleId": "ML001",
  "severity": "critical",
  "category": "leak",
  "title": "Event handler leak detected",
  "description": "UserService subscribes to AppEvents.Changed but never unsubscribes",
  "evidence": {
    "type": "UserService",
    "retainedBytes": 2457600,
    "instanceCount": 48,
    "retentionPath": "AppEvents.Changed -> UserService.OnChanged -> UserService"
  },
  "suggestion": {
    "file": "src/Services/UserService.cs",
    "line": 42,
    "old": "public void Dispose()\n{\n}",
    "new": "public void Dispose()\n{\n    AppEvents.Changed -= OnChanged;\n}"
  }
}
```

### User Configuration (`.memorylens.json`)

```json
{
  "rules": {
    "ML004": { "severity": "critical", "threshold": { "minBytes": 52428800 } },
    "ML010": { "enabled": false }
  },
  "ignore": [
    "*.Tests.*",
    "*.Benchmarks.*"
  ]
}
```

Users can override severity, disable rules, set thresholds, and add ignore patterns.

## Process Safety

### Default Exclusions

The following processes are excluded from `list_processes` and blocked from attachment:

- `devenv.exe` (Visual Studio)
- `rider64.exe` / `JetBrains.Rider*` (Rider)
- `dotnet` processes matching roslyn-codegraph-mcp (by command line args)
- `Code.exe` / `code-insiders` (VS Code)
- `ServiceHub.*` (VS background services)
- `dotnet-dotmemory` itself
- The memorylens-mcp server process itself

When attaching by PID, the server validates the PID is not excluded. If it is, it returns an error. When launching via `command`, no filtering is needed.

## Marketplace & Skill Packaging

### Repository Structure

```
memorylens-mcp/
├── .claude-plugin/
│   ├── marketplace.json
│   └── plugin.json
├── .mcp.json
├── skills/
│   └── memorylens/
│       └── SKILL.md
├── src/
│   └── MemoryLens.Mcp/
│       ├── MemoryLens.Mcp.csproj
│       ├── Program.cs
│       ├── Tools/
│       ├── Profiler/
│       ├── Rules/
│       │   └── BuiltIn/
│       ├── Analysis/
│       └── Config/
├── .github/
│   └── workflows/
│       ├── ci.yml
│       └── release.yml
├── .gitignore
├── LICENSE
├── README.md
└── memorylens-mcp.sln
```

### MCP Server Config (`.mcp.json`)

```json
{
  "memorylens": {
    "command": "dotnet",
    "args": ["run", "--project", "src/MemoryLens.Mcp"]
  }
}
```

### Skill Integration

The standalone skill (`SKILL.md`) instructs Claude to:

- Call `ensure_dotmemory` at the start
- Use `list_processes` to find the target
- Choose single snapshot vs comparison mode based on the user's ask
- Run `analyze` and apply code suggestions via Edit tool
- Integrate with systematic-debugging when memory issues are suspected
- Integrate with brainstorming when designing performance-sensitive features

A separate superpowers-extensions plugin will be created later for deeper superpowers integration.

## Technology Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Runtime | .NET (C#) | Natural fit for dotMemory, consistent with roslyn-codegraph |
| dotMemory invocation | `dotnet-dotmemory` global tool | Newer CLI approach, managed lifecycle |
| Profiler architecture | CLI wrapper (Approach A) | Simple, process-isolated, uses supported interface |
| Suggestion format | Concrete code diffs | Best for AI to apply directly via Edit tool |
| Marketplace | Standalone repo | MCP server + skill bundled, like longterm-memory |
