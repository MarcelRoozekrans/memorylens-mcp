# DOTMEMORY_PATH Test Sample

This sample demonstrates how to use the modified `DotMemoryToolManager` with the `DOTMEMORY_PATH` environment variable to work with official JetBrains dotMemory CLI installations.

## Problem

The original memorylens-mcp required `dotnet-dotmemory` as a global .NET tool from NuGet feeds, but JetBrains does not distribute dotMemory CLI through public NuGet feeds as a .NET tool. This made memorylens-mcp unusable for users with official JetBrains dotMemory CLI installations from JetBrains Toolbox or direct downloads.

## Solution

The modified `DotMemoryToolManager` now supports 4 discovery modes for dotMemory CLI:

1. **Explicit path** via `DOTMEMORY_PATH` or `MEMORYLENS_DOTMEMORY_PATH` environment variable
2. **PATH discovery** - searches for `dotMemory.sh`/`dotMemory` (Linux) or `dotMemory.exe` (Windows)
3. **Local tool manifest** - supports `dotnet tool run dotnet-dotmemory`
4. **Global tool** - legacy fallback for `dotnet-dotmemory` global tool

## Usage

### Linux/macOS
```bash
export DOTMEMORY_PATH="/path/to/dotMemory.sh"
dotnet run --project samples/DotMemoryPathTest/DotMemoryPathTest.csproj
```

### Windows
```cmd
set DOTMEMORY_PATH=C:\path\to\dotMemory.exe
dotnet run --project samples\DotMemoryPathTest\DotMemoryPathTest.csproj
```

## What This Test Does

1. Sets the `DOTMEMORY_PATH` environment variable to point to your dotMemory CLI installation
2. Creates a `DotMemoryToolManager` instance
3. Calls `EnsureInstalledAsync()` to verify dotMemory CLI is available
4. Reports the result

## Expected Output

```
Testing EnsureInstalledAsync with DOTMEMORY_PATH...
IsInstalled: True
Version: 
Message: DOTMEMORY_PATH (/path/to/dotMemory.sh) is available.

✓ SUCCESS: dotMemory CLI is available via DOTMEMORY_PATH
```

## Finding Your dotMemory CLI Path

### JetBrains Toolbox (Linux)
```bash
ls ~/.local/share/JetBrains/Toolbox/apps/rider/tools/profiler/
# Look for dotMemory or dotMemory.sh
```

### JetBrains Toolbox (Windows)
```cmd
dir %LOCALAPPDATA%\JetBrains\Toolbox\apps\rider\tools\profiler\
# Look for dotMemory.exe
```

### Direct Installation
If you installed dotMemory directly from JetBrains website, the CLI is typically in the installation directory:
- Linux: `<install_dir>/bin/dotMemory.sh`
- Windows: `<install_dir>\bin\dotMemory.exe`

## Integration with memorylens-mcp

Once you have verified that `DOTMEMORY_PATH` works with this sample, you can use the same environment variable with the modified memorylens-mcp MCP server. The `ensure_dotmemory` tool will automatically detect and use the dotMemory CLI specified in `DOTMEMORY_PATH`.
