import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { App } from "./App";
import { AUTOSAVE_MS } from "./pages/clinical/notes/NoteEditor";
import { FakeClinicalServer } from "./test/fakeClinicalServer";
import { json, makePatient } from "./test/fakePatientServer";

/**
 * ALV-005-C01: an encounter's notes (SOAP, progress, treatment), the template that shapes them, vital signs, signing and the timeline of amendments, exercised through the
 * real <App /> against an in-memory fake of the API. The rules are proven by the backend tests and the real-backend walkthrough; here the point is the UI's behaviour for each
 * outcome: notes that save themselves and never lose typed text (a refused save, a dropped connection, a stale tab), a signed note that is locked, required notes that block
 * signing and say which, vitals that cannot be recorded twice, and an amendment that leaves the original untouched.
 */
const A = "aaaaaaaa-0000-0000-0000-000000000001";
const READ_ONLY = ["ViewPatientRecords", "ViewClinicalDocumentation"];
const FOUR = [{ kind: "MedicalHistory", name: "Asthma" }, { kind: "DentalHistory", name: "Crown" }, { kind: "Allergy", name: "Latex" }, { kind: "Medication", name: "Albuterol" }];
let server: FakeClinicalServer;

function open(path: string) {
  window.history.pushState({}, "", path);
  return render(<App />);
}
async function openEncounter(id: string) {
  const view = open(`/patients/${A}/clinical/${id}`);
  await screen.findByRole("heading", { name: /^Encounter on/ });
  return view;
}
const note = (label: string | RegExp) => screen.getByRole("textbox", { name: label });
const status = () => screen.getAllByRole("status").find((el) => el.className.includes("alv-clinical__status"))!;

beforeEach(() => {
  server = new FakeClinicalServer();
  server.add(makePatient({ id: A, firstName: "Ann", lastName: "Lee" }));
  server.install();
});
afterEach(() => vi.unstubAllGlobals());

// ---------- templates and notes ----------

