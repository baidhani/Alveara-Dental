import { test, expect } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { createServer } from "node:http";
import { readFileSync, existsSync } from "node:fs";
import path from "node:path";
import type { Server } from "node:http";

/**
 * The repository's ROOT Command Center (index.html + assets/ + .colaberry/* + .alveara/EXECUTION_STATUS.json), served as a static site exactly as GitHub
 * Pages would, in a real browser. Nothing is mocked: the expectations are computed from the committed data files themselves, so the page can never
 * quietly drift from them.
 *
 * Covers the two Command Center changes: (1) there is no sample mode - real data only; (2) Project Management has two views, "Portal stories"
 * (the course portal's stories) and "Engineering stories" (companion and new-production stories from the engineering ledger).
 */
const ROOT = path.resolve(process.cwd(), "..", "..");
const readJson = <T,>(rel: string) => JSON.parse(readFileSync(path.join(ROOT, rel), "utf-8")) as T;

interface LedgerRecord { storyId: string; storyType: string; status: string; num: number; parentCourseStory: string | null; attempt: string | null }
const ledger = readJson<{ records: LedgerRecord[] }>(".alveara/EXECUTION_STATUS.json");
const plan = readJson<{ stories: { id: string }[] }>(".colaberry/plan.json");
const progress = readJson<{ stories: { id: string; verification?: { state?: string } }[] }>(".colaberry/progress.json");
const engineering = ledger.records.filter((r) => r.storyType === "companion" || r.storyType === "new_production");
const STATUS_LABELS: Record<string, string> = { PLANNED: "Planned", AWAITING_REVIEW: "Awaiting review", CHANGES_REQUIRED: "Changes required", COMPLETE: "Complete", BLOCKED: "Blocked", REOPENED: "Reopened" };

/** The engineering stories attached to a portal story, computed from the ledger the same way the page documents it. */
function attached(storyId: string) {
  const byId = new Map(ledger.records.map((r) => [r.storyId, r]));
  const companions = ledger.records.filter((r) => r.parentCourseStory === storyId).sort((a, b) => a.num - b.num);
  const own = ledger.records.find((r) => r.storyId === storyId && r.storyType === "course") as (LedgerRecord & { dependencies: string[] }) | undefined;
  const prerequisites = (own?.dependencies ?? []).filter((d) => d.startsWith("ALV-")).map((d) => byId.get(d)!).filter(Boolean);
  return { companions, prerequisites };
}

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
    const type = file.endsWith(".json") ? "application/json" : file.endsWith(".js") ? "text/javascript" : file.endsWith(".css") ? "text/css" : file.endsWith(".png") ? "image/png" : "text/html";
    res.writeHead(200, { "Content-Type": type });
    res.end(readFileSync(file));
  });
  await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
  base = `http://127.0.0.1:${(server.address() as { port: number }).port}`;
});
test.afterAll(async () => { await new Promise((resolve) => server.close(resolve)); });

test.beforeEach(() => { requested.length = 0; });

async function openPm(page: import("@playwright/test").Page) {
  await page.goto(base + "/index.html");
  await page.locator('#tabs-nav button[data-tab="pm"]').click();
  await expect(page.getByRole("heading", { name: "Project Management" })).toBeVisible();
}

test.describe("the logo", () => {
  test("shows in the sidebar in place of the old text mark, loads at a usable size, and is decorative because the name sits beside it", async ({ page }) => {
    await page.goto(base + "/index.html");
    const logo = page.locator(".brand .brand-logo");
    await expect(logo).toBeVisible();
    expect(await logo.evaluate((img) => (img as HTMLImageElement).complete && (img as HTMLImageElement).naturalWidth > 0), "the image actually loaded").toBe(true);
    await expect(logo).toHaveAttribute("alt", "");
    const box = (await logo.boundingBox())!;
    expect([Math.round(box.width), Math.round(box.height)]).toEqual([40, 44]);
    await expect(page.locator(".brand .brand-text")).toContainText("Alveara Dental"); // the name the logo sits beside
    await expect(page.locator(".brand-mark")).toHaveCount(0);                          // the old "AD" square is gone
    expect(requested.some((u) => u.endsWith("assets/logo-88.png") || u.endsWith("assets/logo-176.png")), "a logo file was requested").toBe(true);
  });

  test("stays visible in the dark theme", async ({ page }) => {
    await page.goto(base + "/index.html");
    await page.getByRole("button", { name: /Dark/ }).click();
    await expect(page.locator("html")).toHaveAttribute("data-theme", "dark");
    await expect(page.locator(".brand .brand-logo")).toBeVisible();
  });

  test("the browser-tab icon is declared and each icon file is served as an image", async ({ page }) => {
    await page.goto(base + "/index.html");
    const hrefs = await page.locator('link[rel="icon"], link[rel="apple-touch-icon"]').evaluateAll((els) => els.map((e) => (e as HTMLLinkElement).getAttribute("href")));
    expect(hrefs).toEqual(expect.arrayContaining(["assets/favicon-32.png", "assets/favicon-192.png"]));
    for (const href of new Set(hrefs)) {
      const res = await page.request.get(`${base}/${href}`);
      expect(res.status(), href).toBe(200);
      expect((await res.body()).subarray(0, 4).toString("hex"), `${href} is a PNG`).toBe("89504e47");
    }
  });
});

