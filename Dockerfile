# syntax=docker/dockerfile:1
#
# MemoryLens MCP server.
#
# Collects heap data in-process over EventPipe — nothing is downloaded or
# installed at runtime. Attaching a profiler to another process still needs
# ptrace, and seeing processes outside the container needs the host PID
# namespace:
#
#   docker build -t memorylens-mcp .
#   docker run -i --rm --pid=host --cap-add=SYS_PTRACE \
#     -v /tmp:/tmp \
#     -v "$PWD:/workspace" \
#     memorylens-mcp
#
# -v /tmp:/tmp is not optional in practice: it is how `list_processes` sees the
# targets' diagnostic sockets, and it is also what keeps snapshots alive after
# --rm (see the WORKDIR note below).
#
# See docs/docker.md for MCP client configuration and the caveats.

# ---------------------------------------------------------------- build -------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# Restore against the project file alone so the (slow) restore layer survives
# source-only edits. Directory.Build.props carries the shared analyzer set.
#
# global.json is deliberately NOT copied in: it pins 10.0.400 with
# rollForward: latestPatch, and the floating sdk:10.0 tag above will
# eventually roll forward to a 10.0.5xx feature band. Copying the pin in
# would then hard-break the build ("compatible SDK version not found")
# instead of just picking up the newer SDK, as it does today.
COPY Directory.Build.props ./
COPY src/MemoryLens.Mcp/MemoryLens.Mcp.csproj src/MemoryLens.Mcp/
RUN dotnet restore src/MemoryLens.Mcp/MemoryLens.Mcp.csproj

COPY src/ src/
RUN dotnet publish src/MemoryLens.Mcp/MemoryLens.Mcp.csproj \
      --configuration Release \
      --no-restore \
      --output /app

# -------------------------------------------------------------- runtime -------
# The runtime image suffices: heap collection is in-process over EventPipe, so
# nothing is installed or shelled out to at runtime. This was previously the SDK
# image only because the deleted dotMemory installer needed `dotnet tool install`.
FROM mcr.microsoft.com/dotnet/runtime:10.0

ENV DOTNET_NOLOGO=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

# Self-contained: nothing is downloaded or installed at runtime.
COPY --from=build /app /opt/memorylens

# .memorylens.json is read from the working directory (see Program.cs), so mount
# your project here if you want the server to pick up your rule configuration.
#
# Snapshots do NOT land here. SnapshotStore is constructed with no root, so it
# writes to memorylens-snapshots under Path.GetTempPath() — /tmp/memorylens-snapshots
# unless TMPDIR is set. Mounting -v /tmp:/tmp is what makes them survive --rm.
WORKDIR /workspace

ENTRYPOINT ["dotnet", "/opt/memorylens/MemoryLens.Mcp.dll"]
