import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import type { PatientDetail } from "../../services/patientsApi";
import type { AllergyAssessment, Prescription, PrescriptionOptions, PrescriptionPreview } from "../../services/prescriptionApi";
import type { SafetyContext, SafetyEntry } from "../../services/safetyApi";
import { parseDose } from "./PrescriptionForm";
import { PrescriptionsPanel } from "./PrescriptionsPanel";

/**
 * STORY-016: the prescriptions screen against a scripted API. The rules are proven by the backend tests and the real-backend walkthrough; what is proven here is the screen: who gets the form, that the safety
 * panel says what is recorded, what the allergy check said and what is NOT checked, that an allergy match or an unestablished list asks for the prescriber's own words and saves nothing until they are given,
 * that every wrong entry is named beside its box with what was typed kept, that a retry reuses its save key, and that a failed load says it could not load.
 */
const patient = { id: "p1" } as PatientDetail;
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

const options: PrescriptionOptions = {
  doseUnits: ["mg", "mcg", "g", "mL", "tablet"], typingCeilings: { mg: 5000, mcg: 5000, g: 10, mL: 100, tablet: 10 }, frequencies: ["Once daily", "Twice daily", "Three times daily"],
  doseDecimals: 3, medicationMax: 200, reviewNoteMax: 500,
  doseLimitation: "The dose is checked for its form only. It is not checked against any clinical dose range: this application has no drug-knowledge source.",
  allergyLimitation: "This check compares the medication's name with the allergies recorded for this patient. It does not know drug families, so no recorded match is not a clearance.",
};
const entry = (title: string, category: "Allergy" | "Medication", severity: string | null = null, detail: string | null = null): SafetyEntry => ({
  origin: "ClinicalRecord", id: `${category}-${title}`, category, title, detail, severity: severity as SafetyEntry["severity"], status: "Active", source: "Clinical record", sourceItemId: null, lastUpdatedAtUtc: "2030-01-01T10:00:00Z",
  lastUpdatedByName: "Dr. Okafor", needsAttention: false, attentionReason: null, acknowledgedByMe: false, acknowledgedAtUtc: null, revision: null, rowVersion: null, resolvedAtUtc: null, resolvedByName: null, resolutionReason: null,
});
const safetyOf = (entries: SafetyEntry[], gaps: SafetyContext["gaps"] = []): SafetyContext => ({
  patientId: "p1", asOfUtc: "2030-01-01T10:00:00Z", entries, resolved: [], clearances: [], gaps,
  summary: { patientId: "p1", asOfUtc: "2030-01-01T10:00:00Z", activeAlertCount: 0, activeAllergyCount: 0, currentMedicationCount: 0, highestSeverity: null, openClearanceCount: 0, unacknowledgedAlertCount: 0, needsAttentionCount: 0, gaps },
});
const assessment = (outcome: AllergyAssessment["outcome"], over: Partial<AllergyAssessment> = {}): AllergyAssessment => ({
  outcome, matches: outcome === "RecordedAllergyMatch" ? [{ name: "Penicillin", severity: "Severe", reaction: "Hives" }] : [], listState: outcome === "NotEstablished" ? "NotEstablished" : "Established",
  message: outcome === "RecordedAllergyMatch" ? "This medication matches an allergy recorded for this patient: Penicillin (high), reaction: Hives." : outcome === "NotEstablished" ? "This patient's allergies have not been reviewed, so the allergy check cannot say anything." : "No allergy recorded for this patient matches this medication's name. This is a name comparison only, not a clearance.",
  limitation: options.allergyLimitation, ...over,
});
const preview = (a: AllergyAssessment | null, problems: PrescriptionPreview["problems"] = []): PrescriptionPreview => ({
  problems, assessment: a, reviewRequired: !!a && a.outcome !== "NoRecordedMatch", reviewProblem: null, doseLimitation: options.doseLimitation, allergyLimitation: options.allergyLimitation,
});
const saved = (over: Partial<Prescription> = {}): Prescription => ({
  id: "rx1", patientId: "p1", medication: "Ibuprofen", doseValue: 400, doseUnit: "mg", frequency: "Three times daily", allergyOutcome: "NoRecordedMatch", allergyListState: "Established", allergyMatches: null, reviewNote: null,
  prescribedByName: "Dr. Okafor", prescribedAtUtc: "2030-02-01T10:00:00Z", ...over,
});

interface Calls { url: string; method: string; body: Record<string, unknown> | null }
interface Script { list: Prescription[]; safety: SafetyContext | "fail"; check: (b: Record<string, unknown>) => Response; save: (b: Record<string, unknown>) => Response; listFails: boolean }

