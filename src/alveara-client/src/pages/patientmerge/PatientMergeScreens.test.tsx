import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { axe } from "jest-axe";
import { PatientMergePage } from "./PatientMergePage";

// STORY-017 screens against a small in-memory server that answers like the real API (stable error codes; request bodies and headers recorded).

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
const err = (status: number, error: string, message = "server words") => json({ error, message }, status);

const brief = (over: Record<string, unknown> = {}) => ({
  id: "a", firstName: "Ann", middleName: null, lastName: "Lee", dateOfBirth: "1985-03-09", phone: "(555) 010-0001", email: null, city: "Austin", state: "TX", isActive: true,
  createdAtUtc: "2026-01-01T10:00:00Z", householdId: null, ...over,
});
const ann = brief(); const anne = brief({ id: "b", firstName: "Anne", phone: "(555) 010-0002", email: "anne@example.org", createdAtUtc: "2026-02-01T10:00:00Z" });
const pair = { first: ann, second: anne, score: 40, reasons: ["Same last name and date of birth"], suggestedSurvivorId: "a", suggestionReason: "The older record", sameHousehold: false };
const preview = (over: Record<string, unknown> = {}) => ({
  source: anne, survivor: ann, conflicts: [{ field: "Phone", survivorValue: "(555) 010-0001", sourceValue: "(555) 010-0002" }], autoFills: [{ field: "Email", sourceValue: "anne@example.org" }],
  moves: [{ table: "Appointments", column: "PatientId", rows: 3 }, { table: "Diagnoses", column: "PatientId", rows: 2 }, { table: "Notes", column: "PatientId", rows: 0 }], leftBehind: [], blockers: [], canMerge: true,
  sourceRowVersion: "VS", survivorRowVersion: "VV", ...over,
});
const mergeResult = () => ({
  mergeId: "m1", sourcePatientId: "b", survivorPatientId: "a", mergedByUserId: "u1", mergedAtUtc: "2026-10-25T10:00:00Z", reason: "Same person", replayed: false,
  moves: [{ table: "Appointments", column: "PatientId", rows: 3 }, { table: "Diagnoses", column: "PatientId", rows: 2 }], resolutions: [{ kind: "Field", subject: "Phone", choice: "TookSource", replacedValue: "(555) 010-0001" }],
});

type Call = { method: string; url: string; body: any; headers: Record<string, string> };
let calls: Call[];
let reply: (c: Call) => Response | null;
let pairs: unknown[];
let log: unknown[];
let current: ReturnType<typeof preview>;

beforeEach(() => {
  calls = []; reply = () => null; pairs = [pair]; log = []; current = preview();
  vi.stubGlobal("fetch", vi.fn().mockImplementation((url: string, init?: RequestInit) => {
    const u = String(url); const method = init?.method ?? "GET";
    const body = init?.body ? JSON.parse(String(init.body)) : undefined;
    if (u.includes("csrf-token")) return Promise.resolve(json({ token: "t" }));
    const call = { method, url: u, body, headers: (init?.headers ?? {}) as Record<string, string> };
    calls.push(call);
    const custom = reply(call);
    if (custom) return Promise.resolve(custom);
    if (u.includes("/api/patient-merges/duplicates")) return Promise.resolve(json({ pairs, truncated: false, patientsConsidered: 2 }));
    if (u.includes("/api/patient-merges/log")) return Promise.resolve(json(log));
    if (u.includes("/api/patient-merges/preview")) return Promise.resolve(json(current));
    if (u.endsWith("/api/patient-merges") && method === "POST") return Promise.resolve(json(mergeResult()));
    return Promise.resolve(err(404, "not_found"));
  }));
});
afterEach(() => vi.unstubAllGlobals());

const renderPage = () => render(<MemoryRouter><PatientMergePage /></MemoryRouter>);
const posts = () => calls.filter((c) => c.method === "POST");

async function openReview() {
  renderPage();
  await userEvent.click(await screen.findByRole("button", { name: /Review Ann Lee and Anne Lee/ }));
  await screen.findByRole("heading", { name: /Merge Anne Lee into Ann Lee/ });
}

describe("the duplicate list", () => {
  it("lists a pair with its reasons, the match and a review action, and has no accessibility violations", async () => {
    const { container } = renderPage();
    expect(await screen.findByText("Same last name and date of birth")).toBeInTheDocument();
    expect(screen.getByText("40%")).toBeInTheDocument();
    expect(await axe(container)).toHaveNoViolations();
  });

  it("says so when there is nothing to review and shows the merge log with who and when", async () => {
    pairs = [];
    log = [{ mergeId: "m0", source: anne, survivor: ann, mergedByUserId: "u1", mergedByName: "admin-hana", mergedAtUtc: "2026-10-24T10:00:00Z", reason: "Front desk registered twice", rowsMoved: 5, recordsLeftBehind: 1 }];
    const { container } = renderPage();
    expect(await screen.findByText(/No possible duplicates found/)).toBeInTheDocument();
    const row = within(await screen.findByRole("table", { name: "Merges made" })).getAllByRole("row")[1];
    expect(within(row).getByText("Front desk registered twice")).toBeInTheDocument();
    expect(within(row).getByText("admin-hana")).toBeInTheDocument();
    expect(await axe(container)).toHaveNoViolations();
  });

  it("explains a failed load instead of showing an empty list", async () => {
    reply = (c) => (c.url.includes("/duplicates") ? err(500, "boom") : null);
    renderPage();
    expect(await screen.findByText("Could not load the duplicate review")).toBeInTheDocument();
  });
});