test.describe("real data only", () => {
  test("there is no sample/real switch, no sample label on any tab, and no sample data is ever requested", async ({ page }) => {
    await page.goto(base + "/index.html");
    await expect(page.locator("#tabs-nav button").first()).toBeVisible();
    await expect(page.locator("#mode-toggle")).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Sample" })).toHaveCount(0);
    const tabIds = await page.locator("#tabs-nav button").evaluateAll((els) => els.map((e) => (e as HTMLElement).dataset.tab));
    for (const id of tabIds) {
      await page.locator(`#tabs-nav button[data-tab="${id}"]`).click();
      await expect(page.locator("#tab-content")).not.toContainText("Loading…");
      await expect(page.locator("#tab-content")).not.toContainText(/sample/i);
    }
    expect(requested.some((u) => u.includes("assets/sample")), "assets/sample is never fetched").toBe(false);
    expect(existsSync(path.join(ROOT, "assets", "sample")), "the sample data files are gone").toBe(false);
    for (const f of [".colaberry/plan.json", ".colaberry/progress.json", ".colaberry/manifest.json", ".alveara/EXECUTION_STATUS.json"])
      expect(requested.some((u) => u.endsWith(f)), `${f} is read at runtime`).toBe(true);
  });

  test("with no synced data the page says so and points to the portal, instead of offering sample data", async ({ page }) => {
    await page.route("**/.colaberry/*.json", (route) => route.fulfill({ status: 404, body: "not found" }));
    await page.goto(base + "/index.html");
    await expect(page.locator("#tab-content")).toContainText("Sync from the portal");
    await expect(page.locator("#data-stamp")).toContainText("not yet synced from the portal");
    await expect(page.locator("#tab-content")).not.toContainText(/sample/i);
  });
});

