import { test, expect } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { createServer } from "node:http";
import { readFileSync, existsSync } from "node:fs";
import path from "node:path";
import type { Server } from "node:http";

/**
 * The Command Center's Architecture & Execution Map tab, in a real browser against the repository root served as a static site exactly as GitHub Pages would.
 * Nothing is mocked and nothing is hard-coded: every expectation is computed from the committed data files (.alveara/EXECUTION_STATUS.json,
 * .alveara/story_catalog.json, .colaberry/plan.json, .colaberry/progress.json), so the map can never quietly drift from them.
 *
 * Also guards the catalog itself: every ledger story needs an entry and a title (from the plan or the catalog), and no entry may be orphaned, so adding a
 * story to the ledger without describing it fails here instead of silently going missing from the map.
 */
const ROOT = path.resolve(process.cwd(), "..", "..");
const readJson = <T,>(rel: string) => JSON.parse(readFileSync(path.join(ROOT, rel), "utf-8")) as T;

interface LedgerRecord { storyId: string; storyType: string; status: string; num: number; phase: string; qualityGateContribution: string[]; parentCourseStory: string | null }
const ledger = readJson<{ records: LedgerRecord[] }>(".alveara/EXECUTION_STATUS.json");
const catalog = readJson<{ stories: { storyId: string; title?: string; subsystem: string }[] }>(".alveara/story_catalog.json");
const plan = readJson<{ stories: { id: string; title: string }[] }>(".colaberry/plan.json");
const records = ledger.records.slice().sort((a, b) => a.num - b.num);
const planTitles = new Map(plan.stories.map((s) => [s.id, s.title]));
const catalogById = new Map(catalog.stories.map((s) => [s.storyId, s]));

let server: Server;
let base = "";
let hideCatalog = false;
// a test can change what a data file says for the page under test, to prove the map follows the data without any change to the page
let overrides: Record<string, (json: any) => void> = {};

