#!/usr/bin/env node
// Claude Code PreToolUse hook: allow Edit/Write only under the given path prefixes.
// Usage (in agent frontmatter): node "$CLAUDE_PROJECT_DIR/scripts/guard-paths.mjs" backend/ docs/api/ frontend/package.json
import path from "node:path";

// Entries ending in "/" are folders (prefix match); anything else is an exact file path.
const allowed = process.argv.slice(2).map((p) => p.replace(/\\/g, "/"));
let raw = "";
process.stdin.on("data", (d) => (raw += d));
process.stdin.on("end", () => {
  let input;
  try {
    input = JSON.parse(raw);
  } catch {
    process.exit(0); // not our business
  }
  const file = input?.tool_input?.file_path ?? input?.tool_input?.notebook_path;
  if (!file) process.exit(0);

  const root = process.env.CLAUDE_PROJECT_DIR || input.cwd || process.cwd();
  const rel = path.relative(root, path.resolve(root, file)).replace(/\\/g, "/");
  const ok = !rel.startsWith("..") && allowed.some((a) => (a.endsWith("/") ? rel.startsWith(a) : rel === a));
  if (ok) process.exit(0);

  process.stderr.write(
    `Blocked: this agent may only edit ${allowed.join(", ")}. "${rel}" is outside its area. ` +
      `Describe the needed change in your summary instead.\n`
  );
  process.exit(2);
});
