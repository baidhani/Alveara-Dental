import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { axe } from "jest-axe";
import { PatientFollowUpTimeline } from "./PatientFollowUpTimeline";
import { PatientRecallCard } from "./PatientRecallCard";
import { RecallSettingsPage } from "./RecallSettingsPage";
import { QUEUE_REFRESH_MS, WorkQueuePage } from "./WorkQueuePage";

// ALV-009-C01 screens against a small in-memory server that answers like the real API (stable error codes, versions and request bodies recorded).

let perms = new Set<string>();
vi.mock("../../contexts/AuthContext", () => ({
  useAuth: () => ({ state: { kind: "signed-in", username: "hana", role: "Hygienist", permissions: [...perms], sessionExpiresAtUtc: null }, hasPermission: (p: string) => perms.has(p) }),
}));

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
const task = (over: Record<string, unknown> = {}) => ({
  id: "t1", patientId: "p1", title: "Patient recall: schedule the next visit", note: null, dueAtUtc: "2026-11-03T15:30:00Z", status: "Open", overdue: false, assigneeUserId: null, assigneeName: null,
  recallIntervalDays: null, recallOfTaskId: null, recallTaskId: null, createdByUserId: null, createdAtUtc: "2026-10-20T10:00:00Z", closedAtUtc: null, closedByUserId: null, closureNote: null, rowVersion: "AAEC",
  priority: "Normal", kind: "Recall", appointmentId: null, sourcePlanItemId: null, closedBySystem: false, ...over,
});
const item = (t: ReturnType<typeof task>, urgency = "Later", patientName = "Ann Lee") => ({ task: t, patientName, appointmentStartUtc: null, urgency });

type Call = { method: string; url: string; body: any };
let calls: Call[];
let reply: (c: Call) => Response | null;
let items: ReturnType<typeof item>[];
let settings: Record<string, unknown>;
let recall: Record<string, unknown>;
let timeline: unknown[];

