import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { axe } from "jest-axe";
import { WorkQueuePage } from "../followups/WorkQueuePage";
import { ExternalCaseDetailPage } from "./ExternalCaseDetailPage";
import { ExternalCasesPage } from "./ExternalCasesPage";
import { ExternalPartiesPage } from "./ExternalPartiesPage";
import { PatientExternalCasesPanel } from "./PatientExternalCasesPanel";

// ALV-009-C02 screens against a small in-memory server that answers like the real API (stable error codes, versions and request bodies recorded).

let perms = new Set<string>();
vi.mock("../../contexts/AuthContext", () => ({
  useAuth: () => ({ state: { kind: "signed-in", username: "hana", role: "Dentist", permissions: [...perms], sessionExpiresAtUtc: null }, hasPermission: (p: string) => perms.has(p) }),
}));

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
const err = (status: number, error: string, message = "server words") => json({ error, message }, status);

const referral = (over: Record<string, unknown> = {}) => ({
  kind: "Referral", id: "r1", patientId: "p1", partyId: "pa1", partyName: "Dr. Rivera Endodontics", partyActive: true, summary: "Retreatment of tooth 19", status: "Sent", nextStatuses: ["Received", "Cancelled"],
  sentOn: "2026-10-10", receivedOn: null, dueOn: "2026-10-20", overdue: true, encounterId: null, toothKey: null, treatmentPlanItemId: null, material: null, shade: null, notes: "Bring the film",
  closureNote: null, closedAtUtc: null, rowVersion: "AAEC", createdAtUtc: "2026-10-09T10:00:00Z", updatedAtUtc: "2026-10-10T10:00:00Z", openChaseTaskId: "t1", ...over,
});
const lab = (over: Record<string, unknown> = {}) => ({
  ...referral(), kind: "Lab", id: "l1", partyId: "pa2", partyName: "Smile Dental Lab", summary: "Crown, zirconia", status: "Received", nextStatuses: ["Completed", "Sent", "Cancelled"], toothKey: "36", material: "Zirconia", shade: "A2",
  notes: null, overdue: false, dueOn: null, openChaseTaskId: null, rowVersion: "AAED", ...over,
});
const doc = (over: Record<string, unknown> = {}) => ({ documentId: "d1", fileName: "pano.png", category: "Radiograph", documentDate: "2026-10-01", description: "Panoramic film", status: "Active", broken: false, brokenReason: null, linkedAtUtc: "2026-10-10T10:00:00Z", linkedByUserId: "u1", ...over });
const party = (over: Record<string, unknown> = {}) => ({ id: "pa1", kind: "ReferralProvider", name: "Dr. Rivera Endodontics", contactDetails: "555-010-0100", isActive: true, rowVersion: "P1", createdAtUtc: "2026-10-01T00:00:00Z", updatedAtUtc: "2026-10-01T00:00:00Z", ...over });

type Call = { method: string; url: string; body: any; headers: Record<string, string> };
let calls: Call[];
let reply: (c: Call) => Response | null;
// eslint-disable-next-line @typescript-eslint/no-explicit-any -- test data: referrals and lab cases share a shape but differ in nullable fields
let cases: any[];
let docs: ReturnType<typeof doc>[];
let parties: ReturnType<typeof party>[];
let events: unknown[];
let timeline: unknown[];
let library: unknown[];

