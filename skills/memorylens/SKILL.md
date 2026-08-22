---
name: memorylens
description: Use when performing memory analysis on .NET applications, investigating memory leaks, or when systematic-debugging identifies a memory-related issue
---

# MemoryLens — Memory Profiling Skill

## Prerequisites

This skill requires the memorylens MCP server tools (`list_processes`, `snapshot`, `compare_snapshots`, `analyze`, `get_rules`). Collection happens in-process over EventPipe, so there is nothing to install or verify before profiling — if the MCP tools aren't available at all, inform the user to install the memorylens plugin.

## When to Use

- **On demand**: User requests memory analysis (`/memorylens`)
- **During debugging**: When `systematic-debugging` identifies symptoms like high memory usage, OutOfMemoryException, slow GC, or growing memory over time
- **During brainstorming**: When designing performance-sensitive features that involve caching, event systems, or long-lived objects

## Announce Line

> "MemoryLens activated. I'll profile your application's memory and suggest concrete fixes."

## Workflow

### Step 1: Identify target

MemoryLens attaches to an already-running .NET process. It cannot launch one.

- Call `list_processes` and let the user pick, or take a pid the user supplies.
- If the app is not running yet, ask the user to start it first, then re-run `list_processes`.
- `snapshot` and `compare_snapshots` both require a `pid`. Do not pass `command` — the
  parameter is accepted for compatibility but is not implemented, and a call without a
  pid returns an error.

### Step 2: Choose profiling mode

Based on the user's ask:
- **"How does my memory look?"** → Single `snapshot`
- **"Is there a leak?"** / **"What changed?"** → `compare_snapshots`
- **Unclear** → Ask: "Do you want a single snapshot of current state, or a before/after comparison to detect growth?"

### Step 3: Capture

Execute `snapshot` or `compare_snapshots` with the target parameters.

### Step 4: Analyze

Call `analyze` with the returned `snapshotId` and nothing else — the id is all it needs.

Rule configuration is not a parameter. The server reads `.memorylens.json` from its own
working directory once at startup; changing that file needs a server restart to take
effect. Use `get_rules` to see which rules are currently active.

### Step 5: Apply fixes

For each finding with a `suggestion`:
1. Present the finding (rule, severity, description, evidence)
2. Show the suggested code change
3. Ask user for approval
4. Apply via Edit tool

Order: critical findings first, then high, medium, low.

### Step 6: Summary

After all findings are addressed, present a summary:
- Total findings by severity
- Fixes applied
- Remaining items (if user skipped any)

## Integration with systematic-debugging

When invoked from systematic-debugging, skip step 1 questioning — the debugger already knows the target process. Use the process context from the debugging session.

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
2. **Running compare_snapshots with very long waits** — `delaySeconds` is in *seconds* and defaults to 10. Warn if the user asks for more than 60. Long profiling sessions have overhead.
3. **Applying suggestions without user approval** — Always present and ask. Memory fixes can change behavior.