describe("notes and templates", () => {
  it("starts from a template: its sections appear with their starter text and the required ones are marked", async () => {
    const user = userEvent.setup();
    server.record.addTemplate({ name: "SOAP note" });
    server.addEncounter(A, { id: "e1", entries: FOUR });
    await openEncounter("e1");

    await user.selectOptions(await screen.findByLabelText("Start from a template"), "SOAP note");
    await user.click(screen.getByRole("button", { name: "Use template" }));

    expect(await screen.findByText("Template: SOAP note")).toBeInTheDocument();
    expect(note("Subjective (required by the template)")).toHaveValue("Chief complaint:");
    expect(note("Plan (required by the template)")).toHaveValue("");
    expect(screen.getByText("Starter text only - write the note.")).toBeInTheDocument();
    expect(screen.queryByLabelText("Start from a template")).not.toBeInTheDocument(); // a note uses one template
    expect(screen.getByText("Template applied.")).toBeInTheDocument();
  });

  it("saves a note when the box loses focus, says so, and attributes it", async () => {
    const user = userEvent.setup();
    server.addEncounter(A, { id: "e1" });
    server.setNote("e1", "Subjective", "");
    await openEncounter("e1");

    await user.type(note("Subjective"), "Pain on the lower left.");
    expect(screen.getByText("Unsaved changes - saving when you pause.")).toBeInTheDocument();
    await user.tab();

    await waitFor(() => expect(screen.getByText("Subjective saved.")).toBeInTheDocument());
    expect(server.encounters.get("e1")!.notes.get("Subjective")!.body).toBe("Pain on the lower left.");
    expect(screen.getByText("Saved. Last saved by Dr. Okafor.")).toBeInTheDocument();
    expect(server.callsToClinical("PUT", "/notes/Subjective")[0].body).toMatchObject({ body: "Pain on the lower left.", rowVersion: "e1" });
  });

  it("saves by itself when typing pauses", async () => {
    const user = userEvent.setup();
    server.addEncounter(A, { id: "e1" });
    server.setNote("e1", "Plan", "");
    await openEncounter("e1");
    await user.type(note("Plan"), "Recall in six months.");
    expect(server.callsToClinical("PUT", "/notes/Plan")).toHaveLength(0); // not on every key
    await waitFor(() => expect(server.callsToClinical("PUT", "/notes/Plan")).toHaveLength(1), { timeout: AUTOSAVE_MS + 3000 });
    expect(server.encounters.get("e1")!.notes.get("Plan")!.body).toBe("Recall in six months.");
  });

  it("adds any other note section: a progress note and a treatment note", async () => {
    const user = userEvent.setup();
    server.addEncounter(A, { id: "e1" });
    await openEncounter("e1");
    expect(screen.getByText("No notes yet. Start from a template or add a note section.")).toBeInTheDocument();
    await user.selectOptions(screen.getByLabelText("Add a note section"), "Treatment note");
    await user.click(screen.getByRole("button", { name: "Add section" }));
    await user.type(note("Treatment note"), "Access cavity prepared.");
    await user.tab();
    await waitFor(() => expect(server.encounters.get("e1")!.notes.get("Treatment")?.body).toBe("Access cavity prepared."));
    expect(within(screen.getByLabelText("Add a note section")).queryByRole("option", { name: "Treatment note" })).not.toBeInTheDocument(); // already there
  });

  it("keeps the typed text and says so when a save is refused, and does not retry on its own", async () => {
    const user = userEvent.setup();
    server.addEncounter(A, { id: "e1" });
    server.setNote("e1", "Subjective", "");
    await openEncounter("e1");
    server.failClinical("PUT /api/encounters/e1/notes/Subjective", json(500, { error: "server_error", message: "The note could not be saved." }));
    await user.type(note("Subjective"), "Keep me");
    await user.tab();

    expect(await screen.findByText("Not saved: The note could not be saved.")).toBeInTheDocument();
    expect(note("Subjective")).toHaveValue("Keep me");
    expect(screen.getByText("Not saved - your text is still here.")).toBeInTheDocument();
    await new Promise((r) => setTimeout(r, AUTOSAVE_MS + 400));
    expect(server.callsToClinical("PUT", "/notes/Subjective")).toHaveLength(1); // no retry loop

    await user.click(screen.getByRole("button", { name: "Save subjective now" }));
    await waitFor(() => expect(server.encounters.get("e1")!.notes.get("Subjective")!.body).toBe("Keep me"));
    expect(await screen.findByText("Saved. Last saved by Dr. Okafor.")).toBeInTheDocument();
  });

  it("after a dropped connection keeps the text, and saving again is recognised as the same note", async () => {
    const user = userEvent.setup();
    server.addEncounter(A, { id: "e1" });
    server.setNote("e1", "Plan", "");
    await openEncounter("e1");
    server.dropNextResponse("PUT /notes/Plan");
    await user.type(note("Plan"), "Extract 38");
    await user.tab();

    expect(await screen.findByText(/Not saved: the connection dropped/)).toBeInTheDocument();
    expect(note("Plan")).toHaveValue("Extract 38");
    expect(server.encounters.get("e1")!.notes.get("Plan")!.body).toBe("Extract 38"); // stored; only the answer was lost
    const versionAfterDrop = server.encounters.get("e1")!.v;

    await user.click(screen.getByRole("button", { name: "Save plan now" }));
    expect(await screen.findByText("Saved. Last saved by Dr. Okafor.")).toBeInTheDocument();
    expect(server.encounters.get("e1")!.v).toBe(versionAfterDrop); // nothing was saved twice
  });

  it("shows the conflict banner for a stale tab and reloads in place so the typed note survives", async () => {
    const user = userEvent.setup();
    server.addEncounter(A, { id: "e1" });
    server.setNote("e1", "Plan", "Original");
    await openEncounter("e1");
    server.touch("e1"); // another clinician changed the encounter
    const box = note("Plan");
    await user.clear(box);
    await user.type(box, "My version");
    await user.tab();

    expect(await screen.findByText("Someone else changed this while you were editing")).toBeInTheDocument();
    expect(status()).toHaveTextContent("Not saved: someone else changed this note.");
    await user.click(screen.getByRole("button", { name: /Reload/ }));
    await waitFor(() => expect(screen.queryByText("Someone else changed this while you were editing")).not.toBeInTheDocument());
    expect(note("Plan")).toHaveValue("My version"); // the box stayed mounted and kept its text
    await user.click(screen.getByRole("button", { name: "Save plan now" }));
    await waitFor(() => expect(server.encounters.get("e1")!.notes.get("Plan")!.body).toBe("My version"));
  });

  it("says why a template cannot be used and keeps the picker", async () => {
    const user = userEvent.setup();
    server.record.addTemplate({ name: "SOAP note" });
    server.addEncounter(A, { id: "e1" });
    await openEncounter("e1");
    server.failClinical("POST /api/encounters/e1/template", json(409, { error: "template_inactive", message: "That template is no longer in use.", fieldErrors: {} }));
    await user.selectOptions(await screen.findByLabelText("Start from a template"), "SOAP note");
    await user.click(screen.getByRole("button", { name: "Use template" }));
    expect(await screen.findByText("Not saved: That template is no longer in use.")).toBeInTheDocument();
    expect(screen.getByLabelText("Start from a template")).toBeInTheDocument();
    expect(server.encounters.get("e1")!.templateId).toBeNull();
  });
});

