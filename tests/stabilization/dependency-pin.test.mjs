// ALV-N019 R03 (PD-26 = update): the transitive System.Security.Cryptography.Pkcs stays pinned to a fixed version.
//
// Run with: node --test tests/stabilization/dependency-pin.test.mjs
// GHSA-846f-f764-76mx and GHSA-2fmv-rv2c-59x7 affect Pkcs 9.0.0 to 9.0.20 and are fixed in 9.0.21. Microsoft.Data.SqlClient brings Pkcs 9.0.18; the direct reference in Alveara.Api.csproj overrides it.
// The behavioural proof is `dotnet list package --vulnerable --include-transitive` (no vulnerable package); this is the repository-level guard that the pin is not removed or lowered.

import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";

const root = path.resolve(import.meta.dirname, "..", "..");
const csproj = readFileSync(path.join(root, "src", "Alveara.Api", "Alveara.Api.csproj"), "utf8");
const FIXED = [9, 0, 21];

const versionOf = (text) => {
  const m = /<PackageReference\s+Include="System\.Security\.Cryptography\.Pkcs"\s+Version="(\d+)\.(\d+)\.(\d+)"\s*\/>/.exec(text);
  return m ? [Number(m[1]), Number(m[2]), Number(m[3])] : null;
};
const atLeast = (v, min) => { for (let i = 0; i < 3; i++) { if (v[i] !== min[i]) return v[i] > min[i]; } return true; };

test("Alveara.Api.csproj pins System.Security.Cryptography.Pkcs directly, at the first fixed version or later", () => {
  const v = versionOf(csproj);
  assert.ok(v, "the direct PackageReference to System.Security.Cryptography.Pkcs is missing: the transitive 9.0.18 (vulnerable) would resolve again");
  assert.ok(atLeast(v, FIXED), `Pkcs is pinned to ${v.join(".")}, below the fixed ${FIXED.join(".")}`);
});

test("the pin is within the 9.0 line the SQL client's dependency was resolved in (no jump to another major line)", () => {
  const v = versionOf(csproj);
  assert.deepEqual(v.slice(0, 2), [9, 0]);
});

test("the checker itself: older and malformed pins are rejected, the fixed and newer ones accepted", () => {
  const line = (v) => `<PackageReference Include="System.Security.Cryptography.Pkcs" Version="${v}" />`;
  assert.equal(atLeast(versionOf(line("9.0.18")), FIXED), false);
  assert.equal(atLeast(versionOf(line("9.0.20")), FIXED), false);
  assert.equal(atLeast(versionOf(line("9.0.21")), FIXED), true);
  assert.equal(atLeast(versionOf(line("9.0.23")), FIXED), true);
  assert.equal(atLeast(versionOf(line("10.0.13")), FIXED), true);
  assert.equal(versionOf("<PackageReference Include=\"Other\" Version=\"9.0.21\" />"), null);
  assert.equal(versionOf(line("9.0")), null);
});

test("idempotent: reading the project twice gives the same pin", () => {
  assert.deepEqual(versionOf(csproj), versionOf(readFileSync(path.join(root, "src", "Alveara.Api", "Alveara.Api.csproj"), "utf8")));
});
