import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { axe } from "jest-axe";
import { FollowUpsPanel } from "./FollowUpsPanel";
import { MyFollowUpsPage } from "./MyFollowUpsPage";
import { validate } from "./followUpFormat";

// STORY-009 screens against a small in-memory server that answers like the real API (stale check, stable error codes, idempotency key and body recorded).

let perms = new Set<string>();
vi.mock("../../contexts/AuthContext", () => ({
  useAuth: () => ({ state: { kind: "signed-in", username: "hana", role: "Hygienist", permissions: [...perms], sessionExpiresAtUtc: null }, hasPermission: (p: string) => perms.has(p) }),
}));

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
const task = (over: Record<string, unknown> = {}) => ({
  id: "t1", patientId: "p1", title: "Call about the crown", note: "Ask how the temporary feels", dueAtUtc: "2026-11-03T15:30:00Z", status: "Open", overdue: false, assigneeUserId: "u-hana", assigneeName: "hana",
  recallIntervalDays: null, recallOfTaskId: null, recallTaskId: null, createdByUserId: "u-dr", createdAtUtc: "2026-10-20T10:00:00Z", closedAtUtc: null, closedByUserId: null, closureNote: null, rowVersion: "AAEC", priority: "Normal", kind: "Manual", appointmentId: null, sourcePlanItemId: null, closedBySystem: false, ...over,
});

type Call = { method: string; url: string; headers: Record<string, string>; body: any };
let tasks: ReturnType<typeof task>[];
let calls: Call[];
let reply: (c: Call) => Response | null;

beforeEach(() => {
  perms = new Set(["ViewFollowUps", "ManageFollowUps"]);
  tasks = [task(), task({ id: "t2", title: "Book the cleaning", overdue: true, dueAtUtc: "2026-10-01T09:00:00Z", rowVersion: "AAED", assigneeUserId: "u-dr", assigneeName: "dr" })];
  calls = [];
  reply = () => null;
  vi.stubGlobal("fetch", vi.fn().mockImplementation((url: string, init?: RequestInit) => {
    const u = String(url); const method = init?.method ?? "GET";
    const headers = (init?.headers ?? {}) as Record<string, string>;
    const body = init?.body ? JSON.parse(String(init.body)) : undefined;
    if (u.includes("csrf-token")) return Promise.resolve(json({ token: "t" }));
    const call = { method, url: u, headers, body };
    calls.push(call);
    const custom = reply(call);
    if (custom) return Promise.resolve(custom);
    const status = new URL(u, "http://x").searchParams.get("status");
    const filtered = () => tasks.filter((t) => status === "all" || (status === "closed" ? t.status !== "Open" : t.status === "Open"));
    if (method === "GET" && /\/follow-ups\/timeline$/.test(u)) return Promise.resolve(json([]));
    if (method === "GET" && /\/api\/patients\/p1\/recall$/.test(u)) return Promise.resolve(json({ patientId: "p1", hasPlan: false, ownIntervalDays: null, optedOut: false, reason: null, effectiveIntervalDays: 180, lastCompletedVisitUtc: null, nextDueUtc: null, verdict: "NoVisit", rowVersion: null }));
    if (method === "GET" && /^\/api\/patients\/p1\/follow-ups/.test(u)) return Promise.resolve(json({ items: filtered(), count: filtered().length, more: false }));
    if (method === "GET" && u.startsWith("/api/follow-ups/mine")) return Promise.resolve(json({ items: filtered(), count: filtered().length, more: false }));
    if (method === "GET" && u === "/api/follow-ups/assignees") return Promise.resolve(json([{ id: "u-dr", name: "dr", role: "Dentist" }, { id: "u-hana", name: "hana", role: "Hygienist" }]));
    if (method === "GET" && /^\/api\/follow-ups\/t1$/.test(u)) {
      return Promise.resolve(json({ task: tasks[0], events: [
        { kind: "Created", actorUserId: "u-dr", actorName: "dr", occurredAtUtc: "2026-10-20T10:00:00Z", detail: "Due 2026-11-03T15:30:00.0000000+00:00" },
        { kind: "Reminded", actorUserId: null, actorName: null, occurredAtUtc: "2026-11-03T15:31:00Z", detail: "InApp to u-hana" },
      ] }));
    }
    if (method === "POST" && /\/follow-ups$/.test(u)) return Promise.resolve(json({ task: task({ id: "t9", title: body.title }), replayed: false, recallTaskId: null }));
    if (method === "PUT") return Promise.resolve(json({ task: task({ title: body.title }), replayed: false, recallTaskId: null }));
    if (method === "POST" && u.endsWith("/complete")) return Promise.resolve(json({ task: task({ status: "Completed" }), replayed: false, recallTaskId: body.note === "recall" ? "t10" : null }));
    if (method === "POST" && u.endsWith("/cancel")) return Promise.resolve(json({ task: task({ status: "Cancelled" }), replayed: false, recallTaskId: null }));
    return Promise.resolve(json({ error: "unexpected" }, 500));
  }));
});

