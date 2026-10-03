import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { App } from "./App";
import { FakeClinicalServer } from "./test/fakeClinicalServer";
import { json, makePatient } from "./test/fakePatientServer";

/**
 * STORY-005: the patient's Clinical tab - the encounter list, documenting medical and dental history, allergies and medications, reviewing and finalizing, and amending
 * by addendum - exercised through the real <App /> (real router, shell, auth) against an in-memory fake of the clinical API. The rules themselves are proven by the
 * backend tests and the real-backend browser walkthrough; what is proven here is the UI's behaviour for each outcome, including every failure path: incomplete
 * documentation, a failed or dropped save (no data loss), a stale edit, an amendment that cannot be confirmed, and a role that may only read.
 */
const A = "aaaaaaaa-0000-0000-0000-000000000001";
const B = "bbbbbbbb-0000-0000-0000-000000000002";
const READ_ONLY = ["ViewPatientRecords", "ViewClinicalDocumentation"];

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
const region = (name: string) => screen.getByRole("region", { name });

beforeEach(() => {
  server = new FakeClinicalServer();
  server.add(makePatient({ id: A, firstName: "Ann", lastName: "Lee" }));
  server.add(makePatient({ id: B, firstName: "Ben", lastName: "Moss", dateOfBirth: "1990-01-01", phone: "555-020-0200" }));
  server.install();
});
afterEach(() => vi.unstubAllGlobals());

/** Fills the open entry form of a section and submits it. */
async function addEntry(user: ReturnType<typeof userEvent.setup>, section: string, noun: string, fields: Record<string, string>) {
  await user.click(within(region(section)).getByRole("button", { name: `Add ${noun}: ${section}` }));
  for (const [label, value] of Object.entries(fields)) {
    const control = within(region(section)).getByLabelText(label);
    if (control.tagName === "SELECT") await user.selectOptions(control, value);
    else await user.type(control, value);
  }
  await user.click(within(region(section)).getByRole("button", { name: `Add ${noun}` }));
}

// ---------- visibility and the list ----------