function stub(over: Partial<Script> = {}) {
  const script: Script = {
    list: [], safety: safetyOf([entry("Latex", "Allergy", "Low", "Rash"), entry("Lisinopril", "Medication", null, "10 mg · Once daily")]),
    check: () => json(preview(assessment("NoRecordedMatch"))), save: () => json({ prescription: saved(), replayed: false }), listFails: false, ...over,
  };
  const calls: Calls[] = [];
  vi.stubGlobal("fetch", vi.fn().mockImplementation((url: RequestInfo | URL, init?: RequestInit) => {
    const u = String(url);
    const method = init?.method ?? "GET";
    const body = init?.body ? (JSON.parse(init.body as string) as Record<string, unknown>) : null;
    calls.push({ url: u, method, body });
    if (u.includes("/api/auth/csrf-token")) return Promise.resolve(json({ token: "csrf" }));
    if (u.includes("/api/prescriptions/options")) return Promise.resolve(json(options));
    if (u.endsWith("/prescriptions/check")) return Promise.resolve(script.check(body!));
    if (u.endsWith("/prescriptions") && method === "POST") return Promise.resolve(script.save(body!));
    if (u.endsWith("/prescriptions")) return Promise.resolve(script.listFails ? json({ error: "server_error", message: "boom" }, 500) : json({ patientId: "p1", prescriptions: script.list }));
    if (u.endsWith("/safety")) return Promise.resolve(script.safety === "fail" ? json({ error: "server_error", message: "boom" }, 500) : json(script.safety));
    return Promise.resolve(json({}));
  }));
  return { script, calls, posts: () => calls.filter((c) => c.method === "POST" && !c.url.includes("csrf")), saves: () => calls.filter((c) => c.method === "POST" && c.url.endsWith("/prescriptions")) };
}

async function open(canPrescribe = true) {
  const view = render(<PrescriptionsPanel patient={patient} canPrescribe={canPrescribe} />);
  await screen.findByRole("heading", { name: "Prescriptions" });
  return view;
}

async function fill(user: ReturnType<typeof userEvent.setup>, f: { medication?: string; dose?: string; unit?: string; frequency?: string } = {}) {
  const form = within(screen.getByRole("form", { name: "Prescribe a medication" }));
  await user.type(form.getByLabelText("Medication"), f.medication ?? "Ibuprofen");
  await user.type(form.getByLabelText("Dose"), f.dose ?? "400");
  await user.selectOptions(form.getByLabelText("Unit"), f.unit ?? "mg");
  await user.selectOptions(form.getByLabelText("How often"), f.frequency ?? "Three times daily");
  return form;
}

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("who sees what", () => {
  it("shows a prescriber the form beside the safety panel and the list, with the limitation said in words", async () => {
    stub({ list: [saved()] });
    await open(true);
    expect(screen.getByRole("form", { name: "Prescribe a medication" })).toBeInTheDocument();
    const panel = within(screen.getByRole("complementary", { name: "Patient safety" }));
    expect(await panel.findByText(/Latex \(low\) Rash/)).toBeInTheDocument();
    expect(panel.getByText(/Lisinopril/)).toBeInTheDocument();
    expect(panel.getByText(/drug interactions, and whether a dose is clinically appropriate/)).toBeInTheDocument();
    expect(panel.getByText(/does not know drug families/)).toBeInTheDocument();
    expect(panel.getByText(/not checked against any clinical dose range/)).toBeInTheDocument();
    expect(screen.getByRole("list", { name: "Prescriptions" })).toHaveTextContent("Ibuprofen");
  });

  it("shows a reader the safety panel and the list and no form, and says the role cannot prescribe", async () => {
    stub({ list: [saved()] });
    await open(false);
    expect(screen.queryByRole("form", { name: "Prescribe a medication" })).not.toBeInTheDocument();
    expect(screen.getByText("You can read this but your role cannot prescribe.")).toBeInTheDocument();
    expect(screen.getByRole("complementary", { name: "Patient safety" })).toBeInTheDocument();
    expect(screen.getByRole("list", { name: "Prescriptions" })).toBeInTheDocument();
  });

  it("offers no way to change or delete a prescription", async () => {
    stub({ list: [saved()] });
    await open(true);
    expect(screen.queryByRole("button", { name: /edit|delete|remove|change|discontinue/i })).not.toBeInTheDocument();
  });

  it("lists each prescription with who prescribed it and when, and its allergy result in words that are never a clearance", async () => {
    stub({ list: [saved({ allergyOutcome: "RecordedAllergyMatch", allergyMatches: "Penicillin", reviewNote: "Childhood rash; tolerated since.", medication: "Penicillin V" }), saved({ id: "rx2" })] });
    await open(false);
    const items = within(screen.getByRole("list", { name: "Prescriptions" })).getAllByRole("listitem");
    expect(items[0]).toHaveTextContent("Prescribed by Dr. Okafor");
    expect(items[0]).toHaveTextContent("matched a recorded allergy (Penicillin); the prescriber reviewed it");
    expect(items[0]).toHaveTextContent("Prescriber's review: Childhood rash; tolerated since.");
    expect(items[1]).toHaveTextContent("a name comparison, not a clearance");
  });

  it("says an empty list means none recorded here, not that none was given", async () => {
    stub();
    await open(false);
    expect(screen.getByText(/means none has been recorded here, not that none was ever given/)).toBeInTheDocument();
  });
});

