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
| `-v /tmp:/tmp` | How the target is **found**, and where snapshots **land**. `list_processes` reads the diagnostic sockets the runtime writes to its temp directory, and a PID namespace does not share a filesystem — so without this the list is empty no matter what `--pid` says. The same mount is what makes snapshots outlive the container: they are written to `/tmp/memorylens-snapshots` inside it. |
| `--pid=host` | How the target is **attached to**. Collection attaches over EventPipe to a process's diagnostic endpoint, which requires sharing that process's PID namespace. |
| `--cap-add=SYS_PTRACE` | Attaching needs `ptrace`, which Docker drops by default. |
| `-v "$PWD:/workspace"` | Optional. `/workspace` is the working directory, and `.memorylens.json` is read from there — mount your project if you want the server to pick up your rule configuration. Nothing is written to `/workspace`. |

The first two are independent and both are required: sharing only `/tmp` lists
processes you then cannot attach to, and sharing only the PID namespace attaches
to processes you cannot discover.

If the target sets `TMPDIR`, mount that directory instead — the runtime places
the socket wherever `TMPDIR` points. Note that `TMPDIR` also moves where the
server writes snapshots, since both follow the same temp-directory lookup.

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
- **Snapshots are container paths.** They are written to
  `memorylens-snapshots` under the container's temp directory — `/tmp/memorylens-snapshots`
  unless `TMPDIR` is set — and the path `snapshot` returns is that container path,
  not a host one. The `-v /tmp:/tmp` mount above is therefore doing double duty: it
  is what keeps snapshots alive after `--rm`, on the host at
  `/tmp/memorylens-snapshots`. Without it they vanish with the container, and a
  second `docker run` cannot `analyze` an id captured by the first. Mounting
  `$PWD:/workspace` does *not* preserve them — nothing is ever written there.
