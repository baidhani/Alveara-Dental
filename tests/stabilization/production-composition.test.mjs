// ALV-N019 R02 (review correction 01, decisions O-1 and PD-32): the production application is not changed by the chain, and never references it.
//
// Run with: node --test tests/stabilization/production-composition.test.mjs
// The behavioural proof (the production host resolves the real PracticeClock and has no chain route) is in src/Alveara.Api.Tests/ChainClockSeamTests.cs; this is the repository-level guard.

import { test } from "node:test";
import assert from "node:assert/strict";
import { readdirSync, readFileSync, statSync } from "node:fs";
import path from "node:path";

const root = path.resolve(import.meta.dirname, "..", "..");
const read = (rel) => readFileSync(path.join(root, rel), "utf8");

test("Program.cs still registers the real PracticeClock and knows nothing of the chain", () => {
  const program = read("src/Alveara.Api/Program.cs");
  assert.match(program, /AddSingleton<IPracticeClock>\(\s*_ => new PracticeClock\(TimeZoneInfo\.FindSystemTimeZoneById\(practiceTimeZoneId\)\)\)/);
  assert.doesNotMatch(program, /ChainHost|ChainPracticeClock|clock-mode|CHAIN_/i);
});

test("the production project does not reference the chain host, and no production source mentions it", () => {
  assert.doesNotMatch(read("src/Alveara.Api/Alveara.Api.csproj"), /ChainHost/);
  const hits = [];
  const walk = (rel) => {
    for (const name of readdirSync(path.join(root, rel))) {
      const next = path.join(rel, name), full = path.join(root, next);
      if (/^(obj|bin|Migrations)$/.test(name)) continue;
      if (statSync(full).isDirectory()) walk(next);
      else if (/\.(cs|csproj|json)$/.test(name) && /ChainHost|ChainPracticeClock/.test(readFileSync(full, "utf8"))) hits.push(next.split(path.sep).join("/"));
    }
  };
  walk("src/Alveara.Api");
  assert.deepEqual(hits, [], "production source refers to the chain host");
});

test("the chain host replaces the clock and adds the one route, and says so", () => {
  const factory = read("src/Alveara.Api.ChainHost/ChainHostFactory.cs");
  assert.match(factory, /RemoveAll<IPracticeClock>\(\)/);
  assert.match(factory, /AddSingleton<IPracticeClock>\(Clock\)/);
  assert.equal((factory.match(/services\.(Add|Remove)/g) ?? []).length, 3, "the chain host must change exactly the clock registration (remove and add) and add the one startup filter");
  assert.match(read("src/Alveara.Api.ChainHost/ClockModeEndpoint.cs"), /Route = "\/api\/chain\/clock-mode"/);
});

test("idempotent: the checks read the same files twice with the same result", () => {
  assert.equal(read("src/Alveara.Api/Program.cs"), read("src/Alveara.Api/Program.cs"));
});
