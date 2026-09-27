// STORY-000 regression / course-root coexistence check, required by ALV-N001.
//
// This does not re-run STORY-000's full portal acceptance criteria (those are
// owned by the course portal). It checks the two structural facts ALV-N001's
// acceptance condition actually depends on:
//   1. The root Command Center (index.html + assets/) still exists, is not
//      replaced, and still reads .colaberry/* at runtime rather than
//      embedding hard-coded plan content.
//   2. The production Alveara app lives in its own separate project path
//      (src/alveara-client, src/Alveara.Api) and did not touch the root
//      surface to get there.
//
// Run with: node --test tests/story-000-coexistence.test.mjs

import { test } from "node:test";
import assert from "node:assert/strict";
import { existsSync, readFileSync } from "node:fs";
import path from "node:path";

const repoRoot = path.resolve(import.meta.dirname, "..");

test("root STORY-000 Command Center entry point still exists", () => {
  assert.ok(existsSync(path.join(repoRoot, "index.html")), "repo-root index.html must still exist");
  assert.ok(existsSync(path.join(repoRoot, "assets", "app.js")), "Command Center assets/app.js must still exist");
  assert.ok(existsSync(path.join(repoRoot, "assets", "styles.css")), "Command Center assets/styles.css must still exist");
});

test("root Command Center still reads .colaberry/* at runtime rather than hard-coding plan content", () => {
  const appJs = readFileSync(path.join(repoRoot, "assets", "app.js"), "utf-8");
  assert.match(appJs, /\.colaberry\/plan\.json/, "app.js must still fetch .colaberry/plan.json");
  assert.match(appJs, /\.colaberry\/progress\.json/, "app.js must still fetch .colaberry/progress.json");
  assert.match(appJs, /\.colaberry\/manifest\.json/, "app.js must still fetch .colaberry/manifest.json");
});

test("the production Alveara app lives in its own separate project paths", () => {
  assert.ok(
    existsSync(path.join(repoRoot, "src", "alveara-client", "package.json")),
    "React client must exist under src/alveara-client, not at the repo root"
  );
  assert.ok(
    existsSync(path.join(repoRoot, "src", "Alveara.Api", "Alveara.Api.csproj")),
    "C# API must exist under src/Alveara.Api, not at the repo root"
  );
});

test("the production app did not replace or rename the root Command Center", () => {
  const rootIndex = readFileSync(path.join(repoRoot, "index.html"), "utf-8");
  assert.match(rootIndex, /Alveara Dental — Command Center/, "root index.html must still be the course Command Center, not the product shell");

  const clientIndex = readFileSync(
    path.join(repoRoot, "src", "alveara-client", "index.html"),
    "utf-8"
  );
  assert.match(clientIndex, /Alveara Dental/, "the product shell has its own separate index.html under src/alveara-client");
  assert.notEqual(
    rootIndex,
    clientIndex,
    "root Command Center and product shell must be genuinely different documents"
  );
});
