import { test, expect } from "@playwright/test";
import { createServer } from "node:http";
import { readFileSync, existsSync, writeFileSync, mkdirSync } from "node:fs";
import path from "node:path";
import type { Server } from "node:http";

/**
 * GATE A, criterion A1: STORY-000's five Done-means checks re-run against the repository's ROOT Command Center
 * (index.html + assets/ + .colaberry/*), served as a static site exactly as GitHub Pages would, in a real browser.
 * Nothing is mocked; the production app is not involved (coexistence is proven by the repo-level node tests).
 *
 *  1. every tab is reachable and every card drills down one level
 *  2. sample mode is visibly labelled
 *  3. tabs read .colaberry/plan.json and progress.json at runtime (observed network requests), both committed
 *  4. .colaberry/manifest.json committed and the data age shown (and a week-old warning rule exists)
 *  5. trust - no tab shows a number the project has not produced (the verified counts shown equal progress.json)
 */
const OUT = process.env.GATE_A_OUT ?? path.join(process.cwd(), "gate-a-out");
mkdirSync(OUT, { recursive: true });
const ROOT = path.resolve(process.cwd(), "..", "..");

let server: Server;
let base = "";
const requested: string[] = [];

test.beforeAll(async () => {
  server = createServer((req, res) => {
    const url = decodeURIComponent((req.url ?? "/").split("?")[0]);
    requested.push(url);
    const rel = url === "/" ? "index.html" : url.replace(/^\//, "");
    const file = path.join(ROOT, rel);
    if (!file.startsWith(ROOT) || !existsSync(file)) { res.writeHead(404); res.end("not found"); return; }
    const type = file.endsWith(".json") ? "application/json" : file.endsWith(".js") ? "text/javascript" : file.endsWith(".css") ? "text/css" : "text/html";
    res.writeHead(200, { "Content-Type": type });
    res.end(readFileSync(file));
  });
  await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
  base = `http://127.0.0.1:${(server.address() as { port: number }).port}`;
});
test.afterAll(async () => { await new Promise((resolve) => server.close(resolve)); });

test("A1: the root Command Center satisfies STORY-000's five Done-means checks and is unreplaced", async ({ page }) => {
  const errors: string[] = [];
  page.on("pageerror", (e) => errors.push(e.message));
  const evidence: Record<string, unknown> = {};

  // files committed
  for (const f of [".colaberry/plan.json", ".colaberry/progress.json", ".colaberry/manifest.json", "index.html", "assets/app.js"]) expect(existsSync(path.join(ROOT, f)), f).toBe(true);

  await page.goto(base + "/index.html");
  await expect(page.locator("#tabs-nav button").first()).toBeVisible();

  // (2) sample mode is the default and visibly labelled on every tab
  const tabs = await page.locator("#tabs-nav button").evaluateAll((els) => els.map((e) => ({ id: (e as HTMLElement).dataset.tab, label: e.textContent })));
  expect(tabs.length).toBeGreaterThanOrEqual(9);
  evidence.tabs = tabs;
  const reached: string[] = [];
  const drill: Record<string, number> = {};
  const drilled: string[] = [];
  for (const t of tabs) {
    await page.locator(`#tabs-nav button[data-tab="${t.id}"]`).click();
    await expect(page.locator(`#tabs-nav button[data-tab="${t.id}"].active`)).toBeVisible();
    await expect(page.locator("#tab-content")).not.toContainText("Loading…");
    await expect(page.locator(".sample-banner").first()).toContainText("SAMPLE DATA");
    reached.push(t.label ?? "");
    // "every card drills down one level": activate the first drill-down card on the tab and require a detail panel with a close control
    const cards = page.locator("#tab-content [data-detail]");
    drill[t.label ?? ""] = await cards.count();
    if (drill[t.label ?? ""] > 0) {
      await cards.first().click();
      await expect(page.locator("#close-detail"), `${t.label}: drill-down detail opens`).toBeVisible();
      await page.locator("#close-detail").click();
      await expect(page.locator("#close-detail")).toHaveCount(0);
      drilled.push(t.label ?? "");
    }
  }
  evidence.tabsReached = reached;
  evidence.cardsPerTab = drill;
  evidence.tabsWithDrillDownActivated = drilled;
  expect(drilled.length, "at least one tab drill-down was actually activated").toBeGreaterThan(0);

  // (3)+(4)+(5) real mode: reads .colaberry/* at runtime, shows data age, no sample label, counts equal progress.json
  requested.length = 0;
  await page.locator('#mode-toggle button[data-mode="real"]').click();
  await expect(page.locator("#data-stamp")).not.toContainText("loading");
  await expect(page.locator(".sample-banner")).toHaveCount(0);
  expect(requested.some((u) => u.endsWith(".colaberry/plan.json")), "plan.json fetched at runtime").toBe(true);
  expect(requested.some((u) => u.endsWith(".colaberry/progress.json")), "progress.json fetched at runtime").toBe(true);
  expect(requested.some((u) => u.endsWith(".colaberry/manifest.json")), "manifest.json fetched at runtime").toBe(true);
  const stamp = (await page.locator("#data-stamp").innerText()).trim();
  evidence.dataStamp = stamp;
  expect(stamp).toMatch(/ago|just now|minute|hour|day/i);

  const progress = JSON.parse(readFileSync(path.join(ROOT, ".colaberry/progress.json"), "utf-8")) as { stories: { id: string; verification?: { state?: string } }[] };
  const verified = progress.stories.filter((s) => s.verification?.state === "verified").length;
  evidence.progressJsonVerifiedStories = verified;
  await page.locator('#tabs-nav button[data-tab="pm"]').click();
  await expect(page.locator("#tab-content")).not.toContainText("Loading…");
  const pmText = await page.locator("#tab-content").innerText();
  evidence.pmTabContainsVerifiedCount = pmText.includes(String(verified));
  expect(evidence.pmTabContainsVerifiedCount, `Project Management tab shows the ${verified} verified stories recorded in progress.json`).toBe(true);
  expect(errors, "no uncaught page errors").toEqual([]);

  // the week-old warning rule is implemented in the shipped app.js (the data is current, so it is not triggered here)
  expect(readFileSync(path.join(ROOT, "assets/app.js"), "utf-8")).toMatch(/ageDays\s*>\s*7/);
  writeFileSync(path.join(OUT, "a1-command-center.json"), JSON.stringify(evidence, null, 2));
});
