"use strict";

// Runs the launcher against a fake `dotnet` on PATH that records the directory each
// call ran in. POSIX only: the fake is a shell script.

const { test } = require("node:test");
const assert = require("node:assert");
const { spawnSync } = require("node:child_process");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");

const launcher = path.join(__dirname, "..", "bin", "memorylens-mcp.js");
const skip = process.platform === "win32" ? "needs a POSIX shell" : false;

test("SDK checks run outside the caller's directory, the server starts in it", { skip }, () => {
  const work = fs.mkdtempSync(path.join(os.tmpdir(), "ml-launcher-"));
  const project = path.join(work, "project");
  const home = path.join(work, "home");
  const bin = path.join(work, "bin");
  const log = path.join(work, "calls.log");
  fs.mkdirSync(project);
  fs.mkdirSync(bin);
  fs.mkdirSync(path.join(home, ".dotnet", "tools"), { recursive: true });

  // A global.json that pins an SDK nobody has installed, as a user project might.
  fs.writeFileSync(
    path.join(project, "global.json"),
    JSON.stringify({ sdk: { version: "10.0.100", rollForward: "disable" } })
  );

  // Fake dotnet: record cwd and args. `tool list` reports the tool as already current.
  const { version } = require("../package.json");
  fs.writeFileSync(
    path.join(bin, "dotnet"),
    `#!/bin/sh
echo "dotnet $* @ $(pwd -P)" >> "${log}"
if [ "$1" = "tool" ] && [ "$2" = "list" ]; then echo "memorylens.mcp   ${version}   memorylens-mcp"; fi
exit 0
`,
    { mode: 0o755 }
  );
  // Fake server executable the launcher resolves from the tools directory.
  fs.writeFileSync(
    path.join(home, ".dotnet", "tools", "memorylens-mcp"),
    `#!/bin/sh
echo "server @ $(pwd -P)" >> "${log}"
`,
    { mode: 0o755 }
  );

  const result = spawnSync(process.execPath, [launcher], {
    cwd: project,
    env: { ...process.env, PATH: `${bin}${path.delimiter}${process.env.PATH}`, HOME: home, USERPROFILE: home },
    encoding: "utf8",
  });
  assert.strictEqual(result.status, 0, result.stderr);

  const lines = fs.readFileSync(log, "utf8").trim().split("\n");
  const real = (p) => fs.realpathSync(p);
  const dotnetCalls = lines.filter((l) => l.startsWith("dotnet "));
  assert.ok(dotnetCalls.length >= 2, `expected the SDK checks, got: ${lines.join(" | ")}`);
  for (const line of dotnetCalls) {
    assert.ok(line.endsWith(`@ ${real(home)}`), `SDK call ran in the caller's directory: ${line}`);
  }
  assert.ok(lines.includes(`server @ ${real(project)}`), `server did not start in the caller's directory: ${lines.join(" | ")}`);
});
