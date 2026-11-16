import { expect } from "@playwright/test";
import type { Browser, BrowserContext, Page } from "@playwright/test";

// ALV-N021 A2: what stages 3 (scheduling) and 4 (visit flow) of the accumulated chain share. Not a test file (playwright.chain.config.ts runs only stage-*.spec.ts).
// The chain host's injected clock starts at 2026-01-15T15:00:00Z = Thursday 09:00 Chicago (ChainHostMain default); "today" in these stages is that Thursday. Stages are separate files, so stage 4
// finds stage 3's data through the API (by name), never through shared memory.

export const DAY = "2026-01-15";
export const ADMIN = "stage0-admin";
export const ADMIN_PASSWORD = "stage0-admin-password-1!";
export const PASSWORD = "stage3-user-password-1!";
/** Stage 3 and 4 accounts (created by stage 3): key -> [username, role]. */
export const ACCOUNTS = {
  desk: ["stage3-desk", "FrontDesk"], desk2: ["stage3-desk2", "FrontDesk"], assistant: ["stage3-assistant", "Assistant"],
  dentist: ["stage3-dentist", "Dentist"], hygienist: ["stage3-hygienist", "Hygienist"], manager: ["stage3-manager", "OfficeManager"],
} as const;
export type AccountKey = keyof typeof ACCOUNTS;

export type Appt = { id: string; rowVersion: string; status: string; flowState: string; startLocal: string; endLocal: string; patientId: string; patientName: string; providerId: string; providerName: string; operatoryId: string; operatoryName: string; cancelCategory?: string | null };
export type Event = {
  eventType: string; actorUserId: string | null; detail: string | null; previousStartLocal: string | null; reason?: string | null; category?: string | null;
  // ALV-N021 A2 (F-046, M4): who did it, and where a move went
  actorName?: string | null; previousProviderName?: string | null; previousOperatoryName?: string | null; newStartLocal?: string | null; newProviderName?: string | null; newOperatoryName?: string | null;
};

export async function csrf(page: Page): Promise<string> {
  return (await (await page.request.get("/api/auth/csrf-token")).json()).token as string;
}

export async function call(page: Page, method: "post" | "put" | "get" | "delete", url: string, data?: unknown, headers: Record<string, string> = {}) {
  const h = method === "get" ? headers : { "X-CSRF-Token": await csrf(page), ...headers };
  const response = await page.request[method](url, method === "get" ? { headers: h } : { headers: h, data });
  return { status: response.status(), body: (await response.json().catch(() => ({}))) as Record<string, unknown> };
}

export async function signIn(page: Page, username: string, password: string): Promise<void> {
  await page.goto("/login");
  await page.getByLabel("Username").fill(username);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();
}

export async function session(browser: Browser, username: string, password = PASSWORD): Promise<{ context: BrowserContext; page: Page }> {
  const context = await browser.newContext();
  const page = await context.newPage();
  await signIn(page, username, password);
  return { context, page };
}

/** The practice records stage 3 created, looked up by name (stage 4 runs in a separate file). */
export type Practice = { dana: string; harper: string; op1: string; hyg1: string; exam: string; patients: Record<string, string>; users: Record<AccountKey, string> };

export async function lookUpPractice(admin: Page): Promise<Practice> {
  const get = async <T>(url: string) => (await (await admin.request.get(url)).json()) as T;
  const providers = await get<{ id: string; displayName: string }[]>("/api/config/providers");
  const operatories = await get<{ id: string; name: string }[]>("/api/config/operatories");
  const types = await get<{ id: string; name: string }[]>("/api/config/appointment-types");
  const byName = <T extends { [k: string]: string }>(list: T[], key: keyof T, name: string) => {
    const hit = list.find((x) => x[key] === name);
    expect(hit, `${name} exists`).toBeTruthy();
    return hit!.id as string;
  };
  const patients: Record<string, string> = {};
  for (const [key, q, full] of [["alice", "Alice", "Alice Anderson"], ["brian", "Brian", "Brian Baker"], ["cara", "Cara", "Cara Carter"], ["dev", "Dev", "Dev Dalton"]] as const) {
    const found = await get<{ id: string; firstName: string; lastName: string }[] | { items: { id: string; firstName: string; lastName: string }[] }>(`/api/patients?q=${q}`);
    const list = Array.isArray(found) ? found : found.items;
    const hit = list.find((p) => `${p.firstName} ${p.lastName}` === full);
    expect(hit, `${full} is registered`).toBeTruthy();
    patients[key] = hit!.id;
  }
  const accounts = await get<{ id: string; username: string }[] | { items: { id: string; username: string }[] }>("/api/auth/users");
  const userList = Array.isArray(accounts) ? accounts : accounts.items;
  const users = Object.fromEntries(Object.entries(ACCOUNTS).map(([key, [username]]) => [key, userList.find((u) => u.username === username)?.id ?? ""])) as Record<AccountKey, string>;
  return {
    dana: byName(providers, "displayName", "Dr. Dana"), harper: byName(providers, "displayName", "Harper"),
    op1: byName(operatories, "name", "Operatory 1"), hyg1: byName(operatories, "name", "Hygiene 1"), exam: byName(types, "name", "New patient exam"),
    patients, users,
  };
}

