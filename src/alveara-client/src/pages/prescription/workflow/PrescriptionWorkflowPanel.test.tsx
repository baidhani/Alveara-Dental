import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import type { PatientDetail } from "../../../services/patientsApi";
import type { AllergyAssessment } from "../../../services/prescriptionApi";
import type { DraftCheck, DraftView, PrescriptionRecord, PrintContent, SafetyCheck, WorkflowOptions } from "../../../services/prescriptionWorkflowApi";
import type { SafetyContext, SafetyEntry } from "../../../services/safetyApi";
import { parseQuantity, parseRefills } from "./workflowRules";
import { PrescriptionWorkflowPanel } from "./PrescriptionWorkflowPanel";

/**
 * ALV-016-C01: the prescription composer against a scripted API. The rules are proven by the backend tests and the real-backend walkthrough; what is proven here is the screen: who gets the composer, that the
 * banner says no drug-knowledge source is configured, that a draft is saved incomplete and finalized only when complete, that every wrong entry is named beside its box with what was typed kept, that a changed
 * allergy or medication list is shown and must be acknowledged, that finalizing goes through a confirmation, that cancelling needs a reason, and that a print that cannot be prepared says so.
 */
const patient = { id: "p1" } as PatientDetail;
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

const options: WorkflowOptions = {
  doseUnits: ["mg", "mcg", "g"], doseTypingCeilings: { mg: 5000, mcg: 5000, g: 10 }, frequencies: ["Once daily", "Twice daily", "Three times daily"], quantityUnits: ["tablet", "capsule", "mL"],
  quantityTypingCeiling: 1000, refillsMax: 12, instructionsMax: 500, medicationMax: 200, reasonMax: 500, decisionSupportConfigured: false,
  decisionSupportMessage: "Drug interaction checking and clinical dose-appropriateness checking are not configured in this application.",
  doseLimitation: "The dose is checked for its form only. It is not checked against any clinical dose range.", quantityLimitation: "The quantity and refills are checked for their form only, not against any clinical or legal limit.",
  allergyLimitation: "This check compares the medication's name with the recorded allergies, so no recorded match is not a clearance.",
};
const assessment = (outcome: AllergyAssessment["outcome"] = "NoRecordedMatch"): AllergyAssessment => ({
  outcome, matches: outcome === "RecordedAllergyMatch" ? [{ name: "Penicillin", severity: "Severe", reaction: "Hives" }] : [], listState: outcome === "NotEstablished" ? "NotEstablished" : "Established",
  message: outcome === "NoRecordedMatch" ? "No allergy recorded for this patient matches this medication's name. This is a name comparison only, not a clearance." : "This medication matches an allergy recorded for this patient.",
  limitation: options.allergyLimitation,
});
const safetyCheck = (over: Partial<SafetyCheck> = {}): SafetyCheck => ({ fingerprint: "fp1", changedSinceAcknowledged: false, added: [], removed: [], assessment: assessment(), reviewRequired: false, reviewProblem: null, ...over });
const draftOf = (over: Partial<DraftView> = {}): DraftView => ({
  id: "d1", patientId: "p1", medication: "Amoxicillin", doseValue: 500, doseUnit: "mg", frequency: "Three times daily", instructions: "Take one capsule with food.", quantityValue: 21, quantityUnit: "capsule", refills: 0,
  reviewNote: null, status: "Draft", supersedesPrescriptionId: null, finalizedPrescriptionId: null, discardedReason: null, safetyFingerprint: "fp1", createdByName: "Dr. Okafor", createdAtUtc: "2030-02-01T10:00:00Z",
  updatedByName: "Dr. Okafor", updatedAtUtc: "2030-02-01T10:00:00Z", rowVersion: "rv1", ...over,
});
const recordOf = (over: Partial<PrescriptionRecord> = {}): PrescriptionRecord => ({
  id: "rx1", patientId: "p1", medication: "Amoxicillin", doseValue: 500, doseUnit: "mg", frequency: "Three times daily", instructions: "Take one capsule with food.", quantityValue: 21, quantityUnit: "capsule", refills: 0,
  allergyOutcome: "NoRecordedMatch", allergyListState: "Established", allergyMatches: null, reviewNote: null, prescribedByName: "Dr. Okafor", prescribedAtUtc: "2030-02-02T10:00:00Z", status: "Active",
  supersedesPrescriptionId: null, supersededByPrescriptionId: null, hasDetails: true,
  events: [{ eventType: "Finalized", reason: null, actorName: "Dr. Okafor", occurredAtUtc: "2030-02-02T10:00:00Z", supersededByPrescriptionId: null }], ...over,
});
const printOf = (over: Partial<PrintContent> = {}): PrintContent => ({
  prescriptionId: "rx1", status: "Active", practice: { name: "Alveara Dental", phone: "555-0100", addressLine: "1 Main St" }, patientName: "Ann Alder", patientDateOfBirth: "1985-03-09T00:00:00", prescriberName: "Dr. Okafor",
  medication: "Amoxicillin", dose: "500 mg", frequency: "Three times daily", instructions: "Take one capsule with food.", quantity: "21 capsule", refills: 0, prescribedAtUtc: "2030-02-02T10:00:00Z",
  allergyStatement: "At the time of prescribing no recorded allergy matched this medication's name. That is a name comparison, not a clearance.", limitationStatement: options.decisionSupportMessage,
  supersedesPrescriptionId: null, supersededByPrescriptionId: null, contentHash: "abcdef012345abcdef", ...over,
});
const entry = (title: string): SafetyEntry => ({
  origin: "ClinicalRecord", id: `A-${title}`, category: "Allergy", title, detail: "Hives", severity: "High" as SafetyEntry["severity"], status: "Active", source: "Clinical record", sourceItemId: null,
  lastUpdatedAtUtc: "2030-01-01T10:00:00Z", lastUpdatedByName: "Dr. Okafor", needsAttention: false, attentionReason: null, acknowledgedByMe: false, acknowledgedAtUtc: null, revision: null, rowVersion: null,
  resolvedAtUtc: null, resolvedByName: null, resolutionReason: null,
} as SafetyEntry);
const safetyOf = (): SafetyContext => ({
  patientId: "p1", asOfUtc: "2030-01-01T10:00:00Z", entries: [entry("Latex")], resolved: [], clearances: [], gaps: [],
  summary: { patientId: "p1", asOfUtc: "2030-01-01T10:00:00Z", activeAlertCount: 0, activeAllergyCount: 1, currentMedicationCount: 0, highestSeverity: null, openClearanceCount: 0, unacknowledgedAlertCount: 0, needsAttentionCount: 0, gaps: [] },
} as unknown as SafetyContext);