test.beforeAll(async () => {
  server = createServer((req, res) => {
    const url = decodeURIComponent((req.url ?? "/").split("?")[0]);
    const rel = url === "/" ? "index.html" : url.replace(/^\//, "");
    const file = path.join(ROOT, rel);
    if (!file.startsWith(ROOT) || !existsSync(file) || (hideCatalog && rel === ".alveara/story_catalog.json")) { res.writeHead(404); res.end("not found"); return; }
    const type = file.endsWith(".json") ? "application/json" : file.endsWith(".js") ? "text/javascript" : file.endsWith(".css") ? "text/css" : file.endsWith(".png") ? "image/png" : "text/html";
    res.writeHead(200, { "Content-Type": type });
    if (overrides[rel]) {
      const changed = JSON.parse(readFileSync(file, "utf-8"));
      overrides[rel](changed);
      res.end(JSON.stringify(changed));
      return;
    }
    res.end(readFileSync(file));
  });
  await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
  base = `http://127.0.0.1:${(server.address() as { port: number }).port}`;
});
test.afterAll(async () => { await new Promise((resolve) => server.close(resolve)); });
test.beforeEach(() => { hideCatalog = false; overrides = {}; });

async function openMap(page: import("@playwright/test").Page) {
  await page.goto(base + "/index.html");
  await page.locator('#tabs-nav button[data-tab="map"]').click();
  await expect(page.getByRole("heading", { name: "Architecture & Execution Map" })).toBeVisible();
}

test.describe("the story catalog", () => {
  test("describes every ledger story exactly once, with no orphans, and every story ends up with a title", () => {
    const ids = records.map((r) => r.storyId);
    expect(catalog.stories.map((s) => s.storyId).sort()).toEqual([...ids].sort());
    for (const r of records) {
      const entry = catalogById.get(r.storyId)!;
      expect(entry.subsystem.trim().length, `${r.storyId} subsystem`).toBeGreaterThan(0);
      expect(planTitles.get(r.storyId) ?? entry.title ?? "", `${r.storyId} title`).not.toBe("");
    }
  });

  test("never duplicates a title the portal's plan already owns", () => {
    for (const s of catalog.stories) if (planTitles.has(s.storyId)) expect(s.title, `${s.storyId} is in the plan, so its title is read from there`).toBeUndefined();
  });
});

test.describe("Architecture Map tab", () => {
  test("draws one card per ledger story, in the ledger's phases, with titles from the plan or the catalog", async ({ page }) => {
    await openMap(page);
    await expect(page.locator("[data-map-story]")).toHaveCount(records.length);
    await expect(page.getByText(`${records.length} visible stories`)).toBeVisible();
    for (const r of [records[0], records.find((x) => x.storyType === "companion")!, records.at(-1)!]) {
      const card = page.locator(`[data-map-story="${r.storyId}"]`);
      await expect(card).toContainText(planTitles.get(r.storyId) ?? catalogById.get(r.storyId)!.title!);
      await expect(card).toContainText(catalogById.get(r.storyId)!.subsystem);
    }
    const phases = [...new Set(records.map((r) => r.phase))];
    await expect(page.locator(".map-phase h3")).toHaveCount(phases.length);
  });

  test("shows each story's delivery state as a word, taken from the ledger, and the gate from the ledger", async ({ page }) => {
    await openMap(page);
    const labels: Record<string, string> = { PLANNED: "Planned", AWAITING_REVIEW: "Awaiting review", CHANGES_REQUIRED: "Changes required", COMPLETE: "Complete", BLOCKED: "Blocked", REOPENED: "Reopened" };
    const complete = records.find((r) => r.status === "COMPLETE")!;
    await expect(page.locator(`[data-map-story="${complete.storyId}"] .map-state`)).toHaveText("Complete");
    const planned = records.find((r) => r.status === "PLANNED" && r.storyType !== "course")!;
    await expect(page.locator(`[data-map-story="${planned.storyId}"] .map-state`)).toHaveText(labels[planned.status]);
    for (const r of records.filter((x) => x.qualityGateContribution.length).slice(0, 5)) await expect(page.locator(`[data-map-story="${r.storyId}"]`)).toContainText(r.qualityGateContribution[0]);
    const completeCount = records.filter((r) => r.status === "COMPLETE").length;
    await expect(page.locator(".map-stats")).toContainText(`${completeCount} complete`);
  });

  test("filters change what is shown and reset restores it, keeping keyboard focus on the filter", async ({ page }) => {
    await openMap(page);
    const type = page.locator("#map-filter-type");
    await type.selectOption("Portal story");
    const portal = records.filter((r) => r.storyType === "course").length;
    await expect(page.locator("[data-map-story]")).toHaveCount(portal);
    await expect(page.getByText(`${portal} visible stories`)).toBeVisible();
    await expect(type).toBeFocused();
    await page.locator("#map-filter-state").selectOption("Complete");
    const both = records.filter((r) => r.storyType === "course" && r.status === "COMPLETE").length;
    await expect(page.locator("[data-map-story]")).toHaveCount(both);
    await page.getByRole("button", { name: "Reset filters" }).click();
    await expect(page.locator("[data-map-story]")).toHaveCount(records.length);
  });

  test("the legend lists exactly the states and story types in the data, and each key filters the map and clears on a second press", async ({ page }) => {
    await openMap(page);
    const stateLabels: Record<string, string> = { PLANNED: "Planned", AWAITING_REVIEW: "Awaiting review", CHANGES_REQUIRED: "Changes required", COMPLETE: "Complete", BLOCKED: "Blocked", REOPENED: "Reopened" };
    const typeLabels: Record<string, string> = { course: "Portal story", companion: "Engineering companion", new_production: "Engineering story" };
    const expected = new Set([...records.map((r) => stateLabels[r.status]), ...records.map((r) => typeLabels[r.storyType])]);
    const legend = page.getByRole("group", { name: /Legend/ });
    const buttons = legend.getByRole("button");
    await expect(buttons).toHaveCount(expected.size);

    const complete = legend.locator('[data-map-legend="state"][data-map-value="Complete"]');
    await expect(complete).toHaveAttribute("aria-pressed", "false");
    await complete.click();
    await expect(page.locator("[data-map-story]")).toHaveCount(records.filter((r) => r.status === "COMPLETE").length);
    await expect(complete).toHaveAttribute("aria-pressed", "true");
    await expect(complete).toBeFocused();
    await expect(page.locator("#map-filter-state")).toHaveValue("Complete");

    const companion = legend.locator('[data-map-legend="type"][data-map-value="Engineering companion"]');
    await companion.click();
    await expect(page.locator("[data-map-story]")).toHaveCount(records.filter((r) => r.status === "COMPLETE" && r.storyType === "companion").length);

    await complete.click();                                          // pressing it again clears that filter only
    await expect(complete).toHaveAttribute("aria-pressed", "false");
    await expect(page.locator("[data-map-story]")).toHaveCount(records.filter((r) => r.storyType === "companion").length);
    await expect(companion).toHaveAttribute("aria-pressed", "true");
  });

  test("a legend key can be used from the keyboard", async ({ page }) => {
    await openMap(page);
    const planned = page.locator('[data-map-legend="state"][data-map-value="Planned"]');
    await planned.focus();
    await page.keyboard.press("Enter");
    await expect(page.locator("[data-map-story]")).toHaveCount(records.filter((r) => r.status === "PLANNED").length);
    await expect(planned).toBeFocused();
  });

  test("a story opens a dialog with its details from the ledger, closes with Escape and returns focus to the card", async ({ page }) => {
    await openMap(page);
    const r = records.find((x) => x.status === "COMPLETE" && x.storyType === "course")!;
    const card = page.locator(`[data-map-story="${r.storyId}"]`);
    await card.click();
    const dialog = page.locator("#map-dialog");
    await expect(dialog).toBeVisible();
    await expect(dialog).toContainText(r.storyId);
    await expect(dialog).toContainText(`#${r.num}`);
    await expect(dialog).toContainText(r.phase);
    await page.keyboard.press("Escape");
    await expect(dialog).toBeHidden();
    await expect(card).toBeFocused();
  });

  test("follows the data: when the ledger says a story moved, the card, the counts and the state filter move with it, with no change to the page", async ({ page }) => {
    const target = records.find((r) => r.status === "PLANNED" && r.storyType === "new_production")!;
    overrides[".alveara/EXECUTION_STATUS.json"] = (l) => { l.records.find((r: { storyId: string }) => r.storyId === target.storyId).status = "AWAITING_REVIEW"; };
    await openMap(page);
    await expect(page.locator(`[data-map-story="${target.storyId}"] .map-state`)).toHaveText("Awaiting review");
    await page.locator("#map-filter-state").selectOption("Awaiting review");
    await expect(page.locator("[data-map-story]")).toHaveCount(records.filter((r) => r.status === "AWAITING_REVIEW").length + 1);
    await expect(page.locator(`[data-map-story="${target.storyId}"]`)).toBeVisible();
  });

  test("a portal story whose criteria have started to pass shows as in progress with the count from the progress file", async ({ page }) => {
    const story = records.find((r) => r.storyType === "course" && r.status === "PLANNED")!;
    overrides[".colaberry/progress.json"] = (p) => {
      const entry = p.stories.find((x: { id: string }) => x.id === story.storyId);
      entry.criteria[0].passed = true;
      entry.criteria[1].passed = true;
    };
    await openMap(page);
    const card = page.locator(`[data-map-story="${story.storyId}"]`);
    await expect(card.locator(".map-state")).toHaveText("In progress");
    await expect(card).toContainText("2 of ");
    await expect(card).toContainText("criteria");
  });

  test("the architecture overview opens and closes", async ({ page }) => {
    await openMap(page);
    const toggle = page.getByRole("button", { name: "Show architecture" });
    await expect(toggle).toHaveAttribute("aria-expanded", "false");
    await toggle.click();
    await expect(page.getByRole("button", { name: "Hide architecture" })).toHaveAttribute("aria-expanded", "true");
    await expect(page.locator(".map-layer")).toHaveCount(3);
  });

  test("when the catalog cannot be read the map says so and still draws every ledger story", async ({ page }) => {
    hideCatalog = true;
    await openMap(page);
    await expect(page.getByRole("status").filter({ hasText: "story catalog" })).toBeVisible();
    await expect(page.locator("[data-map-story]")).toHaveCount(records.length);
    await expect(page.locator(`[data-map-story="${records[0].storyId}"]`)).toContainText("Unclassified");
  });

  for (const theme of ["light", "dark"] as const) {
    test(`has no serious or critical accessibility violations in ${theme} mode, with the dialog open`, async ({ page }) => {
      await page.addInitScript((t) => localStorage.setItem("alveara-theme", t), theme);
      await openMap(page);
      await page.getByRole("button", { name: "Show architecture" }).click();
      let results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa"]).analyze();
      expect(results.violations.filter((v) => v.impact === "serious" || v.impact === "critical").map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(" ")).join(", ")}`)).toEqual([]);
      await page.locator("[data-map-story]").first().click();
      await expect(page.locator("#map-dialog")).toBeVisible();
      results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa"]).analyze();
      expect(results.violations.filter((v) => v.impact === "serious" || v.impact === "critical").map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(" ")).join(", ")}`)).toEqual([]);
    });
  }
});