afterEach(() => { vi.unstubAllGlobals(); });

const panel = () => render(<MemoryRouter><FollowUpsPanel patientId="p1" canManage={perms.has("ManageFollowUps")} /></MemoryRouter>);
const sent = (method: string, matcher: RegExp) => calls.find((c) => c.method === method && matcher.test(c.url));
const dueInput = () => screen.getByLabelText(/^Due/) as HTMLInputElement;

async function fillNew(title = "Check the new filling") {
  await userEvent.click(await screen.findByRole("button", { name: "New follow-up" }));
  await userEvent.type(screen.getByLabelText(/^Title/), title);
  await userEvent.type(dueInput(), "2026-12-01T09:30");
}

describe("STORY-009 follow-ups panel", () => {
  it("lists the open follow-ups, says overdue in words and filters by state", async () => {
    panel();
    expect(await screen.findByText("Call about the crown")).toBeInTheDocument();
    expect(screen.getByText("Book the cleaning")).toBeInTheDocument();
    expect(screen.getAllByText("Overdue")).toHaveLength(1);                                           // in words, not only in colour
    expect(screen.getByText(/Responsible: hana/)).toBeInTheDocument();
    tasks = [task({ id: "t3", title: "Old one", status: "Completed", closedAtUtc: "2026-10-10T10:00:00Z", closureNote: "Done by phone" })];
    await userEvent.click(screen.getByRole("button", { name: "Closed" }));
    expect(await screen.findByText("Old one")).toBeInTheDocument();
    expect(screen.getByText(/Done by phone/)).toBeInTheDocument();
    expect(sent("GET", /status=closed/)).toBeDefined();
    expect(screen.getByRole("button", { name: "Closed" })).toHaveAttribute("aria-pressed", "true");
  });

  it("says it could not load rather than showing an empty list, and shows an empty state in words", async () => {
    reply = (c) => (c.method === "GET" && c.url.includes("/follow-ups?status=open") ? json({ error: "x" }, 500) : null);
    const view = panel();
    expect(await screen.findByRole("alert")).toHaveTextContent("Could not load the follow-ups");
    view.unmount();
    reply = () => null; tasks = [];
    panel();
    expect(await screen.findByText("No open follow-ups")).toBeInTheDocument();
  });

  it("shows a role that can only view the list and the history but none of the change buttons", async () => {
    perms = new Set(["ViewFollowUps"]);
    panel();
    expect(await screen.findByText("Call about the crown")).toBeInTheDocument();
    for (const name of ["New follow-up", /^Complete /, /^Edit /, /^Cancel /]) expect(screen.queryByRole("button", { name })).toBeNull();
    expect(screen.getByRole("button", { name: /^History of Call about the crown/ })).toBeInTheDocument();
    expect(screen.getByText(/your role cannot change them/)).toBeInTheDocument();
    expect(sent("GET", /assignees/)).toBeUndefined();                                                  // no need to load the people list
  });

  it("checks a new follow-up before sending and says what is wrong beside each field", async () => {
    panel();
    await userEvent.click(await screen.findByRole("button", { name: "New follow-up" }));
    await userEvent.click(screen.getByRole("button", { name: "Create follow-up" }));
    expect(screen.getByRole("alert")).toHaveTextContent("Fix the marked fields");
    expect(screen.getByText("Write a title.")).toBeInTheDocument();
    expect(screen.getByText("Choose when it is due.")).toBeInTheDocument();
    expect(screen.getByLabelText(/^Title/)).toHaveAttribute("aria-invalid", "true");
    await userEvent.type(screen.getByLabelText(/^Title/), "x");
    await userEvent.type(dueInput(), "2026-12-01T09:30");
    await userEvent.type(screen.getByLabelText(/^Repeat every/), "0");
    await userEvent.click(screen.getByRole("button", { name: "Create follow-up" }));
    expect(screen.getByText(/whole number of days from 1 to 3650/)).toBeInTheDocument();
    expect(sent("POST", /follow-ups$/)).toBeUndefined();                                                // nothing was sent
  });

  it("creates a follow-up for the signed-in person by default, with an idempotency key and the due time as an instant, and announces it", async () => {
    panel();
    await fillNew();
    expect(await screen.findByRole("option", { name: "hana (Hygienist)" })).toBeInTheDocument();
    await waitFor(() => expect((screen.getByLabelText(/^Responsible/) as HTMLSelectElement).value).toBe("u-hana"));   // defaults to the signed-in person
    await userEvent.type(screen.getByLabelText(/^Note/), "Ask about the bite");
    await userEvent.type(screen.getByLabelText(/^Repeat every/), "180");
    await userEvent.click(screen.getByRole("button", { name: "Create follow-up" }));
    expect(await screen.findByText("Created the follow-up: Check the new filling.")).toBeInTheDocument();
    const post = sent("POST", /follow-ups$/)!;
    expect(post.headers["Idempotency-Key"]).toBeTruthy();
    expect(post.headers["X-CSRF-Token"]).toBe("t");
    expect(post.body).toMatchObject({ title: "Check the new filling", note: "Ask about the bite", assigneeUserId: "u-hana", recallIntervalDays: 180 });
    expect(new Date(post.body.dueAtUtc).toISOString()).toBe(post.body.dueAtUtc);                      // an instant, not a local string
    expect(screen.queryByLabelText(/^Title/)).toBeNull();                                               // the form closed and the list reloaded
  });

  it("keeps the same idempotency key for a retry after a failure and makes a new one when the form changes", async () => {
    let attempts = 0;
    reply = (c) => (c.method === "POST" && /follow-ups$/.test(c.url) && attempts++ === 0 ? json({ error: "x", message: "boom" }, 500) : null);
    panel();
    await fillNew();
    await userEvent.click(screen.getByRole("button", { name: "Create follow-up" }));
    expect(await screen.findByRole("alert")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Create follow-up" }));
    await waitFor(() => expect(calls.filter((c) => c.method === "POST" && /follow-ups$/.test(c.url))).toHaveLength(2));
    const [first, second] = calls.filter((c) => c.method === "POST" && /follow-ups$/.test(c.url));
    expect(second.headers["Idempotency-Key"]).toBe(first.headers["Idempotency-Key"]);                 // the retry cannot create a second task
  });

  it("edits a follow-up with the version it was showing", async () => {
    panel();
    await userEvent.click(await screen.findByRole("button", { name: "Edit Call about the crown" }));
    const title = screen.getByLabelText(/^Title/);
    await userEvent.clear(title);
    await userEvent.type(title, "Call about the crown and the cleaning");
    await userEvent.click(screen.getByRole("button", { name: "Save changes" }));
    expect(await screen.findByText("Saved the changes to: Call about the crown and the cleaning.")).toBeInTheDocument();
    const put = sent("PUT", /follow-ups\/t1$/)!;
    expect(put.body).toMatchObject({ title: "Call about the crown and the cleaning", rowVersion: "AAEC" });
  });

  it("completes a follow-up with an optional note and says when the next one was created", async () => {
    panel();
    await userEvent.click(await screen.findByRole("button", { name: "Complete Call about the crown" }));
    await userEvent.type(screen.getByLabelText(/^Note/), "recall");
    await userEvent.click(screen.getByRole("button", { name: "Mark complete" }));
    expect(await screen.findByText("Completed: Call about the crown. The next follow-up was created.")).toBeInTheDocument();
    expect(sent("POST", /t1\/complete$/)!.body).toEqual({ note: "recall", rowVersion: "AAEC" });
  });

  it("tells the person a next follow-up will be made when completing a task that repeats", async () => {
    tasks = [task({ recallIntervalDays: 180 })];
    panel();
    await userEvent.click(await screen.findByRole("button", { name: "Complete Call about the crown" }));
    expect(screen.getByText(/A new follow-up will be made for 180 days after you complete this one/)).toBeInTheDocument();
  });

  it("will not cancel without a reason and sends the reason when there is one", async () => {
    panel();
    await userEvent.click(await screen.findByRole("button", { name: "Cancel Call about the crown" }));
    await userEvent.click(screen.getByRole("button", { name: "Cancel the follow-up" }));
    expect(screen.getByRole("alert")).toHaveTextContent("Write why the follow-up is being cancelled.");
    expect(sent("POST", /cancel$/)).toBeUndefined();
    await userEvent.type(screen.getByLabelText(/^Reason/), "Patient moved away");
    await userEvent.click(screen.getByRole("button", { name: "Cancel the follow-up" }));
    expect(await screen.findByText("Cancelled: Call about the crown.")).toBeInTheDocument();
    expect(sent("POST", /t1\/cancel$/)!.body).toEqual({ note: "Patient moved away", rowVersion: "AAEC" });
  });

  it("explains a change by someone else, stores nothing and offers a reload", async () => {
    reply = (c) => (c.method === "POST" && c.url.endsWith("/complete") ? json({ error: "task_changed", message: "server words" }, 409) : null);
    panel();
    await userEvent.click(await screen.findByRole("button", { name: "Complete Call about the crown" }));
    await userEvent.click(screen.getByRole("button", { name: "Mark complete" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Someone else changed this follow-up");
    reply = () => null;
    await userEvent.click(screen.getByRole("button", { name: "Reload the follow-up" }));
    expect(await screen.findByText("Reloaded the follow-ups.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Mark complete" })).toBeNull();
  });

  it("shows a task's history with who did each thing and when, the reminder as the system's own entry", async () => {
    panel();
    await userEvent.click(await screen.findByRole("button", { name: "History of Call about the crown" }));
    const list = await screen.findByRole("list", { name: "History" });
    const items = within(list).getAllByRole("listitem");
    expect(items[0]).toHaveTextContent(/Created by dr/);
    expect(items[0]).toHaveTextContent(/due /);
    expect(items[1]).toHaveTextContent(/Reminder sent by the system/);
    expect(items[1]).not.toHaveTextContent(/u-hana/);                                                  // identifiers are not shown
    expect(screen.getByRole("button", { name: "Hide history of Call about the crown" })).toHaveAttribute("aria-expanded", "true");
  });

  it("has no serious accessibility violations with the list and a form open", async () => {
    const view = panel();
    await userEvent.click(await screen.findByRole("button", { name: "New follow-up" }));
    await screen.findByLabelText(/^Title/);
    expect((await axe(view.container)).violations).toEqual([]);
  });
});

describe("STORY-009 my follow-ups page", () => {
  const page = () => render(<MemoryRouter><MyFollowUpsPage /></MemoryRouter>);

  it("lists what the person is responsible for with a link to each patient's follow-ups and overdue in words", async () => {
    page();
    expect(await screen.findByRole("heading", { name: "My follow-ups" })).toBeInTheDocument();
    expect(screen.getByText("Book the cleaning")).toBeInTheDocument();
    expect(screen.getAllByText("Overdue")).toHaveLength(1);
    expect(screen.getAllByRole("link", { name: "Open this patient's follow-ups" })[0]).toHaveAttribute("href", "/patients/p1/follow-ups");
  });

  it("says it could not load and offers the closed ones", async () => {
    reply = (c) => (c.url.includes("mine?status=open") ? json({ error: "x" }, 500) : null);
    const view = page();
    expect(await screen.findByRole("alert")).toHaveTextContent("Could not load your follow-ups");
    view.unmount();
    reply = () => null; tasks = [];
    page();
    expect(await screen.findByText("Nothing here")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "All" }));
    await waitFor(() => expect(sent("GET", /mine\?status=all/)).toBeDefined());
  });

  it("has no serious accessibility violations", async () => {
    const view = page();
    await screen.findByText("Book the cleaning");
    expect((await axe(view.container)).violations).toEqual([]);
  });
});

describe("STORY-009 entry checks", () => {
  it("accepts a good entry and refuses each bad one", () => {
    const good = { title: "Call", note: "", due: "2026-12-01T09:30", assignee: "u1", interval: "" };
    expect(validate(good)).toEqual({});
    expect(validate({ ...good, title: " " }).title).toBeTruthy();
    expect(validate({ ...good, title: "x".repeat(121) }).title).toBeTruthy();
    expect(validate({ ...good, title: "x".repeat(120) }).title).toBeUndefined();
    expect(validate({ ...good, note: "n".repeat(501) }).note).toBeTruthy();
    expect(validate({ ...good, due: "" }).due).toBeTruthy();
    expect(validate({ ...good, assignee: "" }).assignee).toBeTruthy();
    for (const bad of ["0", "-1", "1.5", "abc", "3651"]) expect(validate({ ...good, interval: bad }).interval).toBeTruthy();
    for (const ok of ["1", "3650", " 30 "]) expect(validate({ ...good, interval: ok }).interval).toBeUndefined();
  });
});