beforeEach(() => {
  perms = new Set(["ViewFollowUps", "ManageFollowUps", "ManageRecallSettings"]);
  calls = []; reply = () => null;
  items = [
    item(task({ id: "t1", title: "Urgent overdue recall", priority: "Urgent", overdue: true, dueAtUtc: "2026-10-01T09:00:00Z" }), "Overdue"),
    item(task({ id: "t2", title: "Accepted treatment is not scheduled", kind: "UnscheduledTreatment", assigneeUserId: "u-dr", assigneeName: "dr", rowVersion: "AAED" }), "DueSoon", "Bo Kim"),
  ];
  settings = { enabled: false, defaultIntervalDays: 180, leadDays: 30, maxVisitAgeMonths: 24, dueSoonDays: 7, updatedAtUtc: "2026-10-24T00:00:00Z", updatedByUserId: null, rowVersion: "S1" };
  recall = { patientId: "p1", hasPlan: false, ownIntervalDays: null, optedOut: false, reason: null, effectiveIntervalDays: 180, lastCompletedVisitUtc: "2026-05-01T15:00:00Z", nextDueUtc: "2026-10-28T14:00:00Z", verdict: "NotDue", rowVersion: null };
  timeline = [];
  vi.stubGlobal("fetch", vi.fn().mockImplementation((url: string, init?: RequestInit) => {
    const u = String(url); const method = init?.method ?? "GET";
    const body = init?.body ? JSON.parse(String(init.body)) : undefined;
    if (u.includes("csrf-token")) return Promise.resolve(json({ token: "t" }));
    const call = { method, url: u, body }; calls.push(call);
    const custom = reply(call);
    if (custom) return Promise.resolve(custom);
    if (method === "GET" && u.startsWith("/api/work-queue")) return Promise.resolve(json({ items, total: items.length, page: 1, pageSize: 25, counts: { overdue: 1, dueSoon: 1, unassigned: 1, mine: 0, all: 2 }, dueSoonDays: 7 }));
    if (method === "GET" && u === "/api/follow-ups/assignees") return Promise.resolve(json([{ id: "u-dr", name: "dr", role: "Dentist" }, { id: "u-hana", name: "hana", role: "Hygienist" }]));
    if (method === "POST" && /\/claim$/.test(u)) return Promise.resolve(json({ task: task({ assigneeUserId: "u-hana", assigneeName: "hana", rowVersion: "AAEF" }), replayed: false, recallTaskId: null }));
    if (method === "POST" && /\/assign$/.test(u)) return Promise.resolve(json({ task: task({ assigneeUserId: body.assigneeUserId, rowVersion: "AAEG" }), replayed: false, recallTaskId: null }));
    if (method === "POST" && /\/priority$/.test(u)) return Promise.resolve(json({ task: task({ priority: body.priority, rowVersion: "AAEH" }), replayed: false, recallTaskId: null }));
    if (method === "POST" && /\/complete$/.test(u)) return Promise.resolve(json({ task: task({ status: "Completed" }), replayed: false, recallTaskId: null }));
    if (method === "POST" && /\/cancel$/.test(u)) return Promise.resolve(json({ task: task({ status: "Cancelled" }), replayed: false, recallTaskId: null }));
    if (method === "GET" && /\/follow-ups\/timeline$/.test(u)) return Promise.resolve(json(timeline));
    if (method === "GET" && /\/recall$/.test(u)) return Promise.resolve(json(recall));
    if (method === "PUT" && /\/recall$/.test(u)) { recall = { ...recall, hasPlan: true, ownIntervalDays: body.intervalDays, optedOut: body.optedOut, reason: body.reason, effectiveIntervalDays: body.intervalDays ?? 180, rowVersion: "P1" }; return Promise.resolve(json(recall)); }
    if (method === "POST" && /\/recall\/reset$/.test(u)) { recall = { ...recall, hasPlan: false, ownIntervalDays: null, optedOut: false, reason: null, effectiveIntervalDays: 180, rowVersion: null }; return Promise.resolve(json(recall)); }
    if (method === "GET" && u === "/api/recall/settings") return Promise.resolve(json(settings));
    if (method === "PUT" && u === "/api/recall/settings") { settings = { ...settings, ...body, rowVersion: "S2" }; return Promise.resolve(json(settings)); }
    return Promise.resolve(json({ error: "unexpected" }, 500));
  }));
});

afterEach(() => { vi.useRealTimers(); vi.unstubAllGlobals(); });

const queue = () => render(<MemoryRouter><WorkQueuePage /></MemoryRouter>);
const sent = (method: string, matcher: RegExp) => calls.filter((c) => c.method === method && matcher.test(c.url));