describe("loading", () => {
  it("says it could not load rather than showing an empty list", async () => {
    stub({ listFails: true });
    render(<PrescriptionsPanel patient={patient} canPrescribe />);
    expect(await screen.findByText("Could not load the prescriptions")).toBeInTheDocument();
    expect(screen.queryByText(/has been recorded here/)).not.toBeInTheDocument();
  });

  it("says the safety context could not be read and never draws that as no allergies", async () => {
    stub({ safety: "fail" });
    await open(true);
    const panel = within(screen.getByRole("complementary", { name: "Patient safety" }));
    expect(await panel.findByText(/could not be loaded, so they are not shown here/)).toBeInTheDocument();
    expect(panel.queryByText(/No allergy is recorded/)).not.toBeInTheDocument();
  });

  it("names what is not established in words and never draws a missing list as none", async () => {
    stub({ safety: safetyOf([], [{ section: "Allergy", status: "NotReviewed", message: "Allergies have not been reviewed. Nothing listed here does not mean none." }]) });
    await open(true);
    const panel = within(screen.getByRole("complementary", { name: "Patient safety" }));
    expect(await panel.findByText("Allergies have not been reviewed. Nothing listed here does not mean none.")).toBeInTheDocument();
  });
});

describe("the allergy check", () => {
  it("compares the medication as soon as it is typed and shows what the check said in the safety panel", async () => {
    const api = stub({ check: () => json(preview(assessment("RecordedAllergyMatch"))) });
    const user = userEvent.setup();
    await open(true);
    const form = within(screen.getByRole("form", { name: "Prescribe a medication" }));
    await user.type(form.getByLabelText("Medication"), "Penicillin V");
    await user.tab();
    expect(await screen.findByText(/matches an allergy recorded for this patient: Penicillin \(high\)/)).toBeInTheDocument();
    expect(api.calls.some((c) => c.url.endsWith("/prescriptions/check"))).toBe(true);
    expect(await screen.findByLabelText(/Why are you prescribing this despite the matching allergy/)).toBeInTheDocument();      // the review box appears with its reason
  });

  it("asks for the prescriber's own words when the allergies are not established, and says so", async () => {
    stub({ check: () => json(preview(assessment("NotEstablished"))) });
    const user = userEvent.setup();
    await open(true);
    await user.type(within(screen.getByRole("form", { name: "Prescribe a medication" })).getByLabelText("Medication"), "Ibuprofen");
    await user.tab();
    expect(await screen.findByLabelText(/allergies are not established\. Confirm in your own words/)).toBeInTheDocument();
  });

  it("shows no review box when there is no recorded match, and says that is not a clearance", async () => {
    stub();
    const user = userEvent.setup();
    await open(true);
    await user.type(within(screen.getByRole("form", { name: "Prescribe a medication" })).getByLabelText("Medication"), "Ibuprofen");
    await user.tab();
    expect(await screen.findByText(/a name comparison only, not a clearance/)).toBeInTheDocument();
    expect(screen.queryByLabelText(/Why are you prescribing|Confirm in your own words/)).not.toBeInTheDocument();
  });
});

