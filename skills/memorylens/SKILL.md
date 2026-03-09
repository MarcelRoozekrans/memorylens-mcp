---
name: memorylens
description: Use when performing memory analysis on .NET applications, investigating memory leaks, or when systematic-debugging identifies a memory-related issue
---

# MemoryLens — Memory Profiling Skill

## Prerequisites

This skill requires the memorylens MCP server tools (`ensure_dotmemory`, `list_processes`, `snapshot`, `compare_snapshots`, `analyze`, `get_rules`).

Check if `ensure_dotmemory` is available as an MCP tool. If not, this skill is inert — inform the user to install the memorylens plugin.

## When to Use

- **On demand**: User requests memory analysis (`/memorylens`)
- **During debugging**: When `systematic-debugging` identifies symptoms like high memory usage, OutOfMemoryException, slow GC, or growing memory over time
- **During brainstorming**: When designing performance-sensitive features that involve caching, event systems, or long-lived objects

## Announce Line

> "MemoryLens activated. I'll profile your application's memory and suggest concrete fixes."

## Workflow

### Step 1: Ensure tooling

Call `ensure_dotmemory`. If it fails, stop and report the error.

### Step 2: Identify target

Ask the user what to profile:
- **Running process**: Call `list_processes` and let user pick
- **Launch command**: User provides a `dotnet run` command or similar
- **Both**: Attach to running OR launch — user's choice

### Step 3: Choose profiling mode

Based on the user's ask:
- **"How does my memory look?"** → Single `snapshot`
- **"Is there a leak?"** / **"What changed?"** → `compare_snapshots`
- **Unclear** → Ask: "Do you want a single snapshot of current state, or a before/after comparison to detect growth?"

### Step 4: Capture

Execute `snapshot` or `compare_snapshots` with the target parameters.

### Step 5: Analyze

Call `analyze` with the returned `snapshotId`. If user has a `.memorylens.json`, pass the `rulesPath`.

### Step 6: Apply fixes

For each finding with a `suggestion`:
1. Present the finding (rule, severity, description, evidence)
2. Show the suggested code change
3. Ask user for approval
4. Apply via Edit tool

Order: critical findings first, then high, medium, low.

### Step 7: Summary

After all findings are addressed, present a summary:
- Total findings by severity
- Fixes applied
- Remaining items (if user skipped any)

## Integration with systematic-debugging

When invoked from systematic-debugging, skip step 2 questioning — the debugger already knows the target process. Use the process context from the debugging session.

**Memory smell indicators** (trigger MemoryLens from debugging):
- `OutOfMemoryException`
- "high memory" or "memory leak" in user description
- GC pressure symptoms (frequent gen2 collections)
- Process memory growing over time

## Integration with brainstorming

When brainstorming features involving:
- Event systems → Reference ML001 (event handler leaks)
- Caching → Reference ML002 (static collection growth), ML005 (gen2 retention)
- IDisposable → Reference ML003, ML009
- Large data → Reference ML004 (LOH fragmentation), ML008 (array resizing)

Use rule knowledge to inform design questions and approach proposals.

## Red Flags

1. **Profiling IDE or tooling processes** — Never. `list_processes` excludes them, but if a user asks to attach to devenv/rider/Code, refuse and explain why.
2. **Running compare_snapshots with very long waits** — Warn if `waitAfter` > 60000ms. Long profiling sessions have overhead.
3. **Applying suggestions without user approval** — Always present and ask. Memory fixes can change behavior.