test.describe("expanding a portal story shows its attached engineering stories", () => {
  test("every portal story's row says how many engineering stories are attached, computed from the ledger", async ({ page }) => {
    await openPm(page);
    for (const story of plan.stories) {
      const { companions, prerequisites } = attached(story.id);
      const all = [...companions, ...prerequisites];
      const row = page.locator(`#pm-panel tr[data-expand="${story.id}"]`);
      if (all.length === 0) await expect(row, story.id).toContainText("None");
      else await expect(row, story.id).toContainText(`${all.length} attached · ${all.filter((r) => r.status === "COMPLETE").length} complete`);
    }
  });

  test("clicking a story expands it in place with its own facts and each attached engineering story and its status; clicking again collapses it", async ({ page }) => {
    await openPm(page);
    const story = plan.stories.find((s) => s.id === "STORY-004")!;
    const { companions, prerequisites } = attached(story.id);
    expect(companions.map((r) => r.storyId), "the ledger has ALV-004-C01 extending STORY-004").toContain("ALV-004-C01");
    expect(prerequisites.map((r) => r.storyId), "and ALV-N010 as its prerequisite").toContain("ALV-N010");

    const button = page.locator('[data-expand-btn="STORY-004"]');
    await expect(button).toHaveAttribute("aria-expanded", "false");
    await page.locator('#pm-panel tr[data-expand="STORY-004"]').click();
    await expect(button).toHaveAttribute("aria-expanded", "true");

    const panel = page.locator('[data-expanded-panel="STORY-004"]');
    await expect(panel).toBeVisible();
    await expect(panel).toContainText("As a scheduler"); // the story's own narrative
    for (const r of companions) {
      const row = panel.locator("tbody tr", { hasText: r.storyId });
      await expect(row, r.storyId).toContainText("Extends this story");
      await expect(row, r.storyId).toContainText(STATUS_LABELS[r.status] ?? r.status);
      await expect(row, r.storyId).toContainText("Companion");
    }
    for (const r of prerequisites) {
      const row = panel.locator("tbody tr", { hasText: r.storyId });
      await expect(row, r.storyId).toContainText("Must be complete before it starts");
      await expect(row, r.storyId).toContainText(STATUS_LABELS[r.status] ?? r.status);
    }
    await expect(panel.locator("tbody tr")).toHaveCount(companions.length + prerequisites.length);

    await page.locator('#pm-panel tr[data-expand="STORY-004"]').click();
    await expect(panel).toHaveCount(0);
    await expect(button).toHaveAttribute("aria-expanded", "false");
  });

  test("several stories can be open at once, and a story with nothing attached says so", async ({ page }) => {
    await openPm(page);
    const without = plan.stories.find((s) => { const a = attached(s.id); return a.companions.length + a.prerequisites.length === 0; });
    await page.locator('#pm-panel tr[data-expand="STORY-003"]').click();
    await page.locator('#pm-panel tr[data-expand="STORY-004"]').click();
    await expect(page.locator("[data-expanded-panel]")).toHaveCount(2);
    if (without) {
      await page.locator(`#pm-panel tr[data-expand="${without.id}"]`).click();
      await expect(page.locator(`[data-expanded-panel="${without.id}"]`)).toContainText("No engineering stories are attached to this story.");
    }
  });

  test("the expansion works from the keyboard and keeps focus on the story that was toggled", async ({ page }) => {
    await openPm(page);
    const button = page.locator('[data-expand-btn="STORY-003"]');
    await button.focus();
    await page.keyboard.press("Enter");
    await expect(button).toHaveAttribute("aria-expanded", "true");
    await expect(button).toBeFocused();
    await expect(page.locator('[data-expanded-panel="STORY-003"]')).toContainText("ALV-003-C01");
    await page.keyboard.press("Enter");
    await expect(button).toHaveAttribute("aria-expanded", "false");
    await expect(button).toBeFocused();
  });

  test("an attached engineering story can be opened from the expansion and shows its full details in the Engineering view", async ({ page }) => {
    await openPm(page);
    await page.locator('#pm-panel tr[data-expand="STORY-003"]').click();
    await page.locator('[data-expanded-panel="STORY-003"]').getByRole("button", { name: "ALV-003-C01" }).click();
    await expect(page.getByRole("tab", { name: /Engineering stories/ })).toHaveAttribute("aria-selected", "true");
    const detail = page.locator("#detail-panel");
    await expect(detail).toContainText("ALV-003-C01");
    await expect(detail).toContainText("Extends portal story STORY-003");
    await expect(detail).toContainText("Review decision: APPROVED");
  });
});