describe("who sees the Clinical tab", () => {
  it("shows it to a clinician and lists the patient's encounters with their status and completeness in words", async () => {
    server.addEncounter(A, { id: "done", status: "Finalized", at: "2026-09-20T15:00:00Z", entries: [{ kind: "MedicalHistory", name: "Asthma" }, { kind: "DentalHistory", name: "Crown" }, { kind: "Allergy", name: "Latex" }, { kind: "Medication", name: "Albuterol" }], addenda: ["Note"] });
    server.addEncounter(A, { id: "open", at: "2026-10-01T15:00:00Z", entries: [{ kind: "Allergy", name: "Penicillin" }] });
    server.addEncounter(B, { id: "bens" });
    open(`/patients/${A}/clinical`);

    expect(await screen.findByRole("heading", { name: "Clinical documentation" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Clinical" })).toBeInTheDocument();
    const items = screen.getAllByRole("listitem").filter((li) => li.className.includes("alv-clinical__list-item"));
    expect(items).toHaveLength(2); // Ben's encounter is not listed
    expect(items[0]).toHaveTextContent("Draft - not finalized"); // newest first
    expect(items[0]).toHaveTextContent("Some sections not yet addressed");
    expect(items[1]).toHaveTextContent("Finalized");
    expect(items[1]).toHaveTextContent("All four sections addressed");
    expect(items[1]).toHaveTextContent("4 entries");
    expect(items[1]).toHaveTextContent("1 addendum");
  });

  it("says plainly when nothing has been documented", async () => {
    open(`/patients/${A}/clinical`);
    expect(await screen.findByText("No encounters documented yet")).toBeInTheDocument();
  });

  it("is hidden from a role that may not read clinical documentation: no tab, and the page is denied", async () => {
    server.permissions = ["ViewPatientRecords", "ViewSignedForms"]; // front desk / billing hold ViewPatientRecords but not the clinical permission
    server.addEncounter(A, { id: "x", entries: [{ kind: "Allergy", name: "Penicillin" }] });
    open(`/patients/${A}`);
    await screen.findByRole("heading", { name: "Patient workspace" });
    expect(screen.queryByRole("link", { name: "Clinical" })).not.toBeInTheDocument();

    window.history.pushState({}, "", `/patients/${A}/clinical/x`);
    window.dispatchEvent(new PopStateEvent("popstate"));
    expect((await screen.findAllByText(/permission/i)).length).toBeGreaterThan(0);
    expect(screen.queryByText("Penicillin")).not.toBeInTheDocument();
    expect(server.callsToClinical("GET", "/api/encounters/x")).toHaveLength(0); // the encounter was never even requested
  });

  it("lets a read-only role read the list and an encounter but gives it no way to change anything", async () => {
    server.permissions = READ_ONLY;
    server.addEncounter(A, { id: "e1", entries: [{ kind: "Allergy", name: "Penicillin", reaction: "Hives" }] });
    open(`/patients/${A}/clinical`);
    await screen.findByRole("heading", { name: "Clinical documentation" });
    expect(screen.queryByRole("button", { name: "Start an encounter" })).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole("link", { name: /Encounter on/ }));
    await screen.findByRole("heading", { name: /^Encounter on/ });
    expect(screen.getByText("Penicillin")).toBeInTheDocument();
    expect(screen.getByText(/your role cannot change it/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^Add / })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Review and finalize" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Change|Remove|Reviewed - none reported/ })).not.toBeInTheDocument();
  });
});

// ---------- starting an encounter ----------

describe("starting an encounter", () => {
  it("creates a draft, opens it and sends one request with an idempotency key", async () => {
    const user = userEvent.setup();
    open(`/patients/${A}/clinical`);
    await user.click(await screen.findByRole("button", { name: "Start an encounter" }));
    expect(await screen.findByRole("heading", { name: /^Encounter on/ })).toBeInTheDocument();
    expect(screen.getByText("Draft - not finalized")).toBeInTheDocument();
    const posts = server.callsToClinical("POST", "/encounters");
    expect(posts).toHaveLength(1);
    expect(posts[0].headers["Idempotency-Key"]).toBeTruthy();
    expect(server.encounters.size).toBe(1);
  });

  it("cannot create two encounters from a double click on a slow server: the button is busy and the request carries one key", async () => {
    const user = userEvent.setup();
    open(`/patients/${A}/clinical`);
    const start = await screen.findByRole("button", { name: "Start an encounter" });
    const release = server.hold("POST /api/patients");
    await user.dblClick(start);
    await waitFor(() => expect(start).toBeDisabled()); // in flight: the second click had nothing to press
    release();
    await screen.findByRole("heading", { name: /^Encounter on/ });
    const keys = new Set(server.callsToClinical("POST", "/encounters").map((c) => c.headers["Idempotency-Key"]));
    expect(keys.size).toBe(1);
    expect(server.encounters.size).toBe(1);
  });

  it("after a dropped connection says it is not certain, and the retry reuses the key so it cannot start two", async () => {
    const user = userEvent.setup();
    open(`/patients/${A}/clinical`);
    server.dropNextResponse("POST /encounters");
    await user.click(await screen.findByRole("button", { name: "Start an encounter" }));
    expect(await screen.findByText(/not certain the encounter was started/)).toBeInTheDocument();
    expect(server.encounters.size).toBe(1); // it was stored; only the answer was lost

    await user.click(screen.getByRole("button", { name: "Start an encounter" }));
    await screen.findByRole("heading", { name: /^Encounter on/ });
    const keys = server.callsToClinical("POST", "/encounters").map((c) => c.headers["Idempotency-Key"]);
    expect(keys).toHaveLength(2);
    expect(keys[0]).toBe(keys[1]);
    expect(server.encounters.size).toBe(1);
  });

  it("will not start an encounter for an inactive patient", async () => {
    server.add(makePatient({ id: "inactive-1", firstName: "Ina", lastName: "Cole", isActive: false }));
    open("/patients/inactive-1/clinical");
    expect(await screen.findByRole("button", { name: "Start an encounter" })).toBeDisabled();
    expect(screen.getByText(/This patient is inactive\. Reactivate them before documenting a new encounter\./)).toBeInTheDocument();
  });
});

// ---------- acceptance 1: documenting history, allergies and medications ----------

describe("documenting an encounter", () => {
  it("records medical history, dental history, an allergy and a medication, each saved as it is made", async () => {
    const user = userEvent.setup();
    server.addEncounter(A, { id: "e1" });
    await openEncounter("e1");

    await addEntry(user, "Medical history", "medical history item", { Name: "Hypertension", Details: "Controlled with medication" });
    await addEntry(user, "Dental history", "dental history item", { Name: "Root canal, lower left, 2019" });
    await addEntry(user, "Allergies", "allergy", { Name: "Penicillin", Reaction: "Hives", Severity: "Moderate" });
    await addEntry(user, "Medications", "medication", { Name: "Lisinopril", Dose: "10 mg", Frequency: "daily" });

    expect(within(region("Medical history")).getByText("Hypertension")).toBeInTheDocument();
    expect(within(region("Medical history")).getByText("Details: Controlled with medication".replace("Details: ", ""))).toBeInTheDocument();
    expect(within(region("Allergies")).getByText("Reaction: Hives · Severity: Moderate")).toBeInTheDocument();
    expect(within(region("Medications")).getByText("Dose: 10 mg · Frequency: daily")).toBeInTheDocument();
    for (const name of ["Medical history", "Dental history", "Allergies", "Medications"]) expect(within(region(name)).getByText("Recorded")).toBeInTheDocument();
    expect(screen.getByText("Medications entry saved.")).toBeInTheDocument(); // the status line says it in words

    // it is stored, not just drawn
    const stored = [...server.encounters.get("e1")!.entries];
    expect(stored.map((x) => [x.kind, x.name])).toEqual([["MedicalHistory", "Hypertension"], ["DentalHistory", "Root canal, lower left, 2019"], ["Allergy", "Penicillin"], ["Medication", "Lisinopril"]]);
    expect(server.callsToClinical("POST", "/entries").every((c) => typeof c.body?.rowVersion === "string")).toBe(true);
  });

  it("shows only the fields that belong to each section", async () => {
    const user = userEvent.setup();
    server.addEncounter(A, { id: "e1" });
    await openEncounter("e1");
    await user.click(within(region("Allergies")).getByRole("button", { name: "Add allergy: Allergies" }));
    expect(within(region("Allergies")).getByLabelText("Reaction")).toBeInTheDocument();
    expect(within(region("Allergies")).getByLabelText("Severity")).toBeInTheDocument();
    expect(within(region("Allergies")).queryByLabelText("Dose")).not.toBeInTheDocument();
    await user.click(within(region("Medications")).getByRole("button", { name: "Add medication: Medications" }));
    expect(within(region("Medications")).getByLabelText("Dose")).toBeInTheDocument();
    expect(within(region("Medications")).queryByLabelText("Reaction")).not.toBeInTheDocument();
    await user.click(within(region("Dental history")).getByRole("button", { name: "Add dental history item: Dental history" }));
    expect(within(region("Dental history")).queryByLabelText("Dose")).not.toBeInTheDocument();
    expect(within(region("Dental history")).queryByLabelText("Reaction")).not.toBeInTheDocument();
  });

  it("changes and removes entries, and a removed entry leaves the active section", async () => {
    const user = userEvent.setup();
    server.addEncounter(A, { id: "e1", entries: [{ kind: "Medication", name: "Lisinopril", dose: "10 mg", frequency: "daily" }] });
    await openEncounter("e1");

    await user.click(screen.getByRole("button", { name: "Change Lisinopril" }));
    const dose = within(region("Medications")).getByLabelText("Dose");
    await user.clear(dose);
    await user.type(dose, "20 mg");
    await user.click(screen.getByRole("button", { name: "Save changes" }));
    expect(await screen.findByText("Dose: 20 mg · Frequency: daily")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Remove Lisinopril" }));
    await waitFor(() => expect(within(region("Medications")).queryByText("Lisinopril")).not.toBeInTheDocument());
    expect(within(region("Medications")).getByText("Not yet addressed")).toBeInTheDocument();
    expect(server.encounters.get("e1")!.entries[0].removed).toBe(true); // kept by the server, never deleted
  });

  it("records a section as reviewed with nothing to report, and clears that review to allow entries", async () => {
    const user = userEvent.setup();
    server.addEncounter(A, { id: "e1" });
    await openEncounter("e1");

    await user.click(within(region("Allergies")).getByRole("button", { name: "Reviewed - none reported: Allergies" }));
    expect(await within(region("Allergies")).findByText("Reviewed - none reported")).toBeInTheDocument();
    expect(within(region("Allergies")).getByText("Reviewed with the patient: nothing to report here.")).toBeInTheDocument();
    expect(within(region("Allergies")).queryByRole("button", { name: /^Add allergy/ })).not.toBeInTheDocument(); // cannot add while marked

    await user.click(within(region("Allergies")).getByRole("button", { name: "Clear review to add entries: Allergies" }));
    expect(await within(region("Allergies")).findByRole("button", { name: "Add allergy: Allergies" })).toBeInTheDocument();
    expect(within(region("Allergies")).getByText("Not yet addressed")).toBeInTheDocument();
  });

  it("does not offer 'reviewed - none reported' on a section that already has entries", async () => {
    server.addEncounter(A, { id: "e1", entries: [{ kind: "Allergy", name: "Penicillin" }] });
    await openEncounter("e1");
    expect(within(region("Allergies")).queryByRole("button", { name: /Reviewed - none reported/ })).not.toBeInTheDocument();
    expect(within(region("Medications")).getByRole("button", { name: /Reviewed - none reported/ })).toBeInTheDocument();
  });

  it("is usable with the keyboard alone: focus the button, Enter opens the form, typing fills it, Enter saves", async () => {
    const user = userEvent.setup();
    server.addEncounter(A, { id: "e1" });
    await openEncounter("e1");
    within(region("Allergies")).getByRole("button", { name: "Add allergy: Allergies" }).focus();
    await user.keyboard("{Enter}");
    await user.keyboard("Latex{Enter}"); // the name field takes focus when the form opens; Enter submits
    expect(await within(region("Allergies")).findByText("Latex")).toBeInTheDocument();
    expect(server.encounters.get("e1")!.entries.map((x) => x.name)).toEqual(["Latex"]);
  });
});

// ---------- failure paths: validation, data loss ----------

describe("when an entry cannot be saved", () => {
  it("asks for a name without sending anything", async () => {
    const user = userEvent.setup();
    server.addEncounter(A, { id: "e1" });
    await openEncounter("e1");
    await user.click(within(region("Allergies")).getByRole("button", { name: "Add allergy: Allergies" }));
    await user.click(within(region("Allergies")).getByRole("button", { name: "Add allergy" }));
    expect(within(region("Allergies")).getByText("A name is required.")).toBeInTheDocument();
    expect(server.callsToClinical("POST", "/entries")).toHaveLength(0);
  });

  it("shows the server's per-field refusal next to the field and keeps everything typed", async () => {
    const user = userEvent.setup();
    server.addEncounter(A, { id: "e1" });
    await openEncounter("e1");
    server.failClinical("POST /api/encounters/e1/entries", json(400, { error: "validation_failed", message: "Some fields need attention.", fieldErrors: { reaction: "Keep the reaction to 200 characters or fewer." } }));
    await user.click(within(region("Allergies")).getByRole("button", { name: "Add allergy: Allergies" }));
    await user.type(within(region("Allergies")).getByLabelText("Name"), "Penicillin");
    await user.type(within(region("Allergies")).getByLabelText("Reaction"), "Hives");
    await user.click(within(region("Allergies")).getByRole("button", { name: "Add allergy" }));

    expect(await within(region("Allergies")).findByText("Keep the reaction to 200 characters or fewer.")).toBeInTheDocument();
    expect(within(region("Allergies")).getByLabelText("Name")).toHaveValue("Penicillin");
    expect(within(region("Allergies")).getByLabelText("Reaction")).toHaveValue("Hives");
    expect(server.encounters.get("e1")!.entries).toHaveLength(0);
  });

  it("keeps the form and what was typed when the save fails, says so, and a retry then works", async () => {
    const user = userEvent.setup();
    server.addEncounter(A, { id: "e1" });
    await openEncounter("e1");
    server.failClinical("POST /api/encounters/e1/entries", json(500, { error: "server_error", message: "Something went wrong." }));
    await user.click(within(region("Medications")).getByRole("button", { name: "Add medication: Medications" }));
    await user.type(within(region("Medications")).getByLabelText("Name"), "Aspirin");
    await user.click(within(region("Medications")).getByRole("button", { name: "Add medication" }));

    expect(await screen.findByText(/Not saved: Something went wrong/)).toBeInTheDocument();
    expect(within(region("Medications")).getByLabelText("Name")).toHaveValue("Aspirin"); // not lost
    await user.click(within(region("Medications")).getByRole("button", { name: "Add medication" }));
    expect(await within(region("Medications")).findByText("Aspirin")).toBeInTheDocument();
    expect(server.encounters.get("e1")!.entries).toHaveLength(1);
  });

  it("after a dropped connection keeps the typed values, and sending again does not add the entry twice", async () => {
    const user = userEvent.setup();
    server.addEncounter(A, { id: "e1" });
    await openEncounter("e1");
    server.dropNextResponse("POST /entries");
    await user.click(within(region("Allergies")).getByRole("button", { name: "Add allergy: Allergies" }));
    await user.type(within(region("Allergies")).getByLabelText("Name"), "Penicillin");
    await user.click(within(region("Allergies")).getByRole("button", { name: "Add allergy" }));

    expect(await screen.findByText(/the connection dropped/)).toBeInTheDocument();
    expect(within(region("Allergies")).getByLabelText("Name")).toHaveValue("Penicillin");
    expect(server.encounters.get("e1")!.entries).toHaveLength(1); // it WAS stored; only the answer was lost

    await user.click(within(region("Allergies")).getByRole("button", { name: "Add allergy" }));
    await waitFor(() => expect(within(region("Allergies")).getAllByText("Penicillin")).toHaveLength(1));
    expect(server.encounters.get("e1")!.entries).toHaveLength(1); // the retry was a quiet repeat
  });
});

// ---------- failure path: concurrency ----------

describe("when someone else changed the note", () => {
  it("shows the shared conflict banner, keeps what was typed, and reloading refreshes in place so the typed values survive", async () => {
    const user = userEvent.setup();
    server.addEncounter(A, { id: "e1", entries: [{ kind: "MedicalHistory", name: "Asthma" }] });
    await openEncounter("e1");
    await user.click(within(region("Allergies")).getByRole("button", { name: "Add allergy: Allergies" }));
    await user.type(within(region("Allergies")).getByLabelText("Name"), "Latex");
    server.touch("e1"); // another clinician saved while this one was typing
    await user.click(within(region("Allergies")).getByRole("button", { name: "Add allergy" }));

    expect(await screen.findByText("Someone else changed this while you were editing")).toBeInTheDocument();
    expect(screen.getByText(/Not saved: someone else changed this note/)).toBeInTheDocument();
    expect(server.encounters.get("e1")!.entries.map((x) => x.name)).toEqual(["Asthma"]); // nothing was overwritten
    expect(within(region("Allergies")).getByLabelText("Name")).toHaveValue("Latex");

    await user.click(screen.getByRole("button", { name: /reload/i }));
    await waitFor(() => expect(screen.queryByText("Someone else changed this while you were editing")).not.toBeInTheDocument());
    expect(within(region("Allergies")).getByLabelText("Name")).toHaveValue("Latex"); // the form was NOT remounted: typed values survive the reload
    await user.click(within(region("Allergies")).getByRole("button", { name: "Add allergy" }));
    expect(await within(region("Allergies")).findByText("Latex")).toBeInTheDocument();
  });
});

// ---------- finalizing ----------

describe("reviewing and finalizing", () => {
  it("flags the sections that are not addressed, will not finalize until each has an entry or a none-reported review, then finalizes", async () => {
    const user = userEvent.setup();
    server.addEncounter(A, { id: "e1", entries: [{ kind: "MedicalHistory", name: "Asthma" }] });
    await openEncounter("e1");

    await user.click(screen.getByRole("button", { name: "Review and finalize" }));
    const review = screen.getByRole("region", { name: "Review before finalizing" });
    expect(within(review).getAllByText(/Needs attention/)).toHaveLength(3);
    expect(within(review).getByRole("button", { name: "Finalize encounter" })).toBeDisabled();

    await user.click(screen.getByRole("button", { name: "Keep editing" }));
    for (const [section] of [["Dental history"], ["Allergies"], ["Medications"]]) await user.click(within(region(section)).getByRole("button", { name: `Reviewed - none reported: ${section}` }));
    await waitFor(() => expect(within(region("Medications")).getByText("Reviewed - none reported")).toBeInTheDocument());

    await user.click(screen.getByRole("button", { name: "Review and finalize" }));
    expect(screen.queryByText(/Needs attention/)).not.toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Finalize encounter" }));

    expect(await screen.findByText("Finalized")).toBeInTheDocument();
    expect(screen.getByText(/can no longer be changed/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^Add (medical|dental|allergy|medication)|Change|Remove|Review and finalize|Reviewed - none reported/ })).not.toBeInTheDocument();
    expect(server.encounters.get("e1")!.status).toBe("Finalized");
    expect(screen.getByRole("heading", { name: "Addenda" })).toBeInTheDocument();
  });

  it("shows the server's refusal when another clinician changed the documentation meanwhile, and nothing is finalized", async () => {
    const user = userEvent.setup();
    server.addEncounter(A, { id: "e1", entries: [{ kind: "MedicalHistory", name: "a" }, { kind: "DentalHistory", name: "b" }, { kind: "Allergy", name: "c" }, { kind: "Medication", name: "d" }] });
    await openEncounter("e1");
    await user.click(screen.getByRole("button", { name: "Review and finalize" }));
    server.failClinical("POST /api/encounters/e1/finalize", json(409, { error: "documentation_incomplete", message: "The documentation is not complete: Allergy still need attention.", fieldErrors: { Allergy: "Record at least one entry or mark it reviewed, none reported." } }));
    await user.click(screen.getByRole("button", { name: "Finalize encounter" }));

    expect(await screen.findByText(/The documentation is not complete/)).toBeInTheDocument();
    expect(screen.getByText(/Not finalized: some sections still need attention/)).toBeInTheDocument();
    expect(within(screen.getByRole("region", { name: "Review before finalizing" })).getByText(/Needs attention/)).toBeInTheDocument();
    expect(server.encounters.get("e1")!.status).toBe("Draft");
  });

  it("refuses to finalize from a stale screen with the conflict banner", async () => {
    const user = userEvent.setup();
    server.addEncounter(A, { id: "e1", entries: [{ kind: "MedicalHistory", name: "a" }, { kind: "DentalHistory", name: "b" }, { kind: "Allergy", name: "c" }, { kind: "Medication", name: "d" }] });
    await openEncounter("e1");
    await user.click(screen.getByRole("button", { name: "Review and finalize" }));
    server.touch("e1");
    await user.click(screen.getByRole("button", { name: "Finalize encounter" }));
    expect(await screen.findByText("Someone else changed this while you were editing")).toBeInTheDocument();
    expect(server.encounters.get("e1")!.status).toBe("Draft");
  });
});

// ---------- acceptance 2: the original is preserved with an addendum ----------

describe("amending a finalized encounter", () => {
  const complete = [{ kind: "MedicalHistory", name: "Asthma" }, { kind: "DentalHistory", name: "Crown" }, { kind: "Allergy", name: "Penicillin", reaction: "Hives" }, { kind: "Medication", name: "Albuterol" }];

  it("shows the original read-only with its addenda beside it, oldest first, and no way to change the original", async () => {
    server.addEncounter(A, { id: "e1", status: "Finalized", entries: complete, addenda: ["First addendum", "Second addendum"] });
    await openEncounter("e1");
    expect(within(region("Allergies")).getByText("Penicillin")).toBeInTheDocument();
    expect(within(region("Allergies")).queryByRole("button")).not.toBeInTheDocument();
    const addenda = screen.getAllByText(/^(First|Second) addendum$/).map((n) => n.textContent);
    expect(addenda).toEqual(["First addendum", "Second addendum"]);
    expect(screen.getAllByText(/^Added /)).toHaveLength(2);
  });

  it("adds an addendum beside the untouched original, with the text cleared once it is saved", async () => {
    const user = userEvent.setup();
    server.addEncounter(A, { id: "e1", status: "Finalized", entries: complete });
    await openEncounter("e1");
    expect(screen.getByText(/No addenda\. The note above is exactly as it was finalized\./)).toBeInTheDocument();

    await user.type(screen.getByLabelText("New addendum"), "Patient also reports a latex sensitivity.");
    await user.click(screen.getByRole("button", { name: "Add addendum" }));
    expect(await screen.findByText("Patient also reports a latex sensitivity.")).toBeInTheDocument();
    expect(screen.getByLabelText("New addendum")).toHaveValue("");
    expect(within(region("Allergies")).getByText("Penicillin")).toBeInTheDocument(); // the original is untouched
    expect(server.encounters.get("e1")!.addenda).toHaveLength(1);
    expect(server.callsToClinical("POST", "/addenda")[0].headers["Idempotency-Key"]).toBeTruthy();
  });

  it("asks for text, and a refusal keeps the typed addendum and says why", async () => {
    const user = userEvent.setup();
    server.addEncounter(A, { id: "e1", status: "Finalized", entries: complete });
    await openEncounter("e1");
    await user.click(screen.getByRole("button", { name: "Add addendum" }));
    expect(screen.getByText("Write the addendum.")).toBeInTheDocument();
    expect(server.callsToClinical("POST", "/addenda")).toHaveLength(0);

    server.failClinical("POST /api/encounters/e1/addenda", json(500, { error: "server_error", message: "The addendum could not be recorded." }));
    await user.type(screen.getByLabelText("New addendum"), "Keep me");
    await user.click(screen.getByRole("button", { name: "Add addendum" }));
    expect(await screen.findByText("The addendum could not be recorded.")).toBeInTheDocument();
    expect(screen.getByLabelText("New addendum")).toHaveValue("Keep me");
    expect(server.encounters.get("e1")!.addenda).toHaveLength(0);
  });

  it("after a dropped connection keeps the text, and the retry reuses the same key so the addendum is added exactly once", async () => {
    const user = userEvent.setup();
    server.addEncounter(A, { id: "e1", status: "Finalized", entries: complete });
    await openEncounter("e1");
    server.dropNextResponse("POST /addenda");
    await user.type(screen.getByLabelText("New addendum"), "Sent twice, saved once");
    await user.click(screen.getByRole("button", { name: "Add addendum" }));
    expect(await screen.findByText(/not certain the addendum was saved/)).toBeInTheDocument();
    expect(screen.getByLabelText("New addendum")).toHaveValue("Sent twice, saved once");
    expect(server.encounters.get("e1")!.addenda).toHaveLength(1); // stored; only the answer was lost

    await user.click(screen.getByRole("button", { name: "Add addendum" }));
    await waitFor(() => expect(screen.getAllByText("Sent twice, saved once", { selector: "p" })).toHaveLength(1));
    const keys = server.callsToClinical("POST", "/addenda").map((c) => c.headers["Idempotency-Key"]);
    expect(keys).toHaveLength(2);
    expect(keys[0]).toBe(keys[1]);
    expect(server.encounters.get("e1")!.addenda).toHaveLength(1);
  });

  it("does not offer an addendum on a draft", async () => {
    server.addEncounter(A, { id: "draft" });
    await openEncounter("draft");
    expect(screen.queryByRole("heading", { name: "Addenda" })).not.toBeInTheDocument();
  });

  it("shows a read-only role the addenda but no way to add one", async () => {
    server.permissions = READ_ONLY;
    server.addEncounter(A, { id: "done", status: "Finalized", entries: complete, addenda: ["Existing addendum"] });
    await openEncounter("done");
    expect(screen.getByText("Existing addendum")).toBeInTheDocument();
    expect(screen.queryByLabelText("New addendum")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Add addendum" })).not.toBeInTheDocument();
  });
});

// ---------- history, safety and accessibility ----------

describe("the encounter's own history and which patient it belongs to", () => {
  it("lists every change in words, oldest first, behind a disclosure", async () => {
    const user = userEvent.setup();
    server.addEncounter(A, { id: "e1" });
    await openEncounter("e1");
    await addEntry(user, "Allergies", "allergy", { Name: "Penicillin" });
    expect(await screen.findByText(/History of this encounter \(2\)/)).toBeInTheDocument();
    expect(screen.getByText("Encounter started")).toBeInTheDocument();
    expect(screen.getByText("Entry added")).toBeInTheDocument();
  });

  it("never shows another patient's encounter under this patient", async () => {
    server.addEncounter(B, { id: "bens", entries: [{ kind: "Allergy", name: "Secret allergy" }] });
    open(`/patients/${A}/clinical/bens`);
    expect(await screen.findByText("That encounter was not found")).toBeInTheDocument();
    expect(screen.queryByText("Secret allergy")).not.toBeInTheDocument();
  });

  it("says so for an encounter that does not exist", async () => {
    open(`/patients/${A}/clinical/nope`);
    expect(await screen.findByText("That encounter was not found")).toBeInTheDocument();
  });
});

describe("accessibility (axe, jsdom)", () => {
  it("has no violations on the list, an open draft with a form, the finalize review and a finalized note", async () => {
    const user = userEvent.setup();
    server.addEncounter(A, { id: "list-1", entries: [{ kind: "Allergy", name: "Penicillin" }] });
    server.addEncounter(A, { id: "done", status: "Finalized", at: "2026-09-01T10:00:00Z", entries: [{ kind: "MedicalHistory", name: "Asthma" }, { kind: "DentalHistory", name: "Crown" }, { kind: "Allergy", name: "Latex" }, { kind: "Medication", name: "Albuterol" }], addenda: ["An addendum"] });
    const { container } = open(`/patients/${A}/clinical`);
    await screen.findByRole("heading", { name: "Clinical documentation" });
    expect(await axe(container)).toHaveNoViolations();

    await user.click(screen.getAllByRole("link", { name: /Encounter on/ })[0]);
    await screen.findByRole("heading", { name: /^Encounter on/ });
    await user.click(within(region("Allergies")).getByRole("button", { name: /^Add allergy/ }));
    expect(await axe(container)).toHaveNoViolations();

    await user.click(within(region("Allergies")).getByRole("button", { name: "Cancel" }));
    await user.click(screen.getByRole("button", { name: "Review and finalize" }));
    expect(await axe(container)).toHaveNoViolations();

    window.history.pushState({}, "", `/patients/${A}/clinical/done`);
    window.dispatchEvent(new PopStateEvent("popstate"));
    await screen.findByRole("heading", { name: "Addenda" });
    expect(await axe(container)).toHaveNoViolations();
  });
});