interface Call { url: string; method: string; body: Record<string, unknown> | null }
interface Script {
  drafts: DraftView[]; records: PrescriptionRecord[]; detail: { draft: DraftView; safety: SafetyCheck }; loadFails: boolean; safety?: SafetyContext;
  create: (b: Record<string, unknown>) => Response; update: (b: Record<string, unknown>) => Response; check: () => Response; ack: (b: Record<string, unknown>) => Response; finalize: (b: Record<string, unknown>) => Response;
  discard: (b: Record<string, unknown>) => Response; amend: () => Response; cancel: (b: Record<string, unknown>) => Response; discontinue: (b: Record<string, unknown>) => Response; print: () => Response;
}
const okCheck = (over: Partial<DraftCheck> = {}): DraftCheck => ({ problems: [], safety: safetyCheck(), canFinalize: true, doseLimitation: options.doseLimitation, quantityLimitation: options.quantityLimitation, allergyLimitation: options.allergyLimitation, decisionSupport: options.decisionSupportMessage, ...over });

function stub(over: Partial<Script> = {}) {
  const script: Script = {
    drafts: [], records: [], detail: { draft: draftOf(), safety: safetyCheck() }, loadFails: false,
    create: () => json({ draft: draftOf(), replayed: false }), update: () => json(draftOf({ rowVersion: "rv2" })), check: () => json(okCheck()), ack: () => json(draftOf({ rowVersion: "rv3" })),
    finalize: () => json({ record: recordOf(), replayed: false }), discard: () => json(draftOf({ status: "Discarded" })), amend: () => json({ draft: draftOf({ id: "d9", supersedesPrescriptionId: "rx1" }), replayed: false }),
    cancel: () => json({ record: recordOf({ status: "Cancelled" }), replayed: false }), discontinue: () => json({ record: recordOf({ status: "Discontinued" }), replayed: false }), print: () => json(printOf()), ...over,
  };
  const calls: Call[] = [];
  vi.stubGlobal("fetch", vi.fn().mockImplementation((url: RequestInfo | URL, init?: RequestInit) => {
    const u = String(url);
    const method = init?.method ?? "GET";
    const body = init?.body ? (JSON.parse(init.body as string) as Record<string, unknown>) : null;
    calls.push({ url: u, method, body });
    if (u.includes("/api/auth/csrf-token")) return Promise.resolve(json({ token: "csrf" }));
    if (u.includes("/prescription-workflow/options")) return Promise.resolve(script.loadFails ? json({ error: "server_error", message: "boom" }, 500) : json(options));
    if (u.endsWith("/prescriptions/check")) return Promise.resolve(json({ problems: [], assessment: assessment(), reviewRequired: false, reviewProblem: null, doseLimitation: "", allergyLimitation: "" }));
    if (u.endsWith("/prescription-drafts") && method === "GET") return Promise.resolve(json({ patientId: "p1", drafts: script.drafts }));
    if (u.endsWith("/prescription-drafts") && method === "POST") return Promise.resolve(script.create(body!));
    if (u.endsWith("/prescription-records")) return Promise.resolve(json({ patientId: "p1", prescriptions: script.records }));
    if (u.endsWith("/safety")) return Promise.resolve(json(script.safety ?? safetyOf()));
    if (u.endsWith("/check")) return Promise.resolve(script.check());
    if (u.endsWith("/acknowledge-safety")) return Promise.resolve(script.ack(body!));
    if (u.endsWith("/finalize")) return Promise.resolve(script.finalize(body!));
    if (u.endsWith("/discard")) return Promise.resolve(script.discard(body!));
    if (u.endsWith("/amend")) return Promise.resolve(script.amend());
    if (u.endsWith("/cancel")) return Promise.resolve(script.cancel(body!));
    if (u.endsWith("/discontinue")) return Promise.resolve(script.discontinue(body!));
    if (u.endsWith("/print")) return Promise.resolve(script.print());
    if (u.includes("/api/prescription-drafts/") && method === "PUT") return Promise.resolve(script.update(body!));
    if (u.includes("/api/prescription-drafts/")) return Promise.resolve(json(script.detail));
    return Promise.resolve(json({}));
  }));
  return { script, calls, posts: (suffix: string) => calls.filter((c) => c.method === "POST" && c.url.endsWith(suffix)), puts: () => calls.filter((c) => c.method === "PUT") };
}

