# Running in Docker

The repository ships a `Dockerfile`, so you can run the server without a .NET SDK
on the host. Read the [limitations](#limitations) first — profiling from a
container is more constrained than the other install methods.

## Build the image

```bash
docker build -t memorylens-mcp .
```

## Run it

The server communicates over stdio, so the container needs `-i` and must not
allocate a TTY:

```bash
docker run -i --rm --pid=host --cap-add=SYS_PTRACE \
  -v "$PWD:/workspace" \
  -v memorylens-tools:/root/.memorylens \
  memorylens-mcp
```

| Flag | Why |
|---|---|
| `--pid=host` | Without it `list_processes` sees only the container's own PIDs, so there is nothing to profile. |
| `--cap-add=SYS_PTRACE` | dotMemory attaches to a running process; that needs `ptrace`, which Docker drops by default. |
| `-v memorylens-tools:/root/.memorylens` | Persists the dotMemory CLI that `ensure_dotmemory` downloads (~hundreds of MB) so it is fetched once, not per container start. |
| `-v "$PWD:/workspace"` | `.memorylens.json` is read from the working directory, and snapshots are written there. |

## MCP client configuration

```json
{
  "mcpServers": {
    "memorylens": {
      "type": "stdio",
      "command": "docker",
      "args": [
        "run", "-i", "--rm",
        "--pid=host", "--cap-add=SYS_PTRACE",
        "-v", "/absolute/path/to/your/repo:/workspace",
        "-v", "memorylens-tools:/root/.memorylens",
        "memorylens-mcp"
      ]
    }
  }
}
```

## Call `ensure_dotmemory` first

The profiler is not baked into the image. `ensure_dotmemory` downloads the
`JetBrains.dotMemory.Console.<rid>` package from nuget.org on first use and
extracts it under `$HOME/.memorylens` — so a cold container needs network
access, and the `/root/.memorylens` volume is what stops it re-downloading on
every start.

Calling `list_processes` before `ensure_dotmemory` fails with *"Could not execute
because the specified command or file was not found"*: it shells out to
`dotnet dotmemory list-processes`, which needs a `dotnet-dotmemory` command on
`PATH`. This is not container-specific — it behaves the same way on a host
without that tool.

The image is SDK-based rather than `dotnet/runtime` because `DotMemoryToolManager`
falls back to `dotnet tool install`, which requires the SDK.

## Windows hosts

Under Git Bash / MSYS, path arguments are rewritten before Docker sees them —
`/workspace` becomes `C:/Program Files/Git/workspace`. Prefix the command with
`MSYS_NO_PATHCONV=1`, or use PowerShell:

```powershell
docker run -i --rm -v "${PWD}:/workspace" memorylens-mcp
```

## Limitations

- **The target process must be reachable.** A containerized profiler can only
  attach to processes in a PID namespace it shares. `--pid=host` covers processes
  on the Docker host; profiling a process in a *different* container additionally
  needs `--pid=container:<id>`.
- **Docker Desktop hosts are a VM.** On Windows and macOS `--pid=host` is the
  Linux VM's namespace, not your desktop's, so a .NET app running natively on
  Windows or macOS is not visible. Use the [global tool](../README.md#net-global-tool)
  or [npx](../README.md#npx-any-mcp-client) install for those.
- **Linux glibc/musl only.** dotMemory Console has no container-friendly build
  for other platforms; set `DOTMEMORY_PATH` if you supply your own.
- **Snapshots are container paths.** They land under `/workspace`, so mount a
  volume there or they vanish with `--rm`.
