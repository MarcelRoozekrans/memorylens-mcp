# syntax=docker/dockerfile:1
#
# MemoryLens MCP server.
#
# Wraps JetBrains dotMemory Console, which supports linux-x64 / linux-arm64
# (see DotMemoryAutoInstaller.GetRid), so profiling from a container works — but
# attaching a profiler to a process needs ptrace, and seeing processes outside
# the container needs the host PID namespace:
#
#   docker build -t memorylens-mcp .
#   docker run -i --rm --pid=host --cap-add=SYS_PTRACE \
#     -v "$PWD:/workspace" -v memorylens-tools:/root/.memorylens \
#     memorylens-mcp
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
# Must be the SDK image, not dotnet/runtime: DotMemoryToolManager falls back to
# `dotnet tool install -g dotnet-dotmemory`, which requires the SDK. dotMemory
# Console is glibc-linked, so this is also why the image is not Alpine-based.
FROM mcr.microsoft.com/dotnet/sdk:10.0

ENV DOTNET_NOLOGO=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    PATH="/root/.dotnet/tools:${PATH}"

# The profiler is NOT baked in: ensure_dotmemory downloads the
# JetBrains.dotMemory.Console.<rid> nupkg from nuget.org at runtime and extracts
# it under $HOME/.memorylens. Mount a volume there (see docs/docker.md) so the
# download survives --rm; a cold container needs network access for it.
COPY --from=build /app /opt/memorylens

# .memorylens.json is read from the working directory (see Program.cs), and
# snapshots land here — mount your project so both survive the container.
WORKDIR /workspace

ENTRYPOINT ["dotnet", "/opt/memorylens/MemoryLens.Mcp.dll"]