// ---------- signing ----------

describe("signing", () => {
  async function templated(id = "e1") {
    server.record.addTemplate({ name: "SOAP note" });
    server.addEncounter(A, { id, entries: FOUR });
    await openEncounter(id);
    const user = userEvent.setup();
    await user.selectOptions(await screen.findByLabelText("Start from a template"), "SOAP note");
    await user.click(screen.getByRole("button", { name: "Use template" }));
    await screen.findByText("Template: SOAP note");
    return user;
  }

  it("names the required notes that are unwritten, and will not sign or finalize until they are written", async () => {
    const user = await templated();
    await user.click(screen.getByRole("button", { name: "Review and finalize" }));
    const review = screen.getByRole("region", { name: "Review before finalizing" });
    expect(within(review).getAllByText(/Needs attention - write this note/)).toHaveLength(2); // Subjective (starter only) and Plan (empty)
    expect(within(review).getByRole("button", { name: "Sign note" })).toBeDisabled();
    expect(within(review).getByRole("button", { name: "Finalize encounter" })).toBeDisabled();

    await user.click(screen.getByRole("button", { name: "Keep editing" }));
    await user.clear(note("Subjective (required by the template)"));
    await user.type(note("Subjective (required by the template)"), "Chief complaint: pain");
    await user.type(note("Plan (required by the template)"), "Endodontic treatment");
    await user.tab();
    await waitFor(() => expect(server.encounters.get("e1")!.notes.get("Plan")!.body).toBe("Endodontic treatment"));
    await user.click(screen.getByRole("button", { name: "Review and finalize" }));
    expect(screen.queryByText(/Needs attention/)).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Sign note" })).toBeEnabled();
  });

  /** A templated encounter whose required notes are already written, so it can be signed. */
  async function readyToSign() {
    const user = await templated();
    await user.clear(note("Subjective (required by the template)"));
    await user.type(note("Subjective (required by the template)"), "Chief complaint: pain");
    await user.type(note("Plan (required by the template)"), "Endodontic treatment");
    await user.tab();
    await waitFor(() => expect(server.encounters.get("e1")!.notes.get("Plan")!.body).toBe("Endodontic treatment"));
    await waitFor(() => expect(status()).toHaveTextContent("Plan saved."));
    return user;
  }

  it("signs a complete note and locks it: no note can be typed, no entry added, and only unsign or finalize is offered", async () => {
    const user = await readyToSign();
    await user.click(screen.getByRole("button", { name: "Review and finalize" }));
    await user.click(screen.getByRole("button", { name: "Sign note" }));

    expect(await screen.findByText("Signed - awaiting finalize")).toBeInTheDocument();
    expect(server.encounters.get("e1")!.signed).toBe(true);
    expect(screen.getByText(/Signed by Dr\. Okafor on .*The note is locked/)).toBeInTheDocument();
    expect(screen.queryByRole("textbox", { name: /Subjective|Plan/ })).not.toBeInTheDocument(); // the notes are read-only text now
    expect(screen.getByText("Endodontic treatment")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^Add (medical|dental|allergy|medication)|Change|Remove|Reviewed - none reported|Record vital signs/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Sign note" })).not.toBeInTheDocument(); // already signed
    expect(screen.getByRole("button", { name: "Unsign to keep editing" })).toBeInTheDocument();
    expect(status()).toHaveTextContent("Encounter signed.");

    await user.click(screen.getByRole("button", { name: "Finalize encounter" })); // finalizing needs no second signature
    expect(await screen.findByText("Finalized")).toBeInTheDocument();
    expect(server.encounters.get("e1")!.status).toBe("Finalized");
  });

  it("unsigns to keep editing: the notes are boxes again and the signature is gone", async () => {
    const user = await readyToSign();
    await user.click(screen.getByRole("button", { name: "Review and finalize" }));
    await user.click(screen.getByRole("button", { name: "Sign note" }));
    await user.click(await screen.findByRole("button", { name: "Unsign to keep editing" }));

    expect(await screen.findByText("Draft - not finalized")).toBeInTheDocument();
    expect(note("Plan (required by the template)")).toHaveValue("Endodontic treatment");
    expect(server.encounters.get("e1")!.signed).toBe(false);
    expect(status()).toHaveTextContent("Signature withdrawn.");
  });

  it("shows the server's refusal when a required note was cleared elsewhere, and nothing is signed", async () => {
    const user = await readyToSign();
    await user.click(screen.getByRole("button", { name: "Review and finalize" }));
    server.failClinical("POST /api/encounters/e1/sign", json(409, {
      error: "documentation_incomplete", message: "The documentation is not complete and cannot be signed: Plan still need attention.", fieldErrors: { "note:Plan": "Write the plan; the template requires it." },
    }));
    await user.click(screen.getByRole("button", { name: "Sign note" }));
    expect(await screen.findByText(/cannot be signed: Plan still need attention/)).toBeInTheDocument();
    expect(within(screen.getByRole("region", { name: "Review before finalizing" })).getByText(/Needs attention - write this note/)).toBeInTheDocument();
    expect(server.encounters.get("e1")!.signed).toBe(false);
  });

  it("shows a stale sign as the conflict banner", async () => {
    const user = await readyToSign();
    await user.click(screen.getByRole("button", { name: "Review and finalize" }));
    server.touch("e1");
    await user.click(screen.getByRole("button", { name: "Sign note" }));
    expect(await screen.findByText("Someone else changed this while you were editing")).toBeInTheDocument();
    expect(server.encounters.get("e1")!.signed).toBe(false);
  });

  it("without a template, a complete note is signed and finalized exactly as before", async () => {
    const user = userEvent.setup();
    server.addEncounter(A, { id: "plain", entries: FOUR });
    await openEncounter("plain");
    await user.click(screen.getByRole("button", { name: "Review and finalize" }));
    expect(screen.getByRole("button", { name: "Sign note" })).toBeEnabled();
    await user.click(screen.getByRole("button", { name: "Finalize encounter" }));
    expect(await screen.findByText("Finalized")).toBeInTheDocument();
  });
});

// ---------- vitals ----------

describe("vital signs", () => {
  const fill = async (user: ReturnType<typeof userEvent.setup>, values: Record<string, string>) => {
    for (const [label, value] of Object.entries(values)) await user.type(screen.getByLabelText(label), value);
  };

  it("records a reading with one idempotency key, lists it with who recorded it, and clears the form", async () => {
    const user = userEvent.setup();
    server.addEncounter(A, { id: "e1" });
    await openEncounter("e1");
    expect(screen.getByRole("button", { name: "Record vital signs" })).toBeDisabled(); // nothing typed yet
    await fill(user, { "Systolic (mmHg)": "122", "Diastolic (mmHg)": "78", "Pulse (beats per minute)": "68", "Temperature (°C)": "36.8" });
    await user.click(screen.getByRole("button", { name: "Record vital signs" }));

    expect(await screen.findByText(/BP 122\/78 mmHg · Pulse 68 bpm · Temperature 36\.8 °C/)).toBeInTheDocument();
    expect(screen.getByText(/Recorded by Dr\. Okafor/)).toBeInTheDocument();
    expect(screen.getByLabelText("Systolic (mmHg)")).toHaveValue("");
    expect(status()).toHaveTextContent("Vital signs saved.");
    expect(server.callsToClinical("POST", "/vitals")[0].headers["Idempotency-Key"]).toBeTruthy();
    expect(server.callsToClinical("POST", "/vitals")[0].body).toMatchObject({ systolicMmHg: 122, diastolicMmHg: 78, pulseBpm: 68, temperatureC: 36.8, rowVersion: "e1" });
  });

  it("keeps what was typed and names the field when a reading is refused", async () => {
    const user = userEvent.setup();
    server.addEncounter(A, { id: "e1" });
    await openEncounter("e1");
    await fill(user, { "Systolic (mmHg)": "120" });
    await user.click(screen.getByRole("button", { name: "Record vital signs" }));
    expect(await screen.findByText("Blood pressure needs both the systolic and the diastolic value.")).toBeInTheDocument();
    expect(screen.getByLabelText("Systolic (mmHg)")).toHaveValue("120");
    expect(server.encounters.get("e1")!.vitals).toHaveLength(0);

    await user.clear(screen.getByLabelText("Systolic (mmHg)"));
    await user.type(screen.getByLabelText("Systolic (mmHg)"), "500");
    await user.type(screen.getByLabelText("Diastolic (mmHg)"), "80");
    await user.click(screen.getByRole("button", { name: "Record vital signs" }));
    expect(await screen.findByText("Enter a value between 40 and 300 mmHg.")).toBeInTheDocument();
  });

  it("after a dropped connection keeps the reading, and the retry carries the same key so it is recorded once", async () => {
    const user = userEvent.setup();
    server.addEncounter(A, { id: "e1" });
    await openEncounter("e1");
    await fill(user, { "Pulse (beats per minute)": "72" });
    server.dropNextResponse("POST /vitals");
    await user.click(screen.getByRole("button", { name: "Record vital signs" }));
    expect(await screen.findByText(/not certain the reading was saved/)).toBeInTheDocument();
    expect(screen.getByLabelText("Pulse (beats per minute)")).toHaveValue("72");
    expect(server.encounters.get("e1")!.vitals).toHaveLength(1); // stored; only the answer was lost

    await user.click(screen.getByRole("button", { name: "Record vital signs" }));
    await waitFor(() => expect(screen.getByLabelText("Pulse (beats per minute)")).toHaveValue(""));
    const keys = new Set(server.callsToClinical("POST", "/vitals").map((c) => c.headers["Idempotency-Key"]));
    expect(keys.size).toBe(1);
    expect(server.encounters.get("e1")!.vitals).toHaveLength(1);
    expect(screen.getAllByText(/Pulse 72 bpm/)).toHaveLength(1);
  });

  it("voids a reading only with a reason, and the voided reading stays listed, marked", async () => {
    const user = userEvent.setup();
    server.addEncounter(A, { id: "e1" });
    await openEncounter("e1");
    await fill(user, { "Pulse (beats per minute)": "190" });
    await user.click(screen.getByRole("button", { name: "Record vital signs" }));
    await screen.findByText(/Pulse 190 bpm/);

    await user.click(screen.getByRole("button", { name: /^Void the reading measured/ }));
    await user.click(screen.getByRole("button", { name: "Void reading" }));
    expect(screen.getByText("Say why the reading is being voided.")).toBeInTheDocument();
    expect(server.callsToClinical("POST", "/void")).toHaveLength(0);
    await user.type(screen.getByLabelText("Why is this reading being voided?"), "Wrong patient");
    await user.click(screen.getByRole("button", { name: "Void reading" }));

    expect(await screen.findByText("Voided", { selector: "span" })).toBeInTheDocument();
    expect(screen.getByText(/Voided by Dr\. Okafor on .*: Wrong patient/)).toBeInTheDocument();
    expect(screen.getByText(/Pulse 190 bpm/)).toBeInTheDocument(); // kept, not erased
    expect(screen.queryByRole("button", { name: /^Void the reading measured/ })).not.toBeInTheDocument();
  });
});

// ---------- the finalized note, its amendments, and who may do what ----------

describe("a finalized note and its amendments", () => {
  function finalized() {
    server.addEncounter(A, { id: "done", status: "Finalized", entries: FOUR, addenda: ["Existing addendum"] });
    server.setNote("done", "Plan", "Original plan", { required: true });
    server.setNote("done", "Treatment", "Access cavity prepared");
    server.encounters.get("done")!.vitals.push({
      id: "v1", measuredAtUtc: "2026-10-02T15:30:00Z", systolicMmHg: 118, diastolicMmHg: 76, pulseBpm: 70, respirationsPerMinute: null, temperatureC: null, oxygenSaturationPercent: null, weightKg: null, heightCm: null,
      note: null, createdAtUtc: "2026-10-02T15:30:00Z", recordedByName: "Hana Hygienist", isVoided: false, voidedAtUtc: null, voidedByName: null, voidReason: null, key: "k",
    });
  }

  it("shows the notes and vitals read-only, with no way to change the original", async () => {
    finalized();
    await openEncounter("done");
    expect(screen.getByText("Original plan")).toBeInTheDocument();
    expect(screen.getByText("Access cavity prepared")).toBeInTheDocument();
    expect(screen.getByText(/BP 118\/76 mmHg · Pulse 70 bpm/)).toBeInTheDocument();
    expect(screen.queryByRole("textbox", { name: /Plan|Treatment/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Record vital signs|Void|Use template|Add section|Save .* now/ })).not.toBeInTheDocument();
  });

  it("is a timeline: when the original was finalized and by whom, then each addendum with its author, time and what it amends", async () => {
    const user = userEvent.setup();
    finalized();
    await openEncounter("done");
    expect(screen.getByText(/Original finalized .* by Dr\. Okafor\./)).toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText("What does this amend? (optional)"), "Plan");
    await user.type(screen.getByLabelText("New addendum"), "Plan changed: refer out.");
    await user.click(screen.getByRole("button", { name: "Add addendum" }));

    expect(await screen.findByText("Plan changed: refer out.")).toBeInTheDocument();
    expect(screen.getByText("Amends: Plan")).toBeInTheDocument();
    expect(screen.getAllByText(/^Added .* by Dr\. Okafor$/)).toHaveLength(2);
    expect(server.callsToClinical("POST", "/addenda")[0].body).toEqual({ text: "Plan changed: refer out.", section: "Plan" });
    expect(screen.getByText("Original plan")).toBeInTheDocument(); // the original is untouched
    expect(screen.getByLabelText("What does this amend? (optional)")).toHaveValue("");
  });

  it("keeps the typed amendment and the section when it cannot be recorded", async () => {
    const user = userEvent.setup();
    finalized();
    await openEncounter("done");
    server.failClinical("POST /api/encounters/done/addenda", json(500, { error: "server_error", message: "The addendum could not be recorded." }));
    await user.selectOptions(screen.getByLabelText("What does this amend? (optional)"), "Vitals");
    await user.type(screen.getByLabelText("New addendum"), "Keep me");
    await user.click(screen.getByRole("button", { name: "Add addendum" }));
    expect(await screen.findByText("The addendum could not be recorded.")).toBeInTheDocument();
    expect(screen.getByLabelText("New addendum")).toHaveValue("Keep me");
    expect(screen.getByLabelText("What does this amend? (optional)")).toHaveValue("Vitals");
    expect(server.encounters.get("done")!.addenda).toHaveLength(1);
  });
});