describe("the review and the merge", () => {
  it("shows the records side by side and keeps the merge button off until every field is answered, the reason is written and the merge is confirmed", async () => {
    await openReview();
    const merge = screen.getByRole("button", { name: "Merge the records" });
    expect(merge).toBeDisabled();
    expect(screen.getByText(/5 rows move/)).toBeInTheDocument();
    expect(screen.getByText(/Filled in from the retired record.*Email/)).toBeInTheDocument();
    await userEvent.click(screen.getByLabelText(/Take the retired record's: \(555\) 010-0002/));
    expect(merge).toBeDisabled();
    await userEvent.type(screen.getByLabelText(/Reason for the merge/), "Same person");
    expect(merge).toBeDisabled();
    await userEvent.click(screen.getByLabelText(/I have compared the two records/));
    expect(merge).toBeEnabled();
  });

  it("sends the answers, the reason, the versions it showed and an Idempotency-Key, then says what happened and links to the record that carries on", async () => {
    await openReview();
    await userEvent.click(screen.getByLabelText(/Take the retired record's/));
    await userEvent.type(screen.getByLabelText(/Reason for the merge/), "Same person");
    await userEvent.click(screen.getByLabelText(/I have compared the two records/));
    await userEvent.click(screen.getByRole("button", { name: "Merge the records" }));
    const status = await screen.findByRole("status");
    expect(status).toHaveTextContent("Merged Anne Lee into Ann Lee. 5 rows moved");
    expect(within(status).getByRole("link", { name: "Open the record that carries on" })).toHaveAttribute("href", "/patients/a");
    const post = posts().find((c) => c.url.endsWith("/api/patient-merges"))!;
    expect(post.body).toEqual({ sourcePatientId: "b", survivorPatientId: "a", reason: "Same person", sourceRowVersion: "VS", survivorRowVersion: "VV", fieldChoices: { Phone: "Source" } });
    expect(post.headers["Idempotency-Key"]).toMatch(/^[0-9a-f-]{8,}$/i);
  });

  it.each([
    ["an unanswered field", { answer: false, reason: true, confirm: true }],
    ["no reason", { answer: true, reason: false, confirm: true }],
    ["no confirmation", { answer: true, reason: true, confirm: false }],
  ])("keeps the merge button off for %s even when everything else is done", async (_name, done) => {
    await openReview();
    if (done.answer) await userEvent.click(screen.getByLabelText(/Take the retired record's/));
    if (done.reason) await userEvent.type(screen.getByLabelText(/Reason for the merge/), "Same person");
    if (done.confirm) await userEvent.click(screen.getByLabelText(/I have compared the two records/));
    expect(screen.getByRole("button", { name: "Merge the records" })).toBeDisabled();
    expect(posts()).toHaveLength(0);
  });

  it("can swap which record is kept", async () => {
    await openReview();
    current = preview({ source: ann, survivor: anne });
    await userEvent.click(screen.getByRole("button", { name: "Keep the other record instead" }));
    await screen.findByRole("heading", { name: /Merge Ann Lee into Anne Lee/ });
    expect(calls.some((c) => c.url.includes("sourceId=a") && c.url.includes("survivorId=b"))).toBe(true);
  });

  it("shows what stops a merge and offers no way to go ahead", async () => {
    current = preview({ canMerge: false, blockers: [{ code: "shared_record", message: "A SafetyAlerts record that must stay with the retired patient is linked to a ClinicalRecordItems record that would move. Resolve it before merging." }] });
    await openReview();
    expect(screen.getByRole("alert")).toHaveTextContent("This merge cannot go ahead yet.");
    await userEvent.click(screen.getByLabelText(/Take the retired record's/));
    await userEvent.type(screen.getByLabelText(/Reason for the merge/), "Same person");
    await userEvent.click(screen.getByLabelText(/I have compared the two records/));
    expect(screen.getByRole("button", { name: "Merge the records" })).toBeDisabled();
    expect(posts()).toHaveLength(0);
  });

  it("explains a record that changed meanwhile, merges nothing, and reloads the review on request", async () => {
    reply = (c) => (c.method === "POST" && c.url.endsWith("/api/patient-merges") ? err(409, "patient_changed") : null);
    await openReview();
    await userEvent.click(screen.getByLabelText(/Keep the kept record's/));
    await userEvent.type(screen.getByLabelText(/Reason for the merge/), "Same person");
    await userEvent.click(screen.getByLabelText(/I have compared the two records/));
    await userEvent.click(screen.getByRole("button", { name: "Merge the records" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("changed after you reviewed it");
    const before = calls.filter((c) => c.url.includes("/preview")).length;
    await userEvent.click(screen.getByRole("button", { name: "Reload the review" }));
    await waitFor(() => expect(calls.filter((c) => c.url.includes("/preview")).length).toBe(before + 1));
    expect(screen.queryByText(/changed after you reviewed it/)).not.toBeInTheDocument();
  });

  it("goes back to the list without changing anything", async () => {
    await openReview();
    await userEvent.click(screen.getByRole("button", { name: "Back to the list" }));
    expect(await screen.findByRole("heading", { name: "Possible duplicates" })).toBeInTheDocument();
    expect(posts()).toHaveLength(0);
  });
});