describe("ALV-009-C01 work queue page", () => {
  it("shows the open work with priority and urgency in words, the counts on the filter chips and a link to each patient's follow-ups", async () => {
    queue();
    expect(await screen.findByRole("heading", { name: "Work queue" })).toBeInTheDocument();
    expect(screen.getByText("Urgent overdue recall")).toBeInTheDocument();
    expect(screen.getByText("Overdue")).toBeInTheDocument();                                               // in words
    expect(screen.getByText("Due soon")).toBeInTheDocument();
    expect(screen.getByText("Urgent", { selector: "span" })).toBeInTheDocument();
    expect(screen.getByText("Unassigned", { selector: "td" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Overdue (1)" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "All open (2)" })).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByRole("link", { name: "Bo Kim" })).toHaveAttribute("href", "/patients/p1/follow-ups");
    expect(screen.getByRole("table", { name: /Work queue, 2 tasks/ })).toBeInTheDocument();
  });

  it("asks for the filter, the narrowing and the page that were chosen", async () => {
    queue();
    await screen.findByText("Urgent overdue recall");
    await userEvent.click(screen.getByRole("button", { name: "Unassigned (1)" }));
    await waitFor(() => expect(sent("GET", /filter=unassigned/).length).toBeGreaterThan(0));
    expect(screen.getByRole("button", { name: "Unassigned (1)" })).toHaveAttribute("aria-pressed", "true");
    await userEvent.selectOptions(screen.getByLabelText("Priority"), "High");
    await userEvent.selectOptions(screen.getByLabelText("Kind"), "Recall");
    await waitFor(() => expect(sent("GET", /priority=High/).some((c) => /kind=Recall/.test(c.url))).toBe(true));
    await userEvent.selectOptions(await screen.findByLabelText("Owner"), "none");
    await waitFor(() => expect(sent("GET", /owner=none/).length).toBeGreaterThan(0));
  });

  it("says it could not load rather than showing an empty queue, and keeps what it has when only a refresh fails", async () => {
    reply = (c) => (c.method === "GET" && c.url.startsWith("/api/work-queue") ? json({ error: "x" }, 500) : null);
    const view = queue();
    expect(await screen.findByRole("alert")).toHaveTextContent("Could not load the work queue");
    view.unmount();
    reply = () => null; items = [];
    queue();
    expect(await screen.findByText("Nothing in this view")).toBeInTheDocument();
  });

  it("shows a role that can only view the queue no action buttons", async () => {
    perms = new Set(["ViewFollowUps"]);
    queue();
    expect(await screen.findByText("Urgent overdue recall")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^Take / })).toBeNull();
    expect(screen.queryByRole("columnheader", { name: "Actions" })).toBeNull();
    expect(screen.getByText(/your role cannot change tasks/)).toBeInTheDocument();
    expect(sent("GET", /assignees/)).toHaveLength(0);
  });

  it("lets a person take an unassigned task with the version it was showing and says so", async () => {
    queue();
    await userEvent.click(await screen.findByRole("button", { name: "Take Urgent overdue recall" }));
    expect(await screen.findByText("You took: Urgent overdue recall.")).toBeInTheDocument();
    expect(sent("POST", /t1\/claim$/)[0].body).toEqual({ rowVersion: "AAEC" });
    expect(screen.queryByRole("button", { name: "Take Accepted treatment is not scheduled" })).toBeNull();      // already owned
  });

  it("explains a task someone else changed, reloads the queue and stores nothing", async () => {
    reply = (c) => (c.method === "POST" && c.url.endsWith("/claim") ? json({ error: "task_changed", message: "server words" }, 409) : null);
    queue();
    await userEvent.click(await screen.findByRole("button", { name: "Take Urgent overdue recall" }));
    expect(await screen.findByText(/Someone else changed this follow-up.*The queue was reloaded/)).toBeInTheDocument();
    await waitFor(() => expect(sent("GET", /\/api\/work-queue/).length).toBeGreaterThan(1));
  });

  it("changes owner then priority in one save, each with the version the last one returned, and does nothing when nothing changed", async () => {
    queue();
    await userEvent.click(await screen.findByRole("button", { name: "Change owner or priority of Urgent overdue recall" }));
    const save = screen.getByRole("button", { name: "Save" });
    expect(save).toBeDisabled();
    await userEvent.selectOptions(screen.getByLabelText("Owner", { selector: "select[id^='qre-owner']" }), "u-dr");
    await userEvent.selectOptions(screen.getByLabelText("Priority", { selector: "select[id^='qre-priority']" }), "Low");
    await userEvent.click(save);
    expect(await screen.findByText("Saved: Urgent overdue recall.")).toBeInTheDocument();
    expect(sent("POST", /t1\/assign$/)[0].body).toEqual({ assigneeUserId: "u-dr", rowVersion: "AAEC" });
    expect(sent("POST", /t1\/priority$/)[0].body).toEqual({ priority: "Low", rowVersion: "AAEG" });          // the version the assignment returned
  });

  it("explains a stale owner change and offers a reload", async () => {
    reply = (c) => (c.method === "POST" && c.url.endsWith("/assign") ? json({ error: "task_changed", message: "x" }, 409) : null);
    queue();
    await userEvent.click(await screen.findByRole("button", { name: "Change owner or priority of Urgent overdue recall" }));
    await userEvent.selectOptions(screen.getByLabelText("Owner", { selector: "select[id^='qre-owner']" }), "u-dr");
    await userEvent.click(screen.getByRole("button", { name: "Save" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Someone else changed this follow-up");
    reply = () => null;
    await userEvent.click(screen.getByRole("button", { name: "Reload the queue" }));
    expect(await screen.findByText("Reloaded the queue.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Save" })).toBeNull();
  });

  it("completes and cancels a task from the queue, a cancellation needing its reason", async () => {
    queue();
    await userEvent.click(await screen.findByRole("button", { name: "Complete Urgent overdue recall" }));
    await userEvent.type(screen.getByLabelText(/^Note/), "Booked for March");
    await userEvent.click(screen.getByRole("button", { name: "Mark complete" }));
    expect(await screen.findByText("Completed: Patient recall: schedule the next visit.")).toBeInTheDocument();
    expect(sent("POST", /t1\/complete$/)[0].body).toEqual({ note: "Booked for March", rowVersion: "AAEC" });

    await userEvent.click(await screen.findByRole("button", { name: "Cancel Accepted treatment is not scheduled" }));
    await userEvent.click(screen.getByRole("button", { name: "Cancel the follow-up" }));
    expect(screen.getByRole("alert")).toHaveTextContent("Write why the follow-up is being cancelled.");
    await userEvent.type(screen.getByLabelText(/^Reason/), "Handled by phone");
    await userEvent.click(screen.getByRole("button", { name: "Cancel the follow-up" }));
    expect(await screen.findByText("Cancelled: Accepted treatment is not scheduled.")).toBeInTheDocument();
  });

  it("refreshes itself every minute unless a form is open", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    queue();
    await screen.findByText("Urgent overdue recall");
    const before = sent("GET", /\/api\/work-queue/).length;
    await act(async () => { vi.advanceTimersByTime(QUEUE_REFRESH_MS + 100); });
    await waitFor(() => expect(sent("GET", /\/api\/work-queue/).length).toBeGreaterThan(before));
    await userEvent.click(screen.getByRole("button", { name: "Complete Urgent overdue recall" }));
    const open = sent("GET", /\/api\/work-queue/).length;
    await act(async () => { vi.advanceTimersByTime(QUEUE_REFRESH_MS * 2); });
    expect(sent("GET", /\/api\/work-queue/).length).toBe(open);                                             // the form being filled is not pulled out from under the person
  });

  it("has no serious accessibility violations, with and without a form open", async () => {
    const view = queue();
    await screen.findByText("Urgent overdue recall");
    expect((await axe(view.container)).violations).toEqual([]);
    await userEvent.click(screen.getByRole("button", { name: "Change owner or priority of Urgent overdue recall" }));
    await screen.findByLabelText("Owner", { selector: "select[id^='qre-owner']" });
    expect((await axe(view.container)).violations).toEqual([]);
  });
});

describe("ALV-009-C01 patient timeline", () => {
  it("lists what happened, newest first, in words with who did it and says the system's entries are the system's", async () => {
    timeline = [
      { taskId: "t1", taskTitle: "Patient recall: schedule the next visit", taskKind: "Recall", taskStatus: "Open", eventKind: "Assigned", actorUserId: "u1", actorName: "hana", occurredAtUtc: "2026-10-25T10:00:00Z", detail: null },
      { taskId: "t1", taskTitle: "Patient recall: schedule the next visit", taskKind: "Recall", taskStatus: "Open", eventKind: "SystemCreated", actorUserId: null, actorName: null, occurredAtUtc: "2026-10-24T10:00:00Z", detail: "Due 2026-11-03T15:30:00.0000000+00:00" },
    ];
    render(<PatientFollowUpTimeline patientId="p1" version={0} />);
    const list = await screen.findByRole("list", { name: "Follow-up timeline" });
    const rows = within(list).getAllByRole("listitem");
    expect(rows[0]).toHaveTextContent("Assigned: Patient recall: schedule the next visit by hana");
    expect(rows[1]).toHaveTextContent("Made by the system: Patient recall: schedule the next visit by the system");
    expect(rows[1]).toHaveTextContent(/\(due /);
  });

  it("says when nothing has happened and when it could not load", async () => {
    const view = render(<PatientFollowUpTimeline patientId="p1" version={0} />);
    expect(await screen.findByText(/Nothing has happened/)).toBeInTheDocument();
    view.unmount();
    reply = (c) => (c.url.endsWith("/timeline") ? json({ error: "x" }, 500) : null);
    render(<PatientFollowUpTimeline patientId="p1" version={0} />);
    expect(await screen.findByRole("alert")).toHaveTextContent("Could not load the timeline");
  });
});

describe("ALV-009-C01 patient recall card", () => {
  const card = (canManage = true, onChanged = vi.fn()) => render(<PatientRecallCard patientId="p1" canManage={canManage} onChanged={onChanged} />);

  it.each([
    ["Due", "Due for a recall"], ["NotDue", "Next recall due"], ["OptedOut", "opted out"], ["NoVisit", "No completed visit yet"],
    ["LapsedPatient", "Not seen for a long time"], ["OpenRecall", "A recall follow-up is already open"], ["AlreadyBooked", "Already has a future appointment"],
  ])("says in words where a %s patient stands", async (verdict, words) => {
    recall = { ...recall, verdict };
    card(false);
    expect(await screen.findByText(new RegExp(words))).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Arrange/ })).toBeNull();                                  // a role that cannot manage sees no controls
  });

  it("checks the arrangement before sending, saves a new one without a version, and announces it", async () => {
    const changed = vi.fn();
    card(true, changed);
    await userEvent.click(await screen.findByRole("button", { name: /Arrange this patient/ }));
    await userEvent.click(screen.getByRole("button", { name: "Save" }));
    expect(screen.getByRole("alert")).toHaveTextContent("Choose an interval, or opt the patient out");
    await userEvent.type(screen.getByLabelText(/^Own interval/), "0");
    await userEvent.click(screen.getByRole("button", { name: "Save" }));
    expect(screen.getByRole("alert")).toHaveTextContent("whole number of days from 1 to 3650");
    expect(sent("PUT", /recall$/)).toHaveLength(0);
    await userEvent.clear(screen.getByLabelText(/^Own interval/));
    await userEvent.type(screen.getByLabelText(/^Own interval/), "90");
    await userEvent.type(screen.getByLabelText(/^Reason/), "Gum disease");
    await userEvent.click(screen.getByRole("button", { name: "Save" }));
    expect(await screen.findByText("Saved this patient's recall arrangement.")).toBeInTheDocument();
    expect(sent("PUT", /recall$/)[0].body).toEqual({ intervalDays: 90, optedOut: false, reason: "Gum disease", rowVersion: null });
    expect(await screen.findByText(/this patient's own interval/)).toBeInTheDocument();
    expect(changed).toHaveBeenCalled();
  });

  it("opts a patient out and resets them to the practice default with the version it was showing", async () => {
    recall = { ...recall, hasPlan: true, ownIntervalDays: 60, rowVersion: "P0", effectiveIntervalDays: 60 };
    card();
    await userEvent.click(await screen.findByRole("button", { name: /Arrange this patient/ }));
    await userEvent.click(screen.getByRole("checkbox", { name: /Do not recall/ }));
    await userEvent.click(screen.getByRole("button", { name: "Save" }));
    await screen.findByText("Saved this patient's recall arrangement.");
    expect(sent("PUT", /recall$/)[0].body).toMatchObject({ intervalDays: 60, optedOut: true, rowVersion: "P0" });
    await userEvent.click(screen.getByRole("button", { name: /Arrange this patient/ }));
    await userEvent.click(screen.getByRole("button", { name: "Reset to the practice default" }));
    expect(await screen.findByText("Back to the practice default.")).toBeInTheDocument();
    expect(sent("POST", /recall\/reset$/)[0].body).toEqual({ rowVersion: "P1" });
  });

  it("explains a change by someone else and offers a reload", async () => {
    reply = (c) => (c.method === "PUT" && /recall$/.test(c.url) ? json({ error: "plan_changed", message: "x" }, 409) : null);
    card();
    await userEvent.click(await screen.findByRole("button", { name: /Arrange this patient/ }));
    await userEvent.type(screen.getByLabelText(/^Own interval/), "45");
    await userEvent.click(screen.getByRole("button", { name: "Save" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Someone else changed this patient's recall arrangement");
    reply = () => null;
    await userEvent.click(screen.getByRole("button", { name: "Reload this patient's recall" }));
    expect(screen.queryByRole("button", { name: "Save" })).toBeNull();
  });
});

describe("ALV-009-C01 recall settings page", () => {
  const page = () => render(<RecallSettingsPage />);

  it("says whether automatic recall is on, shows every number with its limits and has no serious accessibility violations", async () => {
    const view = page();
    expect(await screen.findByText("Automatic recall is off.")).toBeInTheDocument();
    expect((screen.getByLabelText(/Default recall interval/) as HTMLInputElement).value).toBe("180");
    expect(screen.getByText(/1 to 3650/)).toBeInTheDocument();
    expect((await axe(view.container)).violations).toEqual([]);
  });

  it("checks every number at both of its limits and sends nothing while one is wrong", async () => {
    page();
    const interval = await screen.findByLabelText(/Default recall interval/);
    for (const bad of ["0", "3651", "abc", ""]) {
      await userEvent.clear(interval);
      if (bad) await userEvent.type(interval, bad);
      await userEvent.click(screen.getByRole("button", { name: "Save the recall settings" }));
      expect(screen.getByRole("alert")).toHaveTextContent("Fix the marked fields");
      expect(screen.getByText("Enter a whole number from 1 to 3650.")).toBeInTheDocument();
    }
    expect(sent("PUT", /recall\/settings$/)).toHaveLength(0);
    await userEvent.clear(interval); await userEvent.type(interval, "3650");
    await userEvent.clear(screen.getByLabelText(/this many days before the due date/)); await userEvent.type(screen.getByLabelText(/this many days before the due date/), "0");
    await userEvent.click(screen.getByRole("button", { name: "Save the recall settings" }));
    expect(await screen.findByText("Saved the recall settings.")).toBeInTheDocument();
  });

  it("turns recall on with the version it was showing and says it is on", async () => {
    page();
    await userEvent.click(await screen.findByRole("checkbox", { name: /Make recall follow-ups automatically/ }));
    await userEvent.click(screen.getByRole("button", { name: "Save the recall settings" }));
    expect(await screen.findByText("Saved the recall settings.")).toBeInTheDocument();
    expect(sent("PUT", /recall\/settings$/)[0].body).toEqual({ enabled: true, defaultIntervalDays: 180, leadDays: 30, maxVisitAgeMonths: 24, dueSoonDays: 7, rowVersion: "S1" });
  });

  it("explains a change by someone else and reloads the current settings", async () => {
    reply = (c) => (c.method === "PUT" ? json({ error: "settings_changed", message: "x" }, 409) : null);
    page();
    await userEvent.click(await screen.findByRole("button", { name: "Save the recall settings" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Someone else changed the recall settings");
    settings = { ...settings, defaultIntervalDays: 120, rowVersion: "S9" };
    reply = () => null;
    await userEvent.click(screen.getByRole("button", { name: "Reload the recall settings" }));
    await waitFor(() => expect((screen.getByLabelText(/Default recall interval/) as HTMLInputElement).value).toBe("120"));
  });

  it("says it could not load rather than showing blank numbers", async () => {
    reply = (c) => (c.url === "/api/recall/settings" ? json({ error: "x" }, 500) : null);
    page();
    expect(await screen.findByRole("alert")).toHaveTextContent("Could not load the recall settings");
  });
});