describe("saving", () => {
  it("sends the entry with a save key, then clears the form, says it was prescribed and logged, and reloads the list", async () => {
    const api = stub();
    const user = userEvent.setup();
    await open(true);
    const form = await fill(user);
    await user.click(form.getByRole("button", { name: "Save prescription" }));
    expect(await screen.findByText("Ibuprofen prescribed and logged.")).toBeInTheDocument();
    const body = api.saves()[0].body!;
    expect(body).toMatchObject({ medication: "Ibuprofen", doseValue: 400, doseUnit: "mg", frequency: "Three times daily", reviewNote: null });
    expect(typeof body.idempotencyKey).toBe("string");
    expect(form.getByLabelText("Medication")).toHaveValue("");
    expect(api.calls.filter((c) => c.method === "GET" && c.url.endsWith("/prescriptions")).length).toBeGreaterThanOrEqual(2);
  });

  it("names every wrong entry beside its box and keeps what was typed", async () => {
    stub({
      save: () => json({
        error: "validation_failed", message: "The prescription has entries that need correcting. Nothing was saved.",
        problems: [{ field: "doseValue", code: "above_typing_ceiling", message: "A single dose of 9000 mg is above the 5000 mg this screen accepts. This ceiling only catches typing mistakes; it is not a clinical dose limit." }, { field: "frequency", code: "required", message: "Choose how often." }],
      }, 400),
    });
    const user = userEvent.setup();
    await open(true);
    const form = await fill(user, { dose: "9000" });
    await user.click(form.getByRole("button", { name: "Save prescription" }));
    expect(await screen.findByText("Nothing was saved. Correct the entries marked below.")).toBeInTheDocument();
    expect(form.getAllByText(/is above the 5000 mg this screen accepts/).length).toBeGreaterThan(0);
    expect(form.getByLabelText("Dose")).toHaveAttribute("aria-invalid", "true");
    expect(form.getByLabelText("Dose")).toHaveValue("9000");                                    // what was typed is still there
    expect(form.getByLabelText("Medication")).toHaveValue("Ibuprofen");
  });

  it("sends nothing for a dose that is not a number and says what to enter", async () => {
    const api = stub();
    const user = userEvent.setup();
    await open(true);
    const form = await fill(user, { dose: "four hundred" });
    await user.click(form.getByRole("button", { name: "Save prescription" }));
    expect((await screen.findAllByText("Enter the dose as a number, for example 500 or 2.5.")).length).toBeGreaterThan(0);
    expect(api.saves()).toHaveLength(0);
  });

  it("saves nothing when an allergy matches until the prescriber gives their own words, then sends them with the same entry", async () => {
    let first = true;
    const api = stub({
      save: (b) => {
        if (first || !b.reviewNote) { first = false; return json({ error: "allergy_review_required", message: "This medication matches an allergy recorded for this patient. Nothing was saved. Say why you are prescribing it, or change the medication.", problems: [{ field: "reviewNote", code: "allergy_review_required", message: "This medication matches a recorded allergy. Say why you are prescribing it anyway, or change the medication." }], assessment: assessment("RecordedAllergyMatch") }, 409); }
        return json({ prescription: saved({ medication: "Penicillin V", allergyOutcome: "RecordedAllergyMatch", allergyMatches: "Penicillin", reviewNote: String(b.reviewNote) }), replayed: false });
      },
      check: () => json(preview(null)),
    });
    const user = userEvent.setup();
    await open(true);
    const form = await fill(user, { medication: "Penicillin V", dose: "500" });
    await user.click(form.getByRole("button", { name: "Save prescription" }));

    expect(await screen.findByText(/This medication matches an allergy recorded for this patient\. Nothing was saved\./)).toBeInTheDocument();
    const note = await screen.findByLabelText(/Why are you prescribing this despite the matching allergy/);
    await waitFor(() => expect(note).toHaveFocus());                                               // the cursor goes to the box that needs the prescriber's words
    expect(screen.getByText(/matches an allergy recorded for this patient: Penicillin \(high\)/)).toBeInTheDocument();

    await user.type(note, "Childhood rash, tolerated since.");
    await user.click(form.getByRole("button", { name: "Save prescription" }));
    expect(await screen.findByText("Penicillin V prescribed and logged.")).toBeInTheDocument();
    expect(api.saves()[1].body).toMatchObject({ medication: "Penicillin V", reviewNote: "Childhood rash, tolerated since." });
  });

  it("reuses the save key when the same entry is sent again after a dropped connection, and gives a changed entry a new one", async () => {
    let calls = 0;
    const api = stub({ save: () => { calls += 1; if (calls === 1) throw new TypeError("Failed to fetch"); return json({ prescription: saved(), replayed: calls === 2 }); } });
    const user = userEvent.setup();
    await open(true);
    const form = await fill(user);
    await user.click(form.getByRole("button", { name: "Save prescription" }));
    expect(await screen.findByText(/Nothing was saved: the connection dropped\. What you entered is still here/)).toBeInTheDocument();
    expect(form.getByLabelText("Medication")).toHaveValue("Ibuprofen");
    await user.click(form.getByRole("button", { name: "Save prescription" }));
    expect(await screen.findByText("This prescription was already saved; nothing new was recorded.")).toBeInTheDocument();
    expect(api.saves()).toHaveLength(2);
    expect(api.saves()[1].body?.idempotencyKey).toBe(api.saves()[0].body?.idempotencyKey);
  });

  it("gives a changed entry a new save key", async () => {
    let calls = 0;
    const api = stub({ save: () => { calls += 1; return calls === 1 ? json({ error: "save_failed", message: "The prescription could not be saved, so nothing was recorded. Try again; if it keeps failing, contact support." }, 503) : json({ prescription: saved(), replayed: false }); } });
    const user = userEvent.setup();
    await open(true);
    const form = await fill(user);
    await user.click(form.getByRole("button", { name: "Save prescription" }));
    expect(await screen.findByText(/Nothing was saved: The prescription could not be saved/)).toBeInTheDocument();
    await user.clear(form.getByLabelText("Dose"));
    await user.type(form.getByLabelText("Dose"), "200");
    await user.click(form.getByRole("button", { name: "Save prescription" }));
    await screen.findByText("Ibuprofen prescribed and logged.");
    expect(api.saves()[1].body?.idempotencyKey).not.toBe(api.saves()[0].body?.idempotencyKey);
  });

  it("says nothing was saved when the allergy check could not be made, and keeps the entry", async () => {
    stub({ save: () => json({ error: "allergy_check_unavailable", message: "The allergy check could not be completed, so nothing was saved. Try again; if it keeps failing, contact support." }, 503) });
    const user = userEvent.setup();
    await open(true);
    const form = await fill(user);
    await user.click(form.getByRole("button", { name: "Save prescription" }));
    expect(await screen.findByText(/Nothing was saved: The allergy check could not be completed/)).toBeInTheDocument();
    expect(form.getByLabelText("Medication")).toHaveValue("Ibuprofen");
  });

  it("checks an entry without saving it and says so", async () => {
    const api = stub({ check: () => json(preview(assessment("NoRecordedMatch"), [{ field: "frequency", code: "required", message: "Choose how often." }])) });
    const user = userEvent.setup();
    await open(true);
    const form = within(screen.getByRole("form", { name: "Prescribe a medication" }));
    await user.type(form.getByLabelText("Medication"), "Ibuprofen");
    await user.click(form.getByRole("button", { name: "Check entry" }));
    expect(await screen.findByText("Nothing has been saved. Correct the entries marked below.")).toBeInTheDocument();
    expect(form.getAllByText("Choose how often.").length).toBeGreaterThan(0);
    expect(api.saves()).toHaveLength(0);
  });
});

