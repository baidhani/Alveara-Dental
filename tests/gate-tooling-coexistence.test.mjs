// Gate B review finding B-REV-01: the Gate B evidence tooling generates Playwright specs (src/alveara-client/gate-b/tablet-generated/*.spec.ts) that
// the declared frontend command (`npx vitest run`) must NOT collect - vitest cannot execute Playwright tests, so collecting them fails the command.
// A clean `git status` hides this because the generated files are ignored build output. This repository check generates the output exactly as the
// gate does and then asks vitest which files it would run: every one must be a product test under src/. It stays valid for any future gate-*/ tooling
// directory and for e2e/. Run: node --test tests/*.test.mjs
import { test } from "node:test";
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { existsSync, readdirSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.join(path.dirname(fileURLToPath(import.meta.url)), "..");
const client = path.join(root, "src", "alveara-client");

test("generated Gate B evidence specs exist and are not collected by the declared frontend test command", () => {
  // 1. generate the tooling output exactly as the gate does
  const gen = spawnSync(process.execPath, [path.join("gate-b", "make-tablet-specs.mjs")], { cwd: client, encoding: "utf-8" });
  assert.equal(gen.status, 0, `generator failed: ${gen.stderr}`);
  const generatedDir = path.join(client, "gate-b", "tablet-generated");
  assert.ok(existsSync(generatedDir), "the generator must have created gate-b/tablet-generated");
  const generated = readdirSync(generatedDir).filter((f) => f.endsWith(".spec.ts"));
  assert.ok(generated.length >= 7, `expected the generated tablet specs, found ${generated.length} - the check would prove nothing without them`);

  // 2. ask vitest what it would run, with the repository's own configuration (no command-line exclusions)
  const list = spawnSync("npx", ["vitest", "list", "--filesOnly"], { cwd: client, encoding: "utf-8", shell: true });
  assert.equal(list.status, 0, `vitest list failed: ${list.stderr}`);
  const files = list.stdout.split(/\r?\n/).map((l) => l.trim().replace(/\\/g, "/")).filter((l) => /\.(test|spec)\.[cm]?[tj]sx?$/.test(l));
  assert.ok(files.length > 40, `vitest should still discover the product tests, found ${files.length}`);

  // 3. only product tests under src/ may be collected
  const outsideSrc = files.filter((f) => !f.startsWith("src/"));
  assert.deepEqual(outsideSrc, [], `vitest would run files outside src/ (Playwright evidence specs fail under vitest): ${outsideSrc.join(", ")}`);
  assert.ok(!files.some((f) => f.includes("tablet-generated") || f.startsWith("gate-") || f.startsWith("e2e/")));
});