async function open(canPrescribe = true) {
  const view = render(<PrescriptionWorkflowPanel patient={patient} canPrescribe={canPrescribe} />);
  await screen.findByRole("heading", { name: "Prescription composer" });
  return view;
}

type User = ReturnType<typeof userEvent.setup>;
const composer = () => within(screen.getByRole("form", { name: "Prescription composer" }));
async function fillCore(user: User) {
  const c = composer();
  await user.type(c.getByLabelText("Medication"), "Amoxicillin");
  await user.type(c.getByLabelText("Dose"), "500");
  await user.selectOptions(c.getByLabelText("Unit"), "mg");
  await user.selectOptions(c.getByLabelText("How often"), "Three times daily");
}
async function fillComplete(user: User) {
  await fillCore(user);
  const c = composer();
  await user.type(c.getByLabelText("Instructions for the patient"), "Take one capsule with food.");
  await user.type(c.getByLabelText("Quantity"), "21");
  await user.selectOptions(c.getByLabelText("Quantity unit"), "capsule");
  await user.type(c.getByLabelText("Refills"), "0");
}
async function newDraft(user: User) { await user.click(screen.getByRole("button", { name: "New prescription draft" })); await screen.findByRole("form", { name: "Prescription composer" }); }

afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks(); });

describe("the number boxes", () => {
  it("reads an empty box as not given and anything that is not a number as a problem to put right", () => {
    expect(parseQuantity("")).toEqual({ value: null, problem: null });
    expect(parseQuantity(" 12.5 ").value).toBe(12.5);
    expect(parseQuantity("a lot").problem?.code).toBe("not_a_number");
    expect(parseRefills("").value).toBeNull();
    expect(parseRefills("0").value).toBe(0);
    expect(parseRefills("2.5").problem?.code).toBe("not_a_number");
    expect(parseRefills("-1").problem?.code).toBe("not_a_number");
  });
});