describe("the dose box", () => {
  it.each([["", null, false], ["  ", null, false], ["400", 400, false], ["2.5", 2.5, false], [" 12.345 ", 12.345, false], ["1e3", null, true], ["-5", null, true], ["5 mg", null, true], ["1,5", null, true], [".5", null, true]])(
    "reads %j as %j (problem: %s)", (text, value, problem) => {
      const r = parseDose(text);
      expect(r.value).toBe(value);
      expect(r.problem !== null).toBe(problem);
    });
});

describe("accessibility", () => {
  it("has no accessibility violations with the form, the safety panel and the list on screen", async () => {
    stub({ list: [saved()] });
    const view = await open(true);
    await screen.findByText(/Latex \(low\) Rash/);
    expect(await axe(view.container)).toHaveNoViolations();
  });

  it("has none with the problems, the review box and the allergy result showing", async () => {
    stub({
      check: () => json(preview(assessment("RecordedAllergyMatch"))),
      save: () => json({ error: "allergy_review_required", message: "Nothing was saved. Say why.", problems: [{ field: "reviewNote", code: "allergy_review_required", message: "Say why you are prescribing it anyway." }], assessment: assessment("RecordedAllergyMatch") }, 409),
    });
    const user = userEvent.setup();
    const view = await open(true);
    const form = await fill(user, { medication: "Penicillin V" });
    await user.click(form.getByRole("button", { name: "Save prescription" }));
    await screen.findByLabelText(/Why are you prescribing this despite the matching allergy/);
    expect(await axe(view.container)).toHaveNoViolations();
  });
});