export async function book(page: Page, p: Practice, patient: string, time: string, provider: string, operatory: string, key: string, day = DAY) {
  // the server needs a key of 8-100 characters; the prefix keeps every caller's short label valid and unique to this chain
  return call(page, "post", "/api/appointments", { patientId: p.patients[patient], providerId: provider, operatoryId: operatory, appointmentTypeId: p.exam, startLocal: `${day}T${time}` }, { "Idempotency-Key": `chain-stage3-${key}` });
}

export const dayList = async (page: Page, includeAll = true) =>
  (await (await page.request.get(`/api/appointments?from=${DAY}&to=${DAY}${includeAll ? "&includeAll=true" : ""}`)).json()) as Appt[];
export const load = async (page: Page, id: string) => (await (await page.request.get(`/api/appointments/${id}`)).json()) as Appt;
export const historyOf = async (page: Page, id: string) => (await (await page.request.get(`/api/appointments/${id}/history`)).json()) as Event[];
export const move = (page: Page, id: string, target: string, rowVersion: string | null) => call(page, "post", `/api/visits/${id}/state`, { target, rowVersion });

/** Each move with the given session; the server decides each one. */
export async function walk(page: Page, id: string, states: string[]) {
  for (const s of states) {
    const r = await move(page, id, s, (await load(page, id)).rowVersion);
    expect(r.status, `${s}: ${JSON.stringify(r.body)}`).toBe(200);
  }
}

/** The appointment of a patient on the chain's day at a given start (stage 4 finds stage 3's bookings this way). */
export async function findAt(page: Page, patientName: string, time: string, status = "Scheduled") {
  const hit = (await dayList(page)).find((a) => a.patientName === patientName && a.startLocal === `${DAY}T${time}` && a.status === status);
  expect(hit, `${patientName} at ${time} (${status})`).toBeTruthy();
  return hit!;
}

/**
 * ALV-N021 A2 (owner decision 2026-11-15): moves THIS run's injected clock forward through the chain host's test-only route, with the run's control token (given by Invoke-AccumulatedChain.ps1).
 * Returns the host's record (old time, minutes, new time).
 */
export async function advanceClock(page: Page, minutes: number) {
  const token = process.env.CHAIN_CONTROL_TOKEN;
  expect(token, "CHAIN_CONTROL_TOKEN is set by the chain runner; the clock can only be moved through it").toBeTruthy();
  const r = await page.request.post("/api/chain/clock/advance", { headers: { "X-Chain-Control-Token": token! }, data: { minutes } });
  expect(r.status(), await r.text()).toBe(200);
  return (await r.json()) as { runId: string; previousUtc: string; advancedByMinutes: number; utcNow: string };
}

export const clockNow = async (page: Page) => ((await (await page.request.get("/api/chain/clock-mode")).json()) as { mode: string; utcNow: string });

export async function gotoCalendar(page: Page, view: "Day" | "Week" = "Day") {
  await page.goto("/calendar");
  await expect(page.getByRole("heading", { name: "Calendar", exact: true })).toBeVisible();
  await page.getByLabel("Date", { exact: true }).fill(DAY);   // always explicit: the page's default "today" is the browser's day, not the injected one (A3 scope)
  if (view === "Week") await page.getByRole("button", { name: "Week" }).click();
  await expect(page.locator(".cal")).toBeVisible();
}

export async function gotoBoard(page: Page) {
  await page.goto("/flow");
  await expect(page.getByRole("heading", { name: "Visit board", exact: true })).toBeVisible();
  await page.getByLabel("Date", { exact: true }).fill(DAY);
  await expect(page.getByRole("region", { name: /^Scheduled \(/ })).toBeVisible();
}

export const blockOf = (page: Page, id: string) => page.locator(`[data-appointment="${id}"]`);
export const drawerOf = (page: Page) => page.getByRole("dialog");
export const cardOf = (page: Page, id: string) => page.locator(`[data-visit="${id}"]`);