describe("who sees what", () => {
  it("always says that no drug-knowledge source is configured", async () => {
    stub();
    await open();
    expect(screen.getByRole("note")).toHaveTextContent("Not configured");
    expect(screen.getByRole("note")).toHaveTextContent("not configured in this application");
  });

  it("gives a read-only role the finalized prescriptions and the print button but no composer and no action", async () => {
    stub({ records: [recordOf()] });
    await open(false);
    expect(screen.queryByRole("button", { name: "New prescription draft" })).not.toBeInTheDocument();
    expect(screen.queryByText("Drafts")).not.toBeInTheDocument();
    expect(screen.getByText(/You can read and print these but your role cannot prescribe/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Print Amoxicillin" })).toBeInTheDocument();
    for (const name of ["Amend Amoxicillin", "Cancel Amoxicillin", "Discontinue Amoxicillin"]) expect(screen.queryByRole("button", { name })).not.toBeInTheDocument();
  });

  it("says it could not load rather than showing an empty list", async () => {
    stub({ loadFails: true });
    render(<PrescriptionWorkflowPanel patient={patient} canPrescribe />);
    expect(await screen.findByText("Could not load the prescriptions")).toBeInTheDocument();
    expect(screen.queryByText(/No finalized prescription/)).not.toBeInTheDocument();
  });

  it("shows what the shared safety framework knows while prescribing: allergies, current medications, other active alerts and what was never reviewed", async () => {
    const full = safetyOf();
    full.entries = [
      entry("Latex"),
      { ...entry("Lisinopril"), id: "M-1", category: "Medication", severity: null, detail: "10 mg · Once daily" } as SafetyEntry,
      { ...entry("Prosthetic heart valve"), id: "C-1", origin: "Alert", category: "Condition", severity: "High", detail: null } as SafetyEntry,
    ];
    full.gaps = [{ section: "Medication", status: "NotReviewed", message: "Medications have not been reviewed. Nothing listed here does not mean none." }] as SafetyContext["gaps"];
    stub({ safety: full });
    const user = userEvent.setup();
    await open();
    await newDraft(user);
    const panel = within(screen.getByRole("complementary", { name: "Patient safety" }));
    expect(await panel.findByRole("list", { name: "Recorded allergies" })).toHaveTextContent("Latex");
    expect(panel.getByRole("list", { name: "Current medications" })).toHaveTextContent("Lisinopril");
    expect(panel.getByRole("list", { name: "Current medications" })).toHaveTextContent("10 mg · Once daily");
    expect(panel.getByRole("list", { name: "Other active alerts" })).toHaveTextContent("Prosthetic heart valve (high)");
    expect(panel.getByRole("list", { name: "What is not established" })).toHaveTextContent("Medications have not been reviewed. Nothing listed here does not mean none.");
  });

  it("shows the persistent safety panel with what is recorded and what is not checked", async () => {
    stub();
    const user = userEvent.setup();
    await open();
    await newDraft(user);
    const panel = within(screen.getByRole("complementary", { name: "Patient safety" }));
    expect(await panel.findByText(/Latex/)).toBeInTheDocument();
    expect(panel.getByText(/drug interactions, and whether a dose is clinically appropriate/)).toBeInTheDocument();
  });
});

describe("a draft", () => {
  it("is saved incomplete with a key, and the next save changes the same draft with its version", async () => {
    const s = stub();
    const user = userEvent.setup();
    await open();
    await newDraft(user);
    await fillCore(user);
    await user.click(composer().getByRole("button", { name: "Save draft" }));
    expect(await screen.findByText("Draft saved. Nothing is final until you finalize it.")).toBeInTheDocument();
    const create = s.posts("/prescription-drafts")[0];
    expect(create.body).toMatchObject({ medication: "Amoxicillin", doseValue: 500, doseUnit: "mg", frequency: "Three times daily", instructions: null, quantityValue: null, quantityUnit: null, refills: null });
    expect(typeof create.body!.idempotencyKey).toBe("string");

    await user.type(composer().getByLabelText("Instructions for the patient"), "With food.");
    await user.click(composer().getByRole("button", { name: "Save draft" }));
    await waitFor(() => expect(s.puts()).toHaveLength(1));
    expect(s.puts()[0].body).toMatchObject({ instructions: "With food.", rowVersion: "rv1" });
  });

  it("keeps its save key through a dropped connection so a retry cannot make two drafts", async () => {
    let first = true;
    const s = stub({ create: () => { if (first) { first = false; throw new TypeError("Failed to fetch"); } return json({ draft: draftOf(), replayed: false }); } });
    vi.spyOn(console, "error").mockImplementation(() => {});
    const user = userEvent.setup();
    await open();
    await newDraft(user);
    await fillCore(user);
    await user.click(composer().getByRole("button", { name: "Save draft" }));
    expect(await screen.findByText(/Nothing was saved: the connection dropped/)).toBeInTheDocument();
    expect(composer().getByLabelText("Medication")).toHaveValue("Amoxicillin");
    await user.click(composer().getByRole("button", { name: "Save draft" }));
    await screen.findByText("Draft saved. Nothing is final until you finalize it.");
    const keys = s.posts("/prescription-drafts").map((c) => c.body!.idempotencyKey);
    expect(keys).toHaveLength(2);
    expect(keys[0]).toBe(keys[1]);
  });

  it("puts a box that is not a number right on the screen and sends nothing", async () => {
    const s = stub();
    const user = userEvent.setup();
    await open();
    await newDraft(user);
    await fillCore(user);
    await user.type(composer().getByLabelText("Quantity"), "a lot");
    await user.type(composer().getByLabelText("Refills"), "two");
    await user.click(composer().getByRole("button", { name: "Save draft" }));
    const list = await screen.findByRole("list", { name: "Entries to correct" });
    expect(within(list).getByText(/Enter the quantity as a number/)).toBeInTheDocument();
    expect(within(list).getByText(/Enter the number of refills as a whole number/)).toBeInTheDocument();
    expect(s.posts("/prescription-drafts")).toHaveLength(0);
  });

  it("names every problem the server finds beside its box and keeps what was typed", async () => {
    stub({ create: () => json({ error: "validation_failed", message: "x", problems: [{ field: "doseValue", code: "above_typing_ceiling", message: "A single dose of 6000 mg is above the 5000 mg this screen accepts. This ceiling only catches typing mistakes; it is not a clinical dose limit." }] }, 400) });
    const user = userEvent.setup();
    await open();
    await newDraft(user);
    await fillCore(user);
    await user.click(composer().getByRole("button", { name: "Save draft" }));
    expect(await screen.findByText("Nothing was saved. Correct the entries marked below.")).toBeInTheDocument();
    expect(composer().getByLabelText("Dose")).toHaveAttribute("aria-invalid", "true");
    expect(composer().getByLabelText("Dose")).toHaveValue("500");
    expect(screen.getAllByText(/not a clinical dose limit/).length).toBeGreaterThan(0);
  });

  it("is discarded only after a confirmation, with an optional reason, and says it is kept", async () => {
    const s = stub({ drafts: [draftOf()] });
    const user = userEvent.setup();
    await open();
    await user.click(await screen.findByRole("button", { name: "Open draft Amoxicillin" }));
    await screen.findByRole("form", { name: "Prescription composer" });
    await user.click(composer().getByRole("button", { name: "Discard draft" }));
    expect(s.posts("/discard")).toHaveLength(0);
    await user.type(composer().getByLabelText(/Why are you discarding this draft/), "Wrong patient.");
    await user.click(composer().getByRole("button", { name: "Confirm discard" }));
    expect(await screen.findByText("The draft was discarded. It is kept in the record and cannot be finalized.")).toBeInTheDocument();
    expect(s.posts("/discard")[0].body).toMatchObject({ rowVersion: "rv1", reason: "Wrong patient." });
    expect(screen.queryByRole("form", { name: "Prescription composer" })).not.toBeInTheDocument();
  });
});

describe("finalizing", () => {
  it("goes through a confirmation that shows what will be recorded and finalizes only when confirmed", async () => {
    const s = stub();
    const user = userEvent.setup();
    await open();
    await newDraft(user);
    await fillComplete(user);
    await user.click(composer().getByRole("button", { name: "Finalize…" }));
    const confirm = await screen.findByRole("region", { name: "Confirm finalization" });
    for (const text of ["Amoxicillin", "500 mg, three times daily", "Take one capsule with food.", "21 capsule", /cannot be edited afterwards/, /Not checked:/]) expect(within(confirm).getAllByText(text).length).toBeGreaterThan(0);
    expect(s.posts("/finalize")).toHaveLength(0);                                                      // nothing is recorded until it is confirmed

    await user.click(within(confirm).getByRole("button", { name: "Finalize prescription" }));
    expect(await screen.findByText("Amoxicillin finalized and logged.")).toBeInTheDocument();
    expect(s.posts("/finalize")[0].body).toEqual({ rowVersion: "rv1" });
    expect(screen.queryByRole("region", { name: "Confirm finalization" })).not.toBeInTheDocument();
  });

  it("goes back to editing with everything typed still there", async () => {
    stub();
    const user = userEvent.setup();
    await open();
    await newDraft(user);
    await fillComplete(user);
    await user.click(composer().getByRole("button", { name: "Finalize…" }));
    await user.click(await screen.findByRole("button", { name: "Back to editing" }));
    expect(composer().getByLabelText("Instructions for the patient")).toHaveValue("Take one capsule with food.");
    expect(composer().getByLabelText("Medication")).toHaveValue("Amoxicillin");
  });

  it("does not open the confirmation for an incomplete draft and names what is missing", async () => {
    const s = stub({ check: () => json(okCheck({ canFinalize: false, problems: [
      { field: "instructions", code: "required", message: "Write the instructions for the patient (how to take it, for how long)." },
      { field: "refills", code: "required", message: "Enter the number of refills (0 to 12; 0 means none)." }] })) });
    const user = userEvent.setup();
    await open();
    await newDraft(user);
    await fillCore(user);
    await user.click(composer().getByRole("button", { name: "Finalize…" }));
    expect(await screen.findByText("This prescription is not ready to finalize. Complete the entries marked below.")).toBeInTheDocument();
    const list = screen.getByRole("list", { name: "Entries to correct" });
    expect(within(list).getAllByRole("listitem")).toHaveLength(2);
    expect(composer().getByLabelText("Instructions for the patient")).toHaveAttribute("aria-invalid", "true");
    expect(screen.queryByRole("region", { name: "Confirm finalization" })).not.toBeInTheDocument();
    expect(s.posts("/finalize")).toHaveLength(0);
  });

  it("asks for the prescribers own words when the allergy check needs them and does not open the confirmation", async () => {
    const s = stub({ check: () => json(okCheck({ canFinalize: false, safety: safetyCheck({ assessment: assessment("RecordedAllergyMatch"), reviewRequired: true,
      reviewProblem: { field: "reviewNote", code: "allergy_review_required", message: "This medication matches a recorded allergy. Say why you are prescribing it anyway, or change the medication." } }) })) });
    const user = userEvent.setup();
    await open();
    await newDraft(user);
    await fillComplete(user);
    await user.click(composer().getByRole("button", { name: "Finalize…" }));
    const note = await composer().findByLabelText(/Why are you prescribing this despite the matching allergy/);
    await waitFor(() => expect(note).toHaveFocus());
    expect(s.posts("/finalize")).toHaveLength(0);
  });

  it("shows what changed in the allergy list and finalizes nothing when the server refuses on a changed list", async () => {
    stub({ finalize: () => json({ error: "safety_context_changed", message: "The patient's allergy or medication list changed since you last looked. Nothing was saved.", problems: [],
      detail: { currentFingerprint: "fp2", added: ["Allergy: Latex (low), Rash"], removed: [] } }, 409) });
    const user = userEvent.setup();
    await open();
    await newDraft(user);
    await fillComplete(user);
    await user.click(composer().getByRole("button", { name: "Finalize…" }));
    await user.click(await screen.findByRole("button", { name: "Finalize prescription" }));
    const alert = await screen.findByRole("alert", { name: /changed since you last looked/ });
    expect(within(alert).getByText("Allergy: Latex (low), Rash")).toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Confirm finalization" })).not.toBeInTheDocument();
  });

  it("refuses to confirm a stale draft and tells the prescriber someone else changed it", async () => {
    stub({ finalize: () => json({ error: "concurrency_conflict", entityType: "PrescriptionDraft", entityId: "d1" }, 409) });
    const user = userEvent.setup();
    await open();
    await newDraft(user);
    await fillComplete(user);
    await user.click(composer().getByRole("button", { name: "Finalize…" }));
    await user.click(await screen.findByRole("button", { name: "Finalize prescription" }));
    expect(await screen.findByText(/changed by someone else since you opened it/)).toBeInTheDocument();
  });

  it("says nothing was saved when the connection drops while finalizing", async () => {
    stub({ finalize: () => { throw new TypeError("Failed to fetch"); } });
    vi.spyOn(console, "error").mockImplementation(() => {});
    const user = userEvent.setup();
    await open();
    await newDraft(user);
    await fillComplete(user);
    await user.click(composer().getByRole("button", { name: "Finalize…" }));
    await user.click(await screen.findByRole("button", { name: "Finalize prescription" }));
    expect(await screen.findByText(/Nothing was saved: the connection dropped/)).toBeInTheDocument();
  });
});

describe("a changed allergy or medication list", () => {
  const changed = () => ({ draft: draftOf(), safety: safetyCheck({ fingerprint: "fp2", changedSinceAcknowledged: true, added: ["Allergy: Latex (low), Rash"], removed: ["Medication: Warfarin, 5 mg daily"] }) });

  it("is listed when a draft is opened and must be acknowledged before finalizing", async () => {
    const s = stub({ drafts: [draftOf()], detail: changed(), check: () => json(okCheck({ canFinalize: false, safety: changed().safety })) });
    const user = userEvent.setup();
    await open();
    await user.click(await screen.findByRole("button", { name: "Open draft Amoxicillin" }));
    const alert = await screen.findByRole("alert", { name: /changed since you last looked/ });
    expect(within(alert).getByText("Allergy: Latex (low), Rash")).toBeInTheDocument();
    expect(within(alert).getByText("Medication: Warfarin, 5 mg daily")).toBeInTheDocument();

    await user.click(composer().getByRole("button", { name: "Finalize…" }));
    expect(await screen.findByText(/changed since you last looked. Read the changes below, then continue./)).toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Confirm finalization" })).not.toBeInTheDocument();
    expect(s.posts("/finalize")).toHaveLength(0);
  });

  it("is acknowledged with the fingerprint that was shown, and the alert goes away", async () => {
    const calm = { draft: draftOf({ rowVersion: "rv3" }), safety: safetyCheck({ fingerprint: "fp2" }) };
    let acknowledged = false;
    const s = stub({ drafts: [draftOf()], detail: changed(), ack: () => { acknowledged = true; return json(draftOf({ rowVersion: "rv3" })); } });
    vi.spyOn(globalThis, "fetch");
    const base = (globalThis.fetch as unknown as ReturnType<typeof vi.fn>).getMockImplementation() as (u: RequestInfo | URL, i?: RequestInit) => Promise<Response>;
    (globalThis.fetch as unknown as ReturnType<typeof vi.fn>).mockImplementation((u: RequestInfo | URL, i?: RequestInit) =>
      acknowledged && String(u).endsWith("/api/prescription-drafts/d1") && (i?.method ?? "GET") === "GET" ? Promise.resolve(json(calm)) : base(u, i));
    const user = userEvent.setup();
    await open();
    await user.click(await screen.findByRole("button", { name: "Open draft Amoxicillin" }));
    await user.click(await screen.findByRole("button", { name: "I have read the changes" }));
    await waitFor(() => expect(screen.queryByRole("alert", { name: /changed since you last looked/ })).not.toBeInTheDocument());
    expect(s.posts("/acknowledge-safety")[0].body).toEqual({ rowVersion: "rv1", seenFingerprint: "fp2" });
    expect(screen.getByText("You have acknowledged the allergy and medication lists as they are now.")).toBeInTheDocument();
  });
});

describe("the finalized prescriptions", () => {
  it("show their status, what was recorded and their history, and say when instructions and quantity were never recorded", async () => {
    stub({ records: [
      recordOf({ id: "a", medication: "Amoxicillin", events: [{ eventType: "Finalized", reason: null, actorName: "Dr. Okafor", occurredAtUtc: "2030-02-02T10:00:00Z", supersededByPrescriptionId: null }] }),
      recordOf({ id: "b", medication: "Naproxen", status: "Cancelled", events: [{ eventType: "Cancelled", reason: "Entered for the wrong patient.", actorName: "Dr. Okafor", occurredAtUtc: "2030-02-03T10:00:00Z", supersededByPrescriptionId: null }] }),
      recordOf({ id: "c", medication: "Ibuprofen", hasDetails: false, instructions: null, quantityValue: null, quantityUnit: null, refills: null, events: [] }),
      recordOf({ id: "d", medication: "Codeine", status: "Superseded" }),
    ] });
    await open();
    const list = screen.getByRole("list", { name: "Finalized prescriptions" });
    expect(within(list).getByText(/Cancelled: it should not be taken or dispensed/)).toBeInTheDocument();
    expect(within(list).getByText(/Superseded: it was replaced by an amendment/)).toBeInTheDocument();
    expect(within(list).getByText(/Saved before instructions, quantity and refills were recorded/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Print Ibuprofen" })).not.toBeInTheDocument();             // nothing complete to print
    expect(screen.getByRole("button", { name: "Print Naproxen" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Cancel Naproxen" })).not.toBeInTheDocument();             // an ended prescription cannot end again
    expect(screen.queryByRole("button", { name: "Amend Codeine" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Amend Amoxicillin" })).toBeInTheDocument();
  });

  it("needs a reason to cancel, keeps the dialog open when one is missing and records it once given", async () => {
    let calls = 0;
    const s = stub({ records: [recordOf()], cancel: () => (++calls === 1
      ? json({ error: "validation_failed", message: "A reason is needed. Nothing was recorded.", problems: [{ field: "reason", code: "required", message: "Say why, in your own words. It is kept with the record." }] }, 400)
      : json({ record: recordOf({ status: "Cancelled" }), replayed: false })) });
    const user = userEvent.setup();
    await open();
    await user.click(screen.getByRole("button", { name: "Cancel Amoxicillin" }));
    await user.click(screen.getByRole("button", { name: "Confirm cancellation" }));
    expect(await screen.findByText(/Nothing was recorded. Say why, in your own words/)).toBeInTheDocument();
    await user.type(screen.getByLabelText(/Why is this prescription being cancelled/), "Entered for the wrong patient.");
    await user.click(screen.getByRole("button", { name: "Confirm cancellation" }));
    expect(await screen.findByText("Amoxicillin cancelled and logged.")).toBeInTheDocument();
    expect(s.posts("/cancel")[1].body).toEqual({ reason: "Entered for the wrong patient." });
  });

  it("discontinues with a reason", async () => {
    const s = stub({ records: [recordOf()] });
    const user = userEvent.setup();
    await open();
    await user.click(screen.getByRole("button", { name: "Discontinue Amoxicillin" }));
    await user.type(screen.getByLabelText(/Why is this prescription being discontinued/), "Stopped after a reaction.");
    await user.click(screen.getByRole("button", { name: "Confirm discontinuation" }));
    expect(await screen.findByText("Amoxicillin discontinued and logged.")).toBeInTheDocument();
    expect(s.posts("/discontinue")[0].body).toEqual({ reason: "Stopped after a reaction." });
  });

  it("starts an amendment as a draft and says the original stays active until it is finalized", async () => {
    const amended = draftOf({ id: "d9", supersedesPrescriptionId: "rx1" });
    const s = stub({ records: [recordOf()], detail: { draft: amended, safety: safetyCheck() } });
    const user = userEvent.setup();
    await open();
    await user.click(screen.getByRole("button", { name: "Amend Amoxicillin" }));
    expect(await screen.findByText(/An amendment was started as a draft. The original stays active until you finalize it./)).toBeInTheDocument();
    expect(await screen.findByRole("heading", { name: "Amend a prescription (draft)" })).toBeInTheDocument();
    expect(s.posts("/amend")).toHaveLength(1);
  });
});

describe("printing", () => {
  it("shows the prescription as it prints, with the document reference and what is not checked, and opens the print dialog", async () => {
    stub({ records: [recordOf()] });
    const print = vi.spyOn(window, "print").mockImplementation(() => {});
    const user = userEvent.setup();
    await open();
    await user.click(screen.getByRole("button", { name: "Print Amoxicillin" }));
    const article = await screen.findByRole("article", { name: "Prescription" });
    for (const text of ["Alveara Dental", /Ann Alder/, "500 mg, three times daily", "Take one capsule with food.", "21 capsule", /Document reference abcdef012345/, /Not checked:/]) expect(within(article).getAllByText(text).length).toBeGreaterThan(0);
    await user.click(screen.getByRole("button", { name: "Print" }));
    expect(print).toHaveBeenCalledTimes(1);
  });

  it("says so on the printed page when the prescription is cancelled", async () => {
    stub({ records: [recordOf({ status: "Cancelled" })], print: () => json(printOf({ status: "Cancelled" })) });
    const user = userEvent.setup();
    await open();
    await user.click(screen.getByRole("button", { name: "Print Amoxicillin" }));
    const article = await screen.findByRole("article", { name: "Prescription" });
    expect(within(article).getByText(/Cancelled: it should not be taken or dispensed/)).toBeInTheDocument();                  // it says so on the page itself
  });

  it("says nothing was printed when the print cannot be prepared", async () => {
    stub({ records: [recordOf()], print: () => json({ error: "print_failed", message: "The prescription could not be prepared for printing, because the print could not be logged. Nothing was printed. Try again." }, 503) });
    const user = userEvent.setup();
    await open();
    await user.click(screen.getByRole("button", { name: "Print Amoxicillin" }));
    expect(await screen.findByText(/could not be prepared for printing/)).toBeInTheDocument();
    expect(screen.queryByRole("article", { name: "Prescription" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Print" })).toBeDisabled();
  });

  it("says nothing was printed when the browser cannot open the print dialog", async () => {
    stub({ records: [recordOf()] });
    vi.spyOn(window, "print").mockImplementation(() => { throw new Error("blocked"); });
    const user = userEvent.setup();
    await open();
    await user.click(screen.getByRole("button", { name: "Print Amoxicillin" }));
    await screen.findByRole("article", { name: "Prescription" });
    await user.click(screen.getByRole("button", { name: "Print" }));
    expect(await screen.findByText("The print dialog could not be opened. Nothing was printed.")).toBeInTheDocument();
  });
});

describe("accessibility", () => {
  it("has no violations in the composer with problems showing, the confirmation, the record list and the print preview", async () => {
    stub({ records: [recordOf(), recordOf({ id: "b", medication: "Naproxen", status: "Cancelled" })],
      create: () => json({ error: "validation_failed", message: "x", problems: [{ field: "doseValue", code: "above_typing_ceiling", message: "The dose is above what this screen accepts." }] }, 400) });
    const user = userEvent.setup();
    const view = await open();
    expect(await axe(view.container)).toHaveNoViolations();                                                // the list
    await newDraft(user);
    await fillCore(user);
    await user.click(composer().getByRole("button", { name: "Save draft" }));
    await screen.findByText("Nothing was saved. Correct the entries marked below.");
    expect(await axe(view.container)).toHaveNoViolations();                                                // the composer with its problems
    await user.click(composer().getByRole("button", { name: "Close" }));
    await user.click(screen.getByRole("button", { name: "Print Amoxicillin" }));
    await screen.findByRole("article", { name: "Prescription" });
    expect(await axe(view.container)).toHaveNoViolations();                                                // the print preview
  });

  it("has no violations on the finalization confirmation", async () => {
    stub();
    const user = userEvent.setup();
    const view = await open();
    await newDraft(user);
    await fillComplete(user);
    await user.click(composer().getByRole("button", { name: "Finalize…" }));
    await screen.findByRole("region", { name: "Confirm finalization" });
    expect(await axe(view.container)).toHaveNoViolations();
  });
});
