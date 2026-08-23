# memorylens-mcp

npm launcher for [MemoryLens MCP](https://github.com/MarcelRoozekrans/memorylens-mcp) — on-demand
.NET memory profiling with concrete, AI-actionable code fix suggestions, collecting heap
snapshots in-process via EventPipe with nothing to install.

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

The server collects heap data in-process over EventPipe — nothing is downloaded or installed; see the
[main README](https://github.com/MarcelRoozekrans/memorylens-mcp#readme) for how collection works.

If you already have the .NET SDK and prefer no npm indirection, install the tool directly:

```bash
dotnet tool install -g MemoryLens.Mcp
```

MIT licensed.
