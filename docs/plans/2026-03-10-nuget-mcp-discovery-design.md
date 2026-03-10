# NuGet MCP Server Discovery

## Goal

Make MemoryLens.Mcp show up as an MCP server on NuGet.org feed, so MCP clients (VS Code, Visual Studio, Claude Code, Cursor) can discover and install it directly.

## Changes

### 1. `.mcp/server.json`

Create `src/MemoryLens.Mcp/.mcp/server.json` following the MCP registry spec. Declares:

- Server identity (`io.github.marcelroozekrans/memorylens-mcp`)
- NuGet package reference with `registryType: "nuget"`
- `runtimeHint: "dnx"` for .NET 10 SDK
- `transport: { type: "stdio" }`
- No environment variables or package arguments (none required)

Version fields use `"0.0.0"` placeholder — substituted at pack time by MSBuild.

### 2. `McpServer` package type

Add `<PackageType>DotnetTool;McpServer</PackageType>` to `.csproj` so NuGet.org categorizes the package correctly.

### 3. Embed in package

Add `<None Include=".mcp\server.json" Pack="true" PackagePath=".mcp\" />` to include the file in the `.nupkg`.

### 4. Version substitution

Add an MSBuild target that runs before `Pack` to replace `"0.0.0"` in `server.json` with the actual `$(Version)`. This keeps `server.json` version-agnostic in source while ensuring the packed file has the correct version.

## Implementation Steps

1. Create `src/MemoryLens.Mcp/.mcp/server.json`
2. Update `src/MemoryLens.Mcp/MemoryLens.Mcp.csproj`:
   - Add `PackageType`
   - Add `None Include` for `.mcp/server.json`
   - Add MSBuild target for version substitution
3. Verify with `dotnet pack` and inspect the `.nupkg`
