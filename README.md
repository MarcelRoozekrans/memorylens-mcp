# MemoryLens MCP

MemoryLens is a Model Context Protocol (MCP) server that provides on-demand .NET memory profiling with concrete, AI-actionable code fix suggestions. It integrates JetBrains dotMemory as a profiling backend and exposes memory analysis through MCP tools, enabling Claude to capture snapshots, detect memory leaks, and suggest targeted code changes based on a built-in rule engine.

## Installation

```bash
claude plugin add memorylens from gh:MarcelRoozekrans/memorylens-mcp
```

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later

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