describe("a role that may only read", () => {
  it("sees notes, vitals and the signature on a draft but gets no way to change or sign", async () => {
    server.permissions = READ_ONLY;
    server.addEncounter(A, { id: "e1", entries: FOUR });
    server.setNote("e1", "Subjective", "Pain on the lower left");
    server.encounters.get("e1")!.signed = true;
    await openEncounter("e1");

    expect(screen.getByText("Pain on the lower left")).toBeInTheDocument();
    expect(screen.getByText(/Signed by Dr\. Okafor/)).toBeInTheDocument();
    expect(screen.queryByRole("textbox")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Unsign|Sign note|Review and finalize|Record vital signs|Use template|Add section/ })).not.toBeInTheDocument();
  });
});

describe("accessibility (axe, jsdom)", () => {
  it("has no violations on a templated draft with vitals, the signing review, a signed note and a finalized note with amendments", async () => {
    const user = userEvent.setup();
    server.record.addTemplate({ name: "SOAP note" });
    server.addEncounter(A, { id: "e1", entries: FOUR });
    const { container } = await openEncounter("e1");
    await user.selectOptions(await screen.findByLabelText("Start from a template"), "SOAP note");
    await user.click(screen.getByRole("button", { name: "Use template" }));
    await screen.findByText("Template: SOAP note");
    await user.type(screen.getByLabelText("Pulse (beats per minute)"), "70");
    await user.click(screen.getByRole("button", { name: "Record vital signs" }));
    await screen.findByText(/Pulse 70 bpm/);
    expect(await axe(container)).toHaveNoViolations();

    await user.click(screen.getByRole("button", { name: "Review and finalize" }));
    expect(await axe(container)).toHaveNoViolations();

    server.encounters.get("e1")!.signed = true;
    window.history.pushState({}, "", `/patients/${A}/clinical`);
    window.dispatchEvent(new PopStateEvent("popstate"));
    await screen.findByRole("heading", { name: "Clinical record" });
    server.addEncounter(A, { id: "done", status: "Finalized", entries: FOUR, addenda: ["An addendum"] });
    server.setNote("done", "Plan", "Original plan");
    window.history.pushState({}, "", `/patients/${A}/clinical/done`);
    window.dispatchEvent(new PopStateEvent("popstate"));
    await screen.findByRole("heading", { name: "Addenda" });
    expect(await axe(container)).toHaveNoViolations();
  });
});
