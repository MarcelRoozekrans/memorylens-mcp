# memorylens-mcp

npm launcher for [MemoryLens MCP](https://github.com/MarcelRoozekrans/memorylens-mcp) — on-demand
.NET memory profiling with concrete, AI-actionable code fix suggestions, wrapping JetBrains
dotMemory with a heuristic-based rule engine.

```bash
npx -y memorylens-mcp
```

This package contains no server code. It is a thin shim that ensures the
[`MemoryLens.Mcp`](https://www.nuget.org/packages/MemoryLens.Mcp) .NET global tool is installed at
the matching version, then execs it. **The .NET 10 SDK must be on `PATH`.**

## MCP client config

```json
{
  "mcpServers": {
    "memorylens": {
      "type": "stdio",
      "command": "npx",
      "args": ["-y", "memorylens-mcp"]
    }
  }
}
```

The server downloads and caches `dotnet-dotmemory` on first use; see the
[main README](https://github.com/MarcelRoozekrans/memorylens-mcp#readme) for supported platforms,
cache locations, and manual fallback discovery.

If you already have the .NET SDK and prefer no npm indirection, install the tool directly:

```bash
dotnet tool install -g MemoryLens.Mcp
```

MIT licensed.
