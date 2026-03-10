# NuGet MCP Server Discovery — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Make MemoryLens.Mcp discoverable as an MCP server on NuGet.org.

**Architecture:** Add `.mcp/server.json` (MCP registry spec) embedded in the NuGet package, set `McpServer` package type, and auto-substitute version at pack time via MSBuild.

**Tech Stack:** MSBuild, NuGet packaging, MCP registry spec

---

### Task 1: Create `.mcp/server.json`

**Files:**
- Create: `src/MemoryLens.Mcp/.mcp/server.json`

**Step 1: Create the server.json file**

```json
{
  "$schema": "https://static.modelcontextprotocol.io/schemas/2025-12-11/server.schema.json",
  "name": "io.github.marcelroozekrans/memorylens-mcp",
  "description": "MCP server for .NET memory profiling — wraps JetBrains dotnet-dotmemory with heuristic-based analysis rules",
  "title": "MemoryLens",
  "repository": {
    "url": "https://github.com/MarcelRoozekrans/memorylens-mcp",
    "source": "github"
  },
  "version": "0.0.0",
  "packages": [
    {
      "registryType": "nuget",
      "registryBaseUrl": "https://api.nuget.org/v3/index.json",
      "identifier": "MemoryLens.Mcp",
      "version": "0.0.0",
      "runtimeHint": "dnx",
      "transport": {
        "type": "stdio"
      }
    }
  ]
}
```

**Step 2: Commit**

```bash
git add src/MemoryLens.Mcp/.mcp/server.json
git commit -m "feat: add .mcp/server.json for NuGet MCP discovery"
```

---

### Task 2: Update .csproj — package type, embed server.json, version substitution

**Files:**
- Modify: `src/MemoryLens.Mcp/MemoryLens.Mcp.csproj`

**Step 1: Add `PackageType` property**

In the `<PropertyGroup>`, add:

```xml
<PackageType>DotnetTool;McpServer</PackageType>
```

**Step 2: Add `None Include` for `.mcp/server.json`**

In the existing `<ItemGroup>` with `README.md`, add:

```xml
<None Include=".mcp\server.json" Pack="true" PackagePath=".mcp\" CopyToOutputDirectory="Never" />
```

**Step 3: Add MSBuild target for version substitution**

Add a target that copies `server.json` to the intermediate output directory and replaces `"0.0.0"` with the actual version before packing:

```xml
<Target Name="SetMcpServerVersion" BeforeTargets="GenerateNuspec">
  <PropertyGroup>
    <_McpServerJsonSource>$(MSBuildProjectDirectory)\.mcp\server.json</_McpServerJsonSource>
    <_McpServerJsonIntermediate>$(IntermediateOutputPath).mcp\server.json</_McpServerJsonIntermediate>
  </PropertyGroup>
  <Copy SourceFiles="$(_McpServerJsonSource)" DestinationFiles="$(_McpServerJsonIntermediate)" />
  <WriteLinesToFile File="$(_McpServerJsonIntermediate)"
                    Lines="$([System.IO.File]::ReadAllText('$(_McpServerJsonIntermediate)').Replace('0.0.0', '$(Version)'))"
                    Overwrite="true" />
  <ItemGroup>
    <None Remove=".mcp\server.json" />
    <None Include="$(_McpServerJsonIntermediate)" Pack="true" PackagePath=".mcp\" />
  </ItemGroup>
</Target>
```

**Step 4: Commit**

```bash
git add src/MemoryLens.Mcp/MemoryLens.Mcp.csproj
git commit -m "feat: add McpServer package type and embed .mcp/server.json in nupkg"
```

---

### Task 3: Verify the package

**Step 1: Pack with a test version**

```bash
cd src/MemoryLens.Mcp
dotnet pack -c Release -p:Version=1.2.3-test
```

Expected: Build succeeds, `.nupkg` created.

**Step 2: Inspect the nupkg**

```bash
# List contents to verify .mcp/server.json is present
unzip -l bin/Release/MemoryLens.Mcp.1.2.3-test.nupkg | grep -E "(mcp|nuspec)"
```

Expected: `.mcp/server.json` appears in the package listing.

**Step 3: Verify version was substituted**

```bash
unzip -p bin/Release/MemoryLens.Mcp.1.2.3-test.nupkg .mcp/server.json
```

Expected: Both `"version"` fields show `"1.2.3-test"`, not `"0.0.0"`.

**Step 4: Verify package type in nuspec**

```bash
unzip -p bin/Release/MemoryLens.Mcp.1.2.3-test.nupkg MemoryLens.Mcp.nuspec | grep -A2 packageType
```

Expected: Shows `DotnetTool` and `McpServer` package types.

**Step 5: Commit (if any fixes were needed)**

```bash
git add -A
git commit -m "fix: adjust MCP server packaging"
```