test.describe("Project Management: portal stories and engineering stories", () => {
  test("has two views; Portal stories is the default and shows every portal story with the verified count from progress.json", async ({ page }) => {
    await openPm(page);
    const tabs = page.getByRole("tab");
    await expect(tabs).toHaveText(["Portal stories", "Engineering stories (companions & new)"]);
    await expect(page.getByRole("tab", { name: "Portal stories" })).toHaveAttribute("aria-selected", "true");
    await expect(page.getByRole("tab", { name: /Engineering stories/ })).toHaveAttribute("aria-selected", "false");

    const rows = page.locator("#pm-panel table tbody tr");
    await expect(page.locator("#pm-panel tr[data-expand]")).toHaveCount(plan.stories.length);
    await expect(rows).toHaveCount(plan.stories.length); // none expanded yet
    const verified = progress.stories.filter((s) => s.verification?.state === "verified").length;
    await expect(page.locator("#pm-panel")).toContainText(String(verified));
    await expect(page.locator("#pm-panel")).not.toContainText("ALV-"); // engineering stories are not mixed into the portal view
    for (const s of plan.stories) await expect(rows.filter({ hasText: s.id }).first()).toBeVisible();
  });

  test("Engineering stories lists every companion and new-production story from the ledger with its real status", async ({ page }) => {
    await openPm(page);
    await page.getByRole("tab", { name: /Engineering stories/ }).click();
    await expect(page.getByRole("tab", { name: /Engineering stories/ })).toHaveAttribute("aria-selected", "true");

    const rows = page.locator("#pm-panel table tbody tr");
    await expect(rows).toHaveCount(engineering.length);
    const ids = await rows.locator("td:nth-child(2)").allInnerTexts();
    expect(ids.every((id) => id.startsWith("ALV-")), "only engineering stories are rows here").toBe(true);
    const labels: Record<string, string> = { PLANNED: "Planned", AWAITING_REVIEW: "Awaiting review", CHANGES_REQUIRED: "Changes required", COMPLETE: "Complete", BLOCKED: "Blocked", REOPENED: "Reopened" };
    for (const r of engineering.slice().sort((a, b) => a.num - b.num)) {
      const row = rows.filter({ hasText: r.storyId }).first();
      await expect(row, r.storyId).toContainText(labels[r.status] ?? r.status);
      await expect(row, r.storyId).toContainText(r.storyType === "companion" ? "Companion" : "New production");
      if (r.parentCourseStory) await expect(row, r.storyId).toContainText(r.parentCourseStory);
    }
    const summary = page.locator("#pm-panel .detail-panel").first();
    await expect(summary).toContainText(`${engineering.length} engineering stories`);
    await expect(summary).toContainText(`${engineering.filter((r) => r.status === "COMPLETE").length} complete`);
    await expect(summary).toContainText(`${engineering.filter((r) => r.status === "PLANNED").length} planned`);
    await expect(summary).toContainText(`${engineering.filter((r) => r.storyType === "companion").length} companion`);
  });

  test("an engineering story drills down to its details, and the views switch back and forth", async ({ page }) => {
    await openPm(page);
    await page.getByRole("tab", { name: /Engineering stories/ }).click();
    const first = engineering.slice().sort((a, b) => a.num - b.num).find((r) => r.status === "COMPLETE")!;
    await page.locator("#pm-panel table tbody tr", { hasText: first.storyId }).first().click();
    const detail = page.locator("#detail-panel");
    await expect(detail).toContainText(first.storyId);
    await expect(detail).toContainText("Depends on");
    await page.locator("#close-detail").click();
    await expect(page.locator("#close-detail")).toHaveCount(0);

    await page.getByRole("tab", { name: "Portal stories" }).click();
    await expect(page.locator("#pm-panel tr[data-expand]")).toHaveCount(plan.stories.length);
    await page.locator('#pm-panel tr[data-expand]').first().click(); // a portal story still opens
    await expect(page.locator("[data-expanded-panel]")).toHaveCount(1);
  });

  test("the views can be switched from the keyboard", async ({ page }) => {
    await openPm(page);
    await page.getByRole("tab", { name: "Portal stories" }).focus();
    await page.keyboard.press("Tab");
    await expect(page.getByRole("tab", { name: /Engineering stories/ })).toBeFocused();
    await page.keyboard.press("Enter");
    await expect(page.getByRole("tab", { name: /Engineering stories/ })).toHaveAttribute("aria-selected", "true");
    await expect(page.locator("#pm-panel table tbody tr")).toHaveCount(engineering.length);
  });

  test("if the engineering ledger cannot be read, that view says so and the portal view still works", async ({ page }) => {
    await page.route("**/.alveara/EXECUTION_STATUS.json", (route) => route.fulfill({ status: 404, body: "not found" }));
    await openPm(page);
    await expect(page.locator("#pm-panel tr[data-expand]")).toHaveCount(plan.stories.length);
    await page.locator('#pm-panel tr[data-expand="STORY-004"]').click();
    await expect(page.locator('[data-expanded-panel="STORY-004"]')).toContainText("could not be loaded, so the attached engineering stories cannot be shown");
    await page.getByRole("tab", { name: /Engineering stories/ }).click();
    await expect(page.locator("#pm-panel")).toContainText("could not be loaded");
    await expect(page.locator("#pm-panel table")).toHaveCount(0);
  });

  for (const theme of ["light", "dark"] as const) {
    test(`both views have no critical or serious accessibility violations (${theme})`, async ({ page }) => {
      await page.emulateMedia({ colorScheme: theme });
      await openPm(page);
      for (const view of ["Portal stories", "Engineering stories"]) {
        await page.getByRole("tab", { name: new RegExp(view) }).click();
        if (view === "Portal stories") await page.locator('#pm-panel tr[data-expand="STORY-004"]').click(); // scan with a story expanded
        await page.waitForTimeout(200);
        const results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa"]).analyze();
        const blocking = results.violations.filter((v) => v.impact === "critical" || v.impact === "serious");
        expect(blocking.map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(" ")).join(" | ")}`), `${view} (${theme})`).toEqual([]);
      }
    });
  }
});
