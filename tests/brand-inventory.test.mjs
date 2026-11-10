// ALV-N019 (R05 PKG-01 / PKG-15): the DentalKeep brand migration, as a repository check.
//
// Run with: node --test tests/brand-inventory.test.mjs
// docs/stabilization/BRAND_INVENTORY.json lists what deliberately keeps the Alveara spelling (each identifier pinned by its own file and literal) and the only live product-source lines
// that may still say "Alveara". This test (1) fails when a retained identifier disappears or is renamed, (2) fails when a NEW user-facing "Alveara" or old file-name prefix appears in product
// source, and (3) fails when an allowed mention no longer exists, so the list cannot go stale. Unrelated edits to a file that holds a retained identifier do not fail it.

import { test } from "node:test";
import assert from "node:assert/strict";
import { existsSync, readdirSync, readFileSync, statSync } from "node:fs";
import path from "node:path";

const root = path.resolve(import.meta.dirname, "..");
const inventory = JSON.parse(readFileSync(path.join(root, "docs", "stabilization", "BRAND_INVENTORY.json"), "utf8"));
const read = (rel) => readFileSync(path.join(root, rel), "utf8");

// A standalone product word (not part of Alveara.Api, AlveraDbContext, alveara-theme, Alveara-CSRF, ...) or an old download/backup file-name prefix.
export const OLD_BRAND = /(?<![A-Za-z0-9_.:-])Alveara(?![A-Za-z0-9_-]|\.[A-Za-z])|["'`]alveara-(?:backup|patient|readable|all-patients|recovery)/;
const SKIP_DIR = /(^|[\\/])(obj|bin|Migrations|node_modules|dist)([\\/]|$)/;
const PRODUCT_FILE = /\.(cs|ts|tsx|html)$/;
const TEST_FILE = /(\.test\.[cm]?[tj]sx?|Tests?\.cs)$/;

/** Every product-source file under the scan roots (tests and generated folders excluded). */
function productFiles(rel, out = []) {
  const full = path.join(root, rel);
  if (!existsSync(full) || SKIP_DIR.test(rel)) return out;
  if (statSync(full).isDirectory()) { for (const name of readdirSync(full)) productFiles(path.join(rel, name), out); return out; }
  if (PRODUCT_FILE.test(rel) && !TEST_FILE.test(rel)) out.push(rel.replace(/\\/g, "/"));
  return out;
}

/** The lines in `files` that carry the old brand and are not an allowed mention. Pure over (files, reader, allowed). */
export function findStrays(files, reader, allowed) {
  const strays = [];
  for (const file of files) {
    reader(file).split(/\r?\n/).forEach((line, i) => {
      if (OLD_BRAND.test(line) && !allowed.some((a) => a.file === file && line.includes(a.contains))) strays.push(`${file}:${i + 1}: ${line.trim().slice(0, 140)}`);
    });
  }
  return strays;
}

test("the inventory is well formed: unique ids, each entry has a file, a literal and a reason, newName is DentalKeep", () => {
  assert.equal(inventory.newName, "DentalKeep");
  const ids = inventory.retained.map((r) => r.id);
  assert.equal(new Set(ids).size, ids.length, "duplicate retained id");
  for (const r of inventory.retained) assert.ok(r.id && r.file && r.literal && r.reason, `incomplete entry ${JSON.stringify(r)}`);
  for (const a of inventory.allowedMentions) assert.ok(a.file && a.contains && a.reason, `incomplete allowed mention ${JSON.stringify(a)}`);
});

for (const r of inventory.retained) {
  test(`retained identifier ${r.id} is still in ${r.file}`, () => {
    assert.ok(existsSync(path.join(root, r.file)), `${r.file} is gone`);
    assert.ok(read(r.file).includes(r.literal), `${r.file} no longer contains '${r.literal}' (${r.reason}). If this was a deliberate rename, it needs a migration path and a new pre-implementation review.`);
  });
}

test("no live product source says Alveara to a user: only the allowed, retained mentions remain", () => {
  const files = inventory.scanRoots.flatMap((r) => productFiles(r));
  assert.ok(files.length > 300, `the scan should cover the product source, found ${files.length} files`);
  const strays = findStrays(files, read, inventory.allowedMentions);
  assert.deepEqual(strays, [], `a user-facing 'Alveara' or an old alveara- file-name prefix is back. Use DentalKeep, or add a justified entry to docs/stabilization/BRAND_INVENTORY.json.\n${strays.join("\n")}`);
});

test("every allowed mention still exists, so the allowance cannot go stale", () => {
  for (const a of inventory.allowedMentions) {
    const lines = read(a.file).split(/\r?\n/).filter((l) => l.includes(a.contains));
    assert.ok(lines.length > 0, `allowed mention '${a.contains}' is no longer in ${a.file}: remove it from the inventory`);
    assert.ok(lines.some((l) => OLD_BRAND.test(l)), `'${a.contains}' in ${a.file} no longer carries the old brand: remove the allowance`);
  }
});

test("the new brand is what a user sees", () => {
  assert.match(read("src/alveara-client/index.html"), /<title>DentalKeep<\/title>/);
  assert.match(read("src/Alveara.Api/Architecture/Patients/Portability/PatientExportFormat.cs"), /SystemName = "DentalKeep"/);
  assert.match(read("src/alveara-client/src/pages/LoginPage.tsx"), /access DentalKeep\./);
  assert.match(read("src/Alveara.Api/Architecture/Backup/BackupService.cs"), /\$"dentalkeep-backup-/);
});

test("the scanner flags what it should and spares what it should (synthetic lines)", () => {
  const flagged = ["Welcome to Alveara Dental", 'title="Take it out of Alveara."', 'filename = "alveara-backup-1.abk"', "`alveara-patient-${id}.json`", "from Alveara"];
  const spared = ["namespace Alveara.Api.Architecture", "class AlveraDbContext", 'localStorage.getItem("alveara-theme")', 'cookie "Alveara-CSRF"', "alveara.patient-export", "alveara:safety-changed", "ALVERA_DATA_ROOT", "Alveara.MfaSecret.v1"];
  for (const l of flagged) assert.ok(OLD_BRAND.test(l), `should flag: ${l}`);
  for (const l of spared) assert.ok(!OLD_BRAND.test(l), `should spare: ${l}`);
  assert.deepEqual(findStrays(["a.cs"], () => "Alveara Dental\nok", [{ file: "a.cs", contains: "Alveara Dental" }]), []);
  assert.equal(findStrays(["a.cs"], () => "ok\nAlveara Dental", []).length, 1);
});

test("idempotent: scanning twice gives the same answer", () => {
  const files = inventory.scanRoots.flatMap((r) => productFiles(r));
  assert.deepEqual(findStrays(files, read, inventory.allowedMentions), findStrays(files, read, inventory.allowedMentions));
});