beforeEach(() => {
  perms = new Set(["ViewExternalCases", "ManageExternalCases", "ViewDocuments", "ManageExternalParties", "ViewFollowUps", "ManageFollowUps"]);
  calls = []; reply = () => null;
  cases = [referral(), lab()];
  docs = [doc()];
  parties = [party(), party({ id: "pa2", kind: "LabVendor", name: "Smile Dental Lab", contactDetails: null, rowVersion: "P2" })];
  events = [
    { id: "e1", kind: "Created", fromStatus: null, toStatus: "Draft", documentId: null, actorUserId: "u1", actorName: "hana", occurredAtUtc: "2026-10-09T10:00:00Z", note: null },
    { id: "e2", kind: "StatusChanged", fromStatus: "Draft", toStatus: "Sent", documentId: null, actorUserId: "u1", actorName: "hana", occurredAtUtc: "2026-10-10T10:00:00Z", note: "Sent by courier" },
  ];
  timeline = [{ caseKind: "Referral", caseId: "r1", caseSummary: "Retreatment of tooth 19", caseStatus: "Sent", eventKind: "StatusChanged", fromStatus: "Draft", toStatus: "Sent", documentId: null, actorUserId: "u1", actorName: "hana", occurredAtUtc: "2026-10-10T10:00:00Z", note: null }];
  library = [{ kind: "Document", id: "d2", title: "Bitewing", categoryName: "Radiograph", date: "2026-10-02", document: { id: "d2" } }, { kind: "Document", id: "d1", title: "pano", categoryName: "Radiograph", date: "2026-10-01", document: { id: "d1" } }];
  vi.stubGlobal("fetch", vi.fn().mockImplementation((url: string, init?: RequestInit) => {
    const u = String(url); const method = init?.method ?? "GET";
    const body = init?.body ? JSON.parse(String(init.body)) : undefined;
    if (u.includes("csrf-token")) return Promise.resolve(json({ token: "t" }));
    const call = { method, url: u, body, headers: (init?.headers ?? {}) as Record<string, string> }; calls.push(call);
    const custom = reply(call);
    if (custom) return Promise.resolve(custom);
    if (method === "GET" && u.startsWith("/api/external-cases")) {
      const q = new URL(u, "http://x").searchParams;
      const shown = cases.filter((c) => (!q.get("kind") || c.kind === q.get("kind")) && (!q.get("patientId") || c.patientId === q.get("patientId")) && (q.get("overdue") !== "true" || c.overdue));
      return Promise.resolve(json({ items: shown.map((c) => ({ case: c, patientName: "Ann Lee" })), total: shown.length, page: 1, pageSize: 25, counts: { open: 2, overdue: 1, closed: 0 } }));
    }
    const m = /^\/api\/(referrals|lab-cases)\/([^/]+)(\/[a-z]+)?(\/[^/]+)?$/.exec(u);
    if (m && method === "GET" && !m[3]) return Promise.resolve(json(cases.find((c) => c.id === (m[1] === "referrals" ? "r1" : "l1")) ?? null));
    if (m && method === "GET" && m[3] === "/history") return Promise.resolve(json(events));
    if (m && method === "GET" && m[3] === "/documents") return Promise.resolve(json(docs));
    if (m && method === "PUT" && m[3] === "/documents") { docs = [...docs, doc({ documentId: m[4]!.slice(1), fileName: "Bitewing" })]; return Promise.resolve(json({ documentId: m[4]!.slice(1), linked: true, replayed: false })); }
    if (m && method === "DELETE" && m[3] === "/documents") { docs = docs.filter((d) => d.documentId !== m[4]!.slice(1)); return Promise.resolve(json({ documentId: m[4]!.slice(1), linked: false, replayed: false })); }
    if (m && method === "POST" && m[3] === "/status") return Promise.resolve(json({ case: { ...(m[1] === "referrals" ? referral() : lab()), status: body.toStatus, rowVersion: "NEW", nextStatuses: [], overdue: false }, replayed: false }));
    if (method === "PUT" && /^\/api\/(referrals|lab-cases)\//.test(u)) return Promise.resolve(json({ case: { ...(u.includes("referrals") ? referral() : lab()), summary: body.reason ?? body.title, rowVersion: "NEW" }, replayed: false }));
    if (method === "POST" && /\/(referrals|lab-cases)$/.test(u)) return Promise.resolve(json({ case: { ...(u.endsWith("referrals") ? referral({ id: "r9" }) : lab({ id: "l9" })), summary: body.reason ?? body.title }, replayed: false }));
    if (method === "GET" && /external-cases\/timeline$/.test(u)) return Promise.resolve(json(timeline));
    if (method === "GET" && u.startsWith("/api/patients/p1/documents")) return Promise.resolve(json({ patientId: "p1", entries: library, maxUploadBytes: 1 }));
    if (method === "GET" && u.startsWith("/api/external-parties")) {
      const q = new URL(u, "http://x").searchParams;
      return Promise.resolve(json(parties.filter((p) => (!q.get("kind") || p.kind === q.get("kind")) && (q.get("includeInactive") === "true" || p.isActive))));
    }
    if (method === "POST" && u === "/api/external-parties") return Promise.resolve(json(party({ id: "pa9", name: body.name, kind: body.kind, contactDetails: body.contactDetails })));
    if (method === "PUT" && u.startsWith("/api/external-parties/")) return Promise.resolve(json(party({ name: body.name, contactDetails: body.contactDetails, rowVersion: "P1b" })));
    if (method === "POST" && /external-parties\/[^/]+\/active$/.test(u)) return Promise.resolve(json(party({ isActive: body.active })));
    if (method === "GET" && u.startsWith("/api/work-queue")) {
      const t = { id: "t1", patientId: "p1", title: "Referral to follow up: Dr. Rivera Endodontics", note: null, dueAtUtc: "2026-10-20T14:00:00Z", status: "Open", overdue: true, assigneeUserId: null, assigneeName: null, recallIntervalDays: null, recallOfTaskId: null, recallTaskId: null,
        createdByUserId: null, createdAtUtc: "2026-10-20T14:00:00Z", closedAtUtc: null, closedByUserId: null, closureNote: null, rowVersion: "AAEC", priority: "High", kind: "ReferralFollowUp", appointmentId: null, sourcePlanItemId: null, closedBySystem: false, sourceReferralCaseId: "r1", sourceLabCaseId: null };
      return Promise.resolve(json({ items: [{ task: t, patientName: "Ann Lee", appointmentStartUtc: null, urgency: "Overdue" }], total: 1, page: 1, pageSize: 25, counts: { overdue: 1, dueSoon: 0, unassigned: 1, mine: 0, all: 1 }, dueSoonDays: 7 }));
    }
    if (method === "GET" && u === "/api/follow-ups/assignees") return Promise.resolve(json([]));
    return Promise.resolve(json({ error: "unexpected" }, 500));
  }));
});

afterEach(() => vi.unstubAllGlobals());

const sent = (method: string, matcher: RegExp) => calls.filter((c) => c.method === method && matcher.test(c.url));
const detail = (route = "referrals", id = "r1") => render(
  <MemoryRouter initialEntries={[`/external-cases/${route}/${id}`]}><Routes><Route path="/external-cases/:route/:id" element={<ExternalCaseDetailPage />} /></Routes></MemoryRouter>);

describe("ALV-009-C02 case list", () => {
  it("shows the cases with kind, party, status and due date in words, OVERDUE as text, the chip counts and the links", async () => {
    render(<MemoryRouter><ExternalCasesPage /></MemoryRouter>);
    expect(await screen.findByRole("heading", { name: "Referrals and lab cases" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Retreatment of tooth 19" })).toHaveAttribute("href", "/external-cases/referrals/r1");
    expect(screen.getByRole("link", { name: "Crown, zirconia" })).toHaveAttribute("href", "/external-cases/lab-cases/l1");
    expect(screen.getAllByRole("link", { name: "Ann Lee" })[0]).toHaveAttribute("href", "/patients/p1/external-cases");
    expect(screen.getByText("Overdue")).toBeInTheDocument();
    expect(screen.getByText("Dr. Rivera Endodontics")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Open (2)" })).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByRole("button", { name: "Overdue (1)" })).toBeInTheDocument();
    expect(screen.getByRole("table", { name: /Referrals and lab cases, 2 cases/ })).toBeInTheDocument();
  });

  it("asks for the view, kind and status that were chosen", async () => {
    render(<MemoryRouter><ExternalCasesPage /></MemoryRouter>);
    await screen.findByText("Retreatment of tooth 19");
    await userEvent.click(screen.getByRole("button", { name: "Overdue (1)" }));
    await waitFor(() => expect(sent("GET", /overdue=true/).length).toBeGreaterThan(0));
    await userEvent.click(screen.getByRole("button", { name: "Finished (0)" }));
    await waitFor(() => expect(sent("GET", /status=closed/).length).toBeGreaterThan(0));
    await userEvent.selectOptions(screen.getByLabelText("Kind"), "Lab");
    await waitFor(() => expect(sent("GET", /kind=Lab/).length).toBeGreaterThan(0));
    await userEvent.selectOptions(screen.getByLabelText("Status"), "Sent");
    await waitFor(() => expect(sent("GET", /status=Sent/).length).toBeGreaterThan(0));
  });

  it("says it could not load rather than showing an empty list, and says so for an empty view", async () => {
    reply = (c) => (c.url.startsWith("/api/external-cases") ? err(500, "x") : null);
    const view = render(<MemoryRouter><ExternalCasesPage /></MemoryRouter>);
    expect(await screen.findByRole("alert")).toHaveTextContent("Could not load the cases");
    view.unmount();
    reply = () => null; cases = [];
    render(<MemoryRouter><ExternalCasesPage /></MemoryRouter>);
    expect(await screen.findByText("No cases in this view")).toBeInTheDocument();
  });

  it("has no serious accessibility violations", async () => {
    const view = render(<MemoryRouter><ExternalCasesPage /></MemoryRouter>);
    await screen.findByText("Retreatment of tooth 19");
    expect((await axe(view.container)).violations).toEqual([]);
  });
});

describe("ALV-009-C02 case detail", () => {
  it("shows the case, its dates and overdue in words, the chase task, the documents and the history with who did each thing", async () => {
    detail();
    expect(await screen.findByRole("heading", { name: "Retreatment of tooth 19" })).toBeInTheDocument();
    expect(screen.getByText("Overdue")).toBeInTheDocument();
    expect(screen.getByText("Bring the film")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "work queue" })).toHaveAttribute("href", "/work-queue");
    expect(await screen.findByText("pano.png")).toBeInTheDocument();
    const history = screen.getByRole("list", { name: "Case history" });
    expect(within(history).getAllByRole("listitem")[1]).toHaveTextContent("Draft to Sent by hana");
    expect(within(history).getAllByRole("listitem")[1]).toHaveTextContent("Sent by courier");
    expect(screen.getByRole("button", { name: "Report received" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Cancel the case" })).toBeInTheDocument();
  });

  it("moves the case with its date and the version it was showing and says what happened", async () => {
    detail();
    await userEvent.click(await screen.findByRole("button", { name: "Report received" }));
    await userEvent.click(screen.getAllByRole("button", { name: "Report received" }).find((b) => (b as HTMLButtonElement).type === "submit")!);
    expect(await screen.findByText("Moved to Received.")).toBeInTheDocument();
    const move = sent("POST", /r1\/status$/)[0];
    expect(move.body).toMatchObject({ toStatus: "Received", rowVersion: "AAEC", note: null });
    expect(move.body.date).toMatch(/^\d{4}-\d{2}-\d{2}$/);
  });

  it("needs a reason to cancel and to send a lab case back for a remake, and checks before sending", async () => {
    detail();
    await userEvent.click(await screen.findByRole("button", { name: "Cancel the case" }));
    await userEvent.click(screen.getAllByRole("button", { name: "Cancel the case" }).find((b) => (b as HTMLButtonElement).type === "submit")!);
    expect(screen.getByRole("alert")).toHaveTextContent("Write why the case is being cancelled.");
    expect(sent("POST", /status$/)).toHaveLength(0);
    await userEvent.type(screen.getByLabelText(/^Reason/), "Patient went elsewhere");
    await userEvent.click(screen.getAllByRole("button", { name: "Cancel the case" }).find((b) => (b as HTMLButtonElement).type === "submit")!);
    expect(await screen.findByText("Moved to Cancelled.")).toBeInTheDocument();
    expect(sent("POST", /status$/)[0].body).toMatchObject({ toStatus: "Cancelled", note: "Patient went elsewhere" });
  });

  it("sends a received lab case back for a remake only with a note", async () => {
    detail("lab-cases", "l1");
    await userEvent.click(await screen.findByRole("button", { name: "Send back for a remake" }));
    await userEvent.click(screen.getAllByRole("button", { name: "Send back for a remake" }).find((b) => (b as HTMLButtonElement).type === "submit")!);
    expect(screen.getByRole("alert")).toHaveTextContent("Write why the work is being sent back.");
    await userEvent.type(screen.getByLabelText(/^Reason/), "Margin open");
    await userEvent.click(screen.getAllByRole("button", { name: "Send back for a remake" }).find((b) => (b as HTMLButtonElement).type === "submit")!);
    expect(await screen.findByText("Moved to Sent.")).toBeInTheDocument();
    expect(sent("POST", /l1\/status$/)[0].body).toMatchObject({ toStatus: "Sent", note: "Margin open" });
    expect(screen.getByText("Zirconia")).toBeInTheDocument();
  });

  it("explains a case someone else moved, offers a reload and stores nothing", async () => {
    reply = (c) => (c.method === "POST" && c.url.endsWith("/status") ? err(409, "case_changed") : null);
    detail();
    await userEvent.click(await screen.findByRole("button", { name: "Report received" }));
    await userEvent.click(screen.getAllByRole("button", { name: "Report received" }).find((b) => (b as HTMLButtonElement).type === "submit")!);
    expect(await screen.findByRole("alert")).toHaveTextContent("Someone else changed this case");
    reply = () => null;
    await userEvent.click(screen.getByRole("button", { name: "Reload the case" }));
    expect(await screen.findByText("Reloaded the case.")).toBeInTheDocument();
  });

  it("edits the case with the version it was showing", async () => {
    detail();
    await userEvent.click(await screen.findByRole("button", { name: "Edit" }));
    const reason = screen.getByLabelText(/^Reason for the referral/);
    await userEvent.clear(reason);
    await userEvent.type(reason, "Updated reason");
    await userEvent.click(screen.getByRole("button", { name: "Save changes" }));
    expect(await screen.findByText("Saved the changes.")).toBeInTheDocument();
    expect(sent("PUT", /referrals\/r1$/)[0].body).toMatchObject({ reason: "Updated reason", partyId: "pa1", rowVersion: "AAEC", dueOn: "2026-10-20" });
  });

  it("refuses an empty reason before sending anything", async () => {
    detail();
    await userEvent.click(await screen.findByRole("button", { name: "Edit" }));
    await userEvent.clear(screen.getByLabelText(/^Reason for the referral/));
    await userEvent.click(screen.getByRole("button", { name: "Save changes" }));
    expect(screen.getByRole("alert")).toHaveTextContent("Fix the marked fields");
    expect(screen.getByText(/Write the reason for the referral/)).toBeInTheDocument();
    expect(sent("PUT", /referrals/)).toHaveLength(0);
  });

  it("links one of the patient's other documents, removes a link and flags a broken one", async () => {
    docs = [doc(), doc({ documentId: "d3", fileName: "scan.png", broken: true, brokenReason: "The document's file is missing from storage." })];
    detail();
    expect(await screen.findByText("Broken link: The document's file is missing from storage.")).toBeInTheDocument();
    const pick = await screen.findByLabelText(/Link another of this patient/);
    await waitFor(() => expect(within(pick).getByRole("option", { name: /Bitewing/ })).toBeInTheDocument());
    expect(within(pick).queryByRole("option", { name: /^pano/ })).toBeNull();                                  // already linked
    await userEvent.selectOptions(pick, "d2");
    await userEvent.click(screen.getByRole("button", { name: "Link document" }));
    expect(await screen.findByText("Document linked.")).toBeInTheDocument();
    expect(sent("PUT", /r1\/documents\/d2$/)).toHaveLength(1);
    await userEvent.click(screen.getByRole("button", { name: "Remove the link to pano.png" }));
    expect(await screen.findByText(/Removed the link to pano.png/)).toBeInTheDocument();
    expect(sent("DELETE", /r1\/documents\/d1$/)).toHaveLength(1);
  });

  it("shows a role that can only read the case no action buttons and no document picker", async () => {
    perms = new Set(["ViewExternalCases", "ViewDocuments"]);
    detail();
    expect(await screen.findByText("pano.png")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Edit" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Report received" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Link document" })).toBeNull();
    expect(screen.queryByRole("button", { name: /Remove the link/ })).toBeNull();
    expect(screen.getByText(/can read this case but cannot change it/)).toBeInTheDocument();
  });

  it("does not show linked documents to a role that cannot open the library", async () => {
    perms = new Set(["ViewExternalCases"]);
    detail();
    expect(await screen.findByText(/cannot open the document library/)).toBeInTheDocument();
    expect(sent("GET", /documents$/)).toHaveLength(0);
  });

  it("shows a finished case without actions and says it could not load a missing case", async () => {
    cases = [referral({ status: "Cancelled", nextStatuses: [], closureNote: "Patient moved away", overdue: false })];
    const view = detail();
    expect(await screen.findByText("Patient moved away")).toBeInTheDocument();
    expect(screen.queryByRole("group", { name: "Case actions" })).toBeNull();
    view.unmount();
    reply = (c) => (/^\/api\/referrals\/r1/.test(c.url) ? err(404, "case_not_found") : null);
    detail();
    expect(await screen.findByText("Could not load the case")).toBeInTheDocument();
  });

  it("has no serious accessibility violations, with and without a form open", async () => {
    const view = detail();
    await screen.findByText("pano.png");
    expect((await axe(view.container)).violations).toEqual([]);
    await userEvent.click(screen.getByRole("button", { name: "Edit" }));
    await screen.findByLabelText(/^Reason for the referral/);
    expect((await axe(view.container)).violations).toEqual([]);
  });
});

describe("ALV-009-C02 a patient's referrals and lab cases", () => {
  const panel = (canManage = true) => render(<MemoryRouter><PatientExternalCasesPanel patientId="p1" canManage={canManage} /></MemoryRouter>);

  it("lists the patient's cases and what happened, and offers new cases only to a role that manages them", async () => {
    panel();
    expect(await screen.findByRole("list", { name: "Cases of this patient" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Crown, zirconia" })).toBeInTheDocument();
    expect(within(screen.getByRole("list", { name: "Referral and lab timeline" })).getByText(/Draft to Sent/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "New referral" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "New lab case" })).toBeInTheDocument();
  });

  it("shows a role that can only read no way to start a case", async () => {
    panel(false);
    await screen.findByRole("list", { name: "Cases of this patient" });
    expect(screen.queryByRole("button", { name: "New referral" })).toBeNull();
    expect(screen.getByText(/can read referrals and lab cases but cannot start or change them/)).toBeInTheDocument();
  });

  it("starts a referral: checks the form first, names the party, and makes one case however many times it is saved", async () => {
    panel();
    await userEvent.click(await screen.findByRole("button", { name: "New referral" }));
    await userEvent.click(screen.getByRole("button", { name: "Save the referral" }));
    expect(screen.getByRole("alert")).toHaveTextContent("Fix the marked fields");
    expect(screen.getByText("Choose who the patient is being referred to.")).toBeInTheDocument();
    expect(sent("POST", /referrals$/)).toHaveLength(0);
    await waitFor(() => expect(within(screen.getByLabelText(/^Refer to/)).getByRole("option", { name: "Dr. Rivera Endodontics" })).toBeInTheDocument());
    await userEvent.selectOptions(screen.getByLabelText(/^Refer to/), "pa1");
    await userEvent.type(screen.getByLabelText(/^Reason for the referral/), "Evaluation of an impacted tooth");
    await userEvent.type(screen.getByLabelText(/^Follow up by/), "2026-11-15");
    reply = (c) => (c.method === "POST" && c.url.endsWith("/referrals") && sent("POST", /referrals$/).length === 1 ? err(500, "boom") : null);   // the first attempt fails
    await userEvent.click(screen.getByRole("button", { name: "Save the referral" }));
    expect(await screen.findByRole("alert")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Save the referral" }));                           // the retry
    expect(await screen.findByText("Saved: Evaluation of an impacted tooth.")).toBeInTheDocument();
    const posts = sent("POST", /referrals$/);
    expect(posts[0].body).toMatchObject({ partyId: "pa1", reason: "Evaluation of an impacted tooth", dueOn: "2026-11-15", encounterId: null });
    expect(posts[0].headers["Idempotency-Key"]).toBeTruthy();
    expect(posts[1].headers["Idempotency-Key"]).toBe(posts[0].headers["Idempotency-Key"]);                      // the same attempt keeps its key
  });

  it("starts a lab case with its tooth, material and shade, and refuses a tooth that is not two digits", async () => {
    panel();
    await userEvent.click(await screen.findByRole("button", { name: "New lab case" }));
    await waitFor(() => expect(within(screen.getByLabelText(/^Laboratory/)).getByRole("option", { name: "Smile Dental Lab" })).toBeInTheDocument());
    await userEvent.selectOptions(screen.getByLabelText(/^Laboratory/), "pa2");
    await userEvent.type(screen.getByLabelText(/^What is being made/), "Crown, zirconia");
    await userEvent.type(screen.getByLabelText(/^Tooth/), "3x");
    await userEvent.click(screen.getByRole("button", { name: "Save the lab case" }));
    expect(screen.getByText("Enter the tooth as its two-digit number, or leave it blank.")).toBeInTheDocument();
    await userEvent.clear(screen.getByLabelText(/^Tooth/));
    await userEvent.type(screen.getByLabelText(/^Tooth/), "36");
    await userEvent.type(screen.getByLabelText(/^Material/), "Zirconia");
    await userEvent.type(screen.getByLabelText(/^Shade/), "A2");
    await userEvent.click(screen.getByRole("button", { name: "Save the lab case" }));
    expect(await screen.findByText("Saved: Crown, zirconia.")).toBeInTheDocument();
    expect(sent("POST", /lab-cases$/)[0].body).toMatchObject({ partyId: "pa2", title: "Crown, zirconia", toothKey: "36", material: "Zirconia", shade: "A2", treatmentPlanItemId: null });
  });

  it("says who adds an outside party when there is none yet", async () => {
    parties = [];
    panel();
    await userEvent.click(await screen.findByRole("button", { name: "New referral" }));
    expect(await screen.findByText(/no active referral provider yet/)).toBeInTheDocument();
  });

  it("says it could not load rather than showing an empty list", async () => {
    reply = (c) => (c.url.startsWith("/api/external-cases") ? err(500, "x") : null);
    panel();
    expect(await screen.findByRole("alert")).toHaveTextContent("Could not load the referrals and lab cases");
  });

  it("has no serious accessibility violations, with the form open", async () => {
    const view = panel();
    await userEvent.click(await screen.findByRole("button", { name: "New lab case" }));
    await screen.findByLabelText(/^Laboratory/);
    expect((await axe(view.container)).violations).toEqual([]);
  });
});

describe("ALV-009-C02 outside parties", () => {
  const page = () => render(<MemoryRouter><ExternalPartiesPage /></MemoryRouter>);

  it("lists every party with its kind and status and has no serious accessibility violations", async () => {
    const view = page();
    expect(await screen.findByText("Dr. Rivera Endodontics")).toBeInTheDocument();
    expect(screen.getByText("Referral provider", { selector: "td" })).toBeInTheDocument();
    expect(screen.getByText("Lab vendor", { selector: "td" })).toBeInTheDocument();
    expect((await axe(view.container)).violations).toEqual([]);
  });

  it("adds a party once the name is checked and sends the kind, name and contact", async () => {
    page();
    await userEvent.click(await screen.findByRole("button", { name: "Add the party" }));
    expect(screen.getByRole("alert")).toHaveTextContent("Write the name");
    expect(sent("POST", /external-parties$/)).toHaveLength(0);
    await userEvent.type(screen.getByLabelText(/^Name/), "Northside Oral Surgery");
    await userEvent.selectOptions(screen.getByLabelText("Kind"), "ReferralProvider");
    await userEvent.type(screen.getByLabelText(/^Contact details/), "555-020-0200");
    await userEvent.click(screen.getByRole("button", { name: "Add the party" }));
    expect(await screen.findByText("Added: Northside Oral Surgery.")).toBeInTheDocument();
    expect(sent("POST", /external-parties$/)[0].body).toEqual({ kind: "ReferralProvider", name: "Northside Oral Surgery", contactDetails: "555-020-0200" });
  });

  it("explains a name that is already taken", async () => {
    reply = (c) => (c.method === "POST" && c.url === "/api/external-parties" ? err(409, "party_exists") : null);
    page();
    await userEvent.type(await screen.findByLabelText(/^Name/), "Dr. Rivera Endodontics");
    await userEvent.click(screen.getByRole("button", { name: "Add the party" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("A party with that name already exists.");
  });

  it("edits a party with its version and makes one inactive or active again", async () => {
    page();
    await userEvent.click(await screen.findByRole("button", { name: "Edit Dr. Rivera Endodontics" }));
    const name = screen.getByLabelText("Name of Dr. Rivera Endodontics");
    await userEvent.clear(name);
    await userEvent.type(name, "Dr. Rivera Endodontics PC");
    await userEvent.click(screen.getByRole("button", { name: "Save" }));
    expect(await screen.findByText("Saved: Dr. Rivera Endodontics PC.")).toBeInTheDocument();
    expect(sent("PUT", /external-parties\/pa1$/)[0].body).toMatchObject({ name: "Dr. Rivera Endodontics PC", rowVersion: "P1" });
    await userEvent.click(screen.getByRole("button", { name: "Make inactive: Smile Dental Lab" }));
    expect(await screen.findByText("Smile Dental Lab is now inactive.")).toBeInTheDocument();
    expect(sent("POST", /pa2\/active$/)[0].body).toEqual({ active: false, rowVersion: "P2" });
  });

  it("explains a party someone else changed and offers a reload", async () => {
    reply = (c) => (c.method === "PUT" ? err(409, "party_changed") : null);
    page();
    await userEvent.click(await screen.findByRole("button", { name: "Edit Dr. Rivera Endodontics" }));
    await userEvent.click(screen.getByRole("button", { name: "Save" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Someone else changed this party");
    reply = () => null;
    await userEvent.click(screen.getByRole("button", { name: "Reload the list" }));
    expect(await screen.findByText("Reloaded the list.")).toBeInTheDocument();
  });

  it("says it could not load", async () => {
    reply = (c) => (c.url.startsWith("/api/external-parties") ? err(500, "x") : null);
    page();
    expect(await screen.findByRole("alert")).toHaveTextContent("Could not load the outside parties");
  });
});

describe("ALV-009-C02 the work queue names the case a chase task is about", () => {
  it("links a referral follow-up task to its case", async () => {
    render(<MemoryRouter><WorkQueuePage /></MemoryRouter>);
    expect(await screen.findByText("Referral to follow up: Dr. Rivera Endodontics")).toBeInTheDocument();
    expect(screen.getByText("Referral follow-up", { selector: "span" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open the case" })).toHaveAttribute("href", "/external-cases/referrals/r1");
  });
});
