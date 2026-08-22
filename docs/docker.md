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
  -v /tmp:/tmp \
  -v "$PWD:/workspace" \
  memorylens-mcp
```

| Flag | Why |
|---|---|
| `-v /tmp:/tmp` | How the target is **found**. `list_processes` reads the diagnostic sockets the runtime writes to its temp directory, and a PID namespace does not share a filesystem — so without this the list is empty no matter what `--pid` says. |
| `--pid=host` | How the target is **attached to**. Collection attaches over EventPipe to a process's diagnostic endpoint, which requires sharing that process's PID namespace. |
| `--cap-add=SYS_PTRACE` | Attaching needs `ptrace`, which Docker drops by default. |
| `-v "$PWD:/workspace"` | `.memorylens.json` is read from the working directory, and snapshots are written there. |

The first two are independent and both are required: sharing only `/tmp` lists
processes you then cannot attach to, and sharing only the PID namespace attaches
to processes you cannot discover.

If the target sets `TMPDIR`, mount that directory instead — the runtime places
the socket wherever `TMPDIR` points.

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
        "-v", "/tmp:/tmp",
        "-v", "/absolute/path/to/your/repo:/workspace",
        "memorylens-mcp"
      ]
    }
  }
}
```

## Nothing to install

The image is self-contained: collection runs in-process over EventPipe, so
nothing is downloaded or installed at runtime and the container needs no
network access to work. `docker run` with the mounts above is enough —
there's no separate setup call before `snapshot` or `analyze` will work.

`list_processes` is a good first call to confirm the mounts are right: it
discovers targets from their diagnostic sockets, so an empty result means the
`/tmp` or `--pid` mount is wrong before you try anything else.

## Windows hosts

Under Git Bash / MSYS, path arguments are rewritten before Docker sees them —
`/workspace` becomes `C:/Program Files/Git/workspace`. Prefix the command with
`MSYS_NO_PATHCONV=1`, or use PowerShell:

```powershell
docker run -i --rm -v "${PWD}:/workspace" memorylens-mcp
```

## Limitations

- **The target process must be both discoverable and reachable.** Discovery reads
  the target's temp directory; attaching needs a shared PID namespace. For a
  process in another container that means `--pid=container:<id>` *and* a volume
  shared with that container's `/tmp` — sharing the PID namespace alone yields an
  empty list, because the socket lives on a filesystem you cannot see.
- **Docker Desktop hosts are a VM.** On Windows and macOS `--pid=host` is the
  Linux VM's namespace, not your desktop's, so a .NET app running natively on
  Windows or macOS is not visible. Use the [global tool](../README.md#net-global-tool)
  or [npx](../README.md#npx-any-mcp-client) install for those.
- **Snapshots are container paths.** They land under `/workspace`, so mount a
  volume there or they vanish with `--rm`.
