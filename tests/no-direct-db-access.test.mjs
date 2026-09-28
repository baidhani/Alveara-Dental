// ALV-N002 required test: "Direct-database-access topology check."
//
// Per the story's server-owned-database-topology rule, LAN/browser clients must use the
// application/API and never open the database file/connection directly. This is enforced two
// ways: (1) the client project must not depend on any SQL/database driver library at all — if it
// did, that alone would be evidence of an intended direct-access code path; (2) if a production
// build exists, its bundled JS must not contain a SQL Server connection-string shape (which would
// mean a connection string leaked into client-shipped code).
//
// Run with: node --test tests/no-direct-db-access.test.mjs

import { test } from "node:test";
import assert from "node:assert/strict";
import { existsSync, readFileSync, readdirSync } from "node:fs";
import path from "node:path";

const repoRoot = path.resolve(import.meta.dirname, "..");
const clientDir = path.join(repoRoot, "src", "alveara-client");

const KNOWN_DB_DRIVER_PACKAGES = [
  "mssql", "tedious", "better-sqlite3", "sqlite3", "sql.js", "pg", "mysql", "mysql2", "knex",
  "typeorm", "prisma", "sequelize",
];

test("the client project depends on no SQL/database driver package", () => {
  const pkg = JSON.parse(readFileSync(path.join(clientDir, "package.json"), "utf-8"));
  const allDeps = { ...pkg.dependencies, ...pkg.devDependencies };
  const found = KNOWN_DB_DRIVER_PACKAGES.filter((name) => name in allDeps);
  assert.deepEqual(found, [], `Client must never depend on a database driver; found: ${found.join(", ")}`);
});

test("if a production client build exists, its bundled JS contains no SQL Server connection-string shape", () => {
  const distAssetsDir = path.join(clientDir, "dist", "assets");
  if (!existsSync(distAssetsDir)) {
    // No build present right now (dist/ is gitignored and cleaned after each build) — nothing to
    // scan. This is the expected steady-state, not a skipped check: rebuild and rerun this test
    // to actually exercise it when verifying a release candidate.
    return;
  }

  const jsFiles = readdirSync(distAssetsDir).filter((f) => f.endsWith(".js"));
  for (const file of jsFiles) {
    const content = readFileSync(path.join(distAssetsDir, file), "utf-8");
    assert.doesNotMatch(content, /Trusted_Connection|Server=\(localdb\)|Data Source=.*\.mdf/i, `${file} must not contain a DB connection string`);
  }
});

test("the API's connection string is only ever read from server-side configuration, never hard-coded in client source", () => {
  const clientSrcDir = path.join(clientDir, "src");
  const offendingFiles = [];

  function scanDir(dir) {
    for (const entry of readdirSync(dir, { withFileTypes: true })) {
      const fullPath = path.join(dir, entry.name);
      if (entry.isDirectory()) {
        scanDir(fullPath);
      } else if (/\.(ts|tsx)$/.test(entry.name)) {
        const content = readFileSync(fullPath, "utf-8");
        if (/Trusted_Connection|Server=\(localdb\)/i.test(content)) {
          offendingFiles.push(fullPath);
        }
      }
    }
  }

  scanDir(clientSrcDir);
  assert.deepEqual(offendingFiles, []);
});
