import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { App } from "./App";
import { FakeClinicalServer } from "./test/fakeClinicalServer";
import { json, makePatient } from "./test/fakePatientServer";

/**
 * ALV-005-C01: the patient's longitudinal clinical record (the history summary on the Clinical tab) and the note templates page, exercised through the real <App /> against an
 * in-memory fake of the API. The rules themselves are proven by the backend tests and the real-backend walkthrough; what is proven here is the UI's behaviour for each outcome:
 * a status said in words, no invented facts ("none known" and "unknown" are statements, never items), history-preserving change, a reason when removing, and every failure path
 * - a refused save, a dropped connection, a stale edit, a failed load and a role that may only read.
 */
const A = "aaaaaaaa-0000-0000-0000-000000000001";
const READ_ONLY = ["ViewPatientRecords", "ViewClinicalDocumentation"];
let server: FakeClinicalServer;

function open(path: string) {
  window.history.pushState({}, "", path);
  return render(<App />);
}
const card = (name: string) => screen.getByRole("region", { name: `Record: ${name}` });
async function openRecord() {
  const view = open(`/patients/${A}/clinical`);
  await screen.findByRole("heading", { name: "Clinical record" });
  return view;
}

beforeEach(() => {
  server = new FakeClinicalServer();
  server.add(makePatient({ id: A, firstName: "Ann", lastName: "Lee" }));
  server.install();
});
afterEach(() => vi.unstubAllGlobals());

// ---------- reading the record ----------

describe("the clinical record on the Clinical tab", () => {
  it("shows each section with its items, status in words and who changed what and when", async () => {
    server.record.addItem(A, { kind: "Allergy", name: "Penicillin", reaction: "Hives", severity: "Moderate", status: "Resolved", updatedAt: "2026-09-10T10:00:00Z", updatedByName: "Hana Hygienist" });
    server.record.addItem(A, { kind: "Medication", name: "Lisinopril", dose: "10 mg", frequency: "daily" });
    await openRecord();

    const allergies = card("Allergies");
    expect(within(allergies).getByText("Penicillin")).toBeInTheDocument();
    expect(within(allergies).getByText("Resolved")).toBeInTheDocument(); // the item's status, as a word
    expect(within(allergies).getByText(/Reaction: Hives · Severity: Moderate/)).toBeInTheDocument();
    expect(within(allergies).getByText(/Added by Dr\. Okafor on .*Last changed by Hana Hygienist on /)).toBeInTheDocument();
    expect(within(card("Medications")).getByText(/Dose: 10 mg · Frequency: daily/)).toBeInTheDocument();
  });

  it.each([
    ["Reviewed", "Reviewed", /Reviewed by Hana Hygienist on /],
    ["NeedsReview (never confirmed)", "Needs review", /Listed, but nobody has confirmed the list is current/],
    ["NeedsReview (changed since)", "Needs review", /Changed since Hana Hygienist reviewed it on /],
    ["NoneKnown", "None known", /Reviewed by Hana Hygienist on .*: nothing known to report/],
    ["Unknown", "Unknown", /Marked unknown by Hana Hygienist on .*: it could not be established/],
    ["NotReviewed", "Not reviewed", /Nobody has reviewed this section yet/],
  ])("says %s in words, never by colour alone", async (scenario, word, statement) => {
    if (scenario === "Reviewed") { server.record.addItem(A, { kind: "Allergy", name: "Latex", createdAt: "2026-09-01T10:00:00Z" }); server.record.review(A, "Allergy", "Reviewed", "2026-09-15T10:00:00Z"); }
    if (scenario === "NeedsReview (never confirmed)") server.record.addItem(A, { kind: "Allergy", name: "Latex" });
    if (scenario === "NeedsReview (changed since)") { server.record.addItem(A, { kind: "Allergy", name: "Latex", updatedAt: "2026-09-20T10:00:00Z" }); server.record.review(A, "Allergy", "Reviewed", "2026-09-15T10:00:00Z"); }
    if (scenario === "NoneKnown") server.record.review(A, "Allergy", "NoneKnown");
    if (scenario === "Unknown") server.record.review(A, "Allergy", "Unknown");
    await openRecord();
    const allergies = card("Allergies");
    expect(within(allergies).getByText(word, { selector: "span" })).toBeInTheDocument();
    expect(within(allergies).getByText(statement)).toBeInTheDocument();
  });

  it("says all four sections are not reviewed for a patient with nothing on the chart, and invents no item", async () => {
    await openRecord();
    for (const name of ["Medical history", "Dental history", "Allergies", "Medications"]) {
      expect(within(card(name)).getByText("Not reviewed", { selector: "span" })).toBeInTheDocument();
      expect(within(card(name)).queryAllByRole("listitem")).toHaveLength(0);
    }
  });

  it("opens an item's history with every version, who made it, when and why", async () => {
    const user = userEvent.setup();
    const item = server.record.addItem(A, { kind: "Allergy", name: "Penicillin" });
    item.versions.push({ versionNumber: 2, changeType: "StatusChanged", name: "Penicillin", detail: null, reaction: null, severity: null, dose: null, frequency: null, status: "Resolved", reason: "Tolerated a challenge dose", encounterId: null, actorName: "Hana Hygienist", occurredAtUtc: "2026-09-12T09:00:00Z" });
    await openRecord();

    await user.click(within(card("Allergies")).getByText("History"));
    expect(await within(card("Allergies")).findByText("Added")).toBeInTheDocument();
    expect(within(card("Allergies")).getByText("Status changed")).toBeInTheDocument();
    expect(within(card("Allergies")).getByText(/Reason: Tolerated a challenge dose/)).toBeInTheDocument();
    expect(within(card("Allergies")).getByText(/by Hana Hygienist/)).toBeInTheDocument();
  });
});

// ---------- changing the record ----------

describe("changing the record", () => {
  it("adds an item, saves it, and says so in the status line", async () => {
    const user = userEvent.setup();
    await openRecord();
    await user.click(within(card("Allergies")).getByRole("button", { name: "Add allergy to the record: Allergies" }));
    await user.type(within(card("Allergies")).getByLabelText("Name"), "Latex");
    await user.type(within(card("Allergies")).getByLabelText("Reaction"), "Rash");
    await user.selectOptions(within(card("Allergies")).getByLabelText("Severity"), "Mild");
    await user.click(within(card("Allergies")).getByRole("button", { name: "Add allergy" }));

    expect(await within(card("Allergies")).findByText("Latex")).toBeInTheDocument();
    expect(screen.getByText("Allergies item saved.")).toBeInTheDocument();
    expect(within(card("Allergies")).getByText("Needs review", { selector: "span" })).toBeInTheDocument(); // listed, but nobody confirmed it yet
    expect(server.record.items.size).toBe(1);
  });

  it("keeps what was typed and shows the server's message when a save is refused", async () => {
    const user = userEvent.setup();
    server.record.addItem(A, { kind: "Medication", name: "Aspirin" });
    await openRecord();
    await user.click(within(card("Medications")).getByRole("button", { name: "Add medication to the record: Medications" }));
    await user.type(within(card("Medications")).getByLabelText("Name"), "Aspirin");
    await user.type(within(card("Medications")).getByLabelText("Dose"), "81 mg");
    await user.click(within(card("Medications")).getByRole("button", { name: "Add medication" }));

    expect(await screen.findByText(/Not saved: .*already listed/)).toBeInTheDocument();
    expect(within(card("Medications")).getByLabelText("Name")).toHaveValue("Aspirin");
    expect(within(card("Medications")).getByLabelText("Dose")).toHaveValue("81 mg");
    expect(server.record.items.size).toBe(1);
  });

  it("keeps what was typed after a dropped connection, and sending it again changes nothing more", async () => {
    const user = userEvent.setup();
    await openRecord();
    await user.click(within(card("Allergies")).getByRole("button", { name: "Add allergy to the record: Allergies" }));
    await user.type(within(card("Allergies")).getByLabelText("Name"), "Latex");
    server.dropNextResponse("POST /items");
    await user.click(within(card("Allergies")).getByRole("button", { name: "Add allergy" }));
    expect(await screen.findByText(/the connection dropped/)).toBeInTheDocument();
    expect(within(card("Allergies")).getByLabelText("Name")).toHaveValue("Latex");
    expect(server.record.items.size).toBe(1); // stored; only the answer was lost

    await user.click(within(card("Allergies")).getByRole("button", { name: "Add allergy" }));
    await waitFor(() => expect(within(card("Allergies")).queryByLabelText("Name")).not.toBeInTheDocument());
    expect(server.record.items.size).toBe(1); // the retry was recognised as the same item
  });

  it("changes an item's status with an optional reason and shows the new status in words", async () => {
    const user = userEvent.setup();
    server.record.addItem(A, { kind: "Allergy", name: "Penicillin" });
    await openRecord();
    await user.click(within(card("Allergies")).getByRole("button", { name: "Change status of Penicillin" }));
    await user.selectOptions(within(card("Allergies")).getByLabelText("Status of Penicillin"), "Resolved");
    await user.type(within(card("Allergies")).getByLabelText("Reason (optional)"), "Tolerated a challenge dose");
    await user.click(within(card("Allergies")).getByRole("button", { name: "Save status" }));

    expect(await within(card("Allergies")).findByText("Resolved")).toBeInTheDocument();
    expect(screen.getByText("Penicillin is now resolved.")).toBeInTheDocument();
    const call = server.callsToClinical("POST", "/status")[0];
    expect(call.body).toMatchObject({ status: "Resolved", reason: "Tolerated a challenge dose", rowVersion: "i1" });
  });

  it("offers only the statuses that fit the kind: a medication can be discontinued, an allergy cannot", async () => {
    const user = userEvent.setup();
    server.record.addItem(A, { kind: "Medication", name: "Lisinopril" });
    server.record.addItem(A, { kind: "Allergy", name: "Latex" });
    await openRecord();
    await user.click(within(card("Medications")).getByRole("button", { name: "Change status of Lisinopril" }));
    expect(within(within(card("Medications")).getByLabelText("Status of Lisinopril")).getAllByRole("option").map((o) => o.textContent)).toEqual(["Active", "Inactive", "Discontinued"]);
    await user.click(within(card("Allergies")).getByRole("button", { name: "Change status of Latex" }));
    expect(within(within(card("Allergies")).getByLabelText("Status of Latex")).getAllByRole("option").map((o) => o.textContent)).toEqual(["Active", "Inactive", "Resolved"]);
  });

  it("corrects an item's details and the history keeps what it said before", async () => {
    const user = userEvent.setup();
    const item = server.record.addItem(A, { kind: "Allergy", name: "Penicilin", reaction: "Rash" });
    await openRecord();
    await user.click(within(card("Allergies")).getByRole("button", { name: "Correct details of Penicilin" }));
    const name = within(card("Allergies")).getByLabelText("Name");
    await user.clear(name);
    await user.type(name, "Penicillin");
    await user.click(within(card("Allergies")).getByRole("button", { name: "Save changes" }));

    expect(await within(card("Allergies")).findByText("Penicillin")).toBeInTheDocument();
    expect(item.versions.map((v) => [v.changeType, v.name])).toEqual([["Added", "Penicilin"], ["Changed", "Penicillin"]]);
  });

  it("will not remove an item as entered in error without a reason, then keeps it in its history", async () => {
    const user = userEvent.setup();
    const item = server.record.addItem(A, { kind: "Allergy", name: "Latex" });
    await openRecord();
    await user.click(within(card("Allergies")).getByRole("button", { name: "Entered in error: Latex" }));
    await user.click(within(card("Allergies")).getByRole("button", { name: "Remove as entered in error" }));
    expect(within(card("Allergies")).getByText("Say why.")).toBeInTheDocument();
    expect(server.callsToClinical("POST", "/remove")).toHaveLength(0);

    await user.type(within(card("Allergies")).getByLabelText("Why was it entered in error?"), "Wrong patient");
    await user.click(within(card("Allergies")).getByRole("button", { name: "Remove as entered in error" }));
    await waitFor(() => expect(within(card("Allergies")).queryByText("Latex")).not.toBeInTheDocument());
    expect(item.removed).toBe(true);
    expect(item.versions.at(-1)).toMatchObject({ changeType: "RemovedInError", reason: "Wrong patient" });
  });
});

// ---------- statements about a section ----------

describe("none known, unknown and confirmed", () => {
  it("states 'none known' and 'unknown' without creating an item, and withdraws a statement", async () => {
    const user = userEvent.setup();
    await openRecord();
    await user.click(within(card("Allergies")).getByRole("button", { name: "None known: Allergies" }));
    expect(await within(card("Allergies")).findByText(/nothing known to report/)).toBeInTheDocument();
    await user.click(within(card("Medications")).getByRole("button", { name: "Unknown: Medications" }));
    expect(await within(card("Medications")).findByText(/it could not be established/)).toBeInTheDocument();
    expect(server.record.items.size).toBe(0); // a statement is not an invented fact

    await user.click(within(card("Allergies")).getByRole("button", { name: "Withdraw the statement: Allergies" }));
    expect(await within(card("Allergies")).findByText(/Nobody has reviewed this section yet/)).toBeInTheDocument();
    expect(screen.getByText("Allergies statement withdrawn.")).toBeInTheDocument();
  });

  it("offers 'none known' and 'unknown' only for an empty section and 'confirm' only for a listed one", async () => {
    server.record.addItem(A, { kind: "Allergy", name: "Latex" });
    await openRecord();
    expect(within(card("Allergies")).queryByRole("button", { name: /None known|Unknown/ })).not.toBeInTheDocument();
    expect(within(card("Allergies")).getByRole("button", { name: "Confirm the allergies list is current" })).toBeEnabled();
    expect(within(card("Medications")).queryByRole("button", { name: /Confirm/ })).not.toBeInTheDocument();
    expect(within(card("Medications")).getByRole("button", { name: "None known: Medications" })).toBeInTheDocument();
  });

  it("confirming a list says who reviewed it and when, and the button is then disabled", async () => {
    const user = userEvent.setup();
    server.record.addItem(A, { kind: "Allergy", name: "Latex" });
    await openRecord();
    await user.click(within(card("Allergies")).getByRole("button", { name: "Confirm the allergies list is current" }));
    expect(await within(card("Allergies")).findByText(/Reviewed by Dr\. Okafor on /)).toBeInTheDocument();
    expect(within(card("Allergies")).getByText("Reviewed", { selector: "span" })).toBeInTheDocument();
    expect(within(card("Allergies")).getByRole("button", { name: "Confirm the allergies list is current" })).toBeDisabled();
  });

  it("shows the server's refusal when a section changed meanwhile (an item appeared under 'none known')", async () => {
    const user = userEvent.setup();
    await openRecord();
    server.record.addItem(A, { kind: "Allergy", name: "Latex" }); // someone else lists an allergy behind this screen's back
    await user.click(within(card("Allergies")).getByRole("button", { name: "None known: Allergies" }));
    expect(await screen.findByText(/Not saved: .*already has items/)).toBeInTheDocument();
    expect(server.record.reviews.size).toBe(0);
  });
});

// ---------- failure paths and roles ----------

describe("failure paths", () => {
  it("shows the shared conflict banner for a stale edit and reloads in place, so an open form keeps what was typed", async () => {
    const user = userEvent.setup();
    const item = server.record.addItem(A, { kind: "Medication", name: "Lisinopril", dose: "10 mg" });
    await openRecord();
    await user.click(within(card("Medications")).getByRole("button", { name: "Correct details of Lisinopril" }));
    const dose = within(card("Medications")).getByLabelText("Dose");
    await user.clear(dose);
    await user.type(dose, "20 mg");
    item.v++; // another clinician changed it first
    await user.click(within(card("Medications")).getByRole("button", { name: "Save changes" }));

    expect(await screen.findByText("Someone else changed this while you were editing")).toBeInTheDocument();
    expect(screen.getByText("Not saved: someone else changed this record.")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /Reload/ }));
    await waitFor(() => expect(screen.queryByText("Someone else changed this while you were editing")).not.toBeInTheDocument());
    expect(within(card("Medications")).getByLabelText("Dose")).toHaveValue("20 mg"); // the form stayed mounted
    await user.click(within(card("Medications")).getByRole("button", { name: "Save changes" }));
    expect(await within(card("Medications")).findByText(/Dose: 20 mg/)).toBeInTheDocument();
  });

  it("says the record could not be loaded without hiding the encounters", async () => {
    server.addEncounter(A, { id: "e1" });
    server.failClinical(`GET /api/patients/${A}/clinical-record`, json(500, { error: "server_error", message: "boom" }));
    open(`/patients/${A}/clinical`);
    expect(await screen.findByText("Could not load the clinical record")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /Encounter on/ })).toBeInTheDocument();
  });

  it("gives a read-only role the record, its history and no way to change anything", async () => {
    server.permissions = READ_ONLY;
    server.record.addItem(A, { kind: "Allergy", name: "Penicillin" });
    await openRecord();
    expect(within(card("Allergies")).getByText("Penicillin")).toBeInTheDocument();
    expect(screen.getByText(/your role cannot change it/)).toBeInTheDocument();
    expect(within(card("Allergies")).getByText("History")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^(Add .* to the record|Confirm|None known|Unknown|Correct|Change status|Entered in error|Withdraw)/ })).not.toBeInTheDocument();
  });
});

// ---------- note templates ----------

describe("note templates", () => {
  const templatesPath = `/patients/${A}/clinical/templates`;
  beforeEach(() => { server.permissions = ["ViewPatientRecords", "ViewClinicalDocumentation", "ManageClinicalNotes", "ManageClinicalTemplates"]; });

  it("lists templates with their sections and which are required, for a clinician who can configure them", async () => {
    server.record.addTemplate({ name: "SOAP note", description: "Standard visit" });
    server.record.addTemplate({ name: "Old form", isActive: false });
    open(templatesPath);
    expect(await screen.findByRole("heading", { name: "Note templates" })).toBeInTheDocument();
    expect(screen.getByText("SOAP note")).toBeInTheDocument();
    expect(screen.getByText(/Subjective \(required\), Plan \(required\) · Standard visit/)).toBeInTheDocument();
    expect(screen.getByText("In use")).toBeInTheDocument();
    expect(screen.getByText("Out of use")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "New template" })).toBeInTheDocument();
  });

  it("creates a template with a required section and starter text", async () => {
    const user = userEvent.setup();
    open(templatesPath);
    await user.click(await screen.findByRole("button", { name: "New template" }));
    await user.type(screen.getByLabelText("Template name"), "Hygiene visit");
    await user.click(screen.getByLabelText("Include Progress note"));
    await user.click(screen.getByLabelText("Progress note is required before signing"));
    await user.type(screen.getByLabelText("Starter text for Progress note"), "Calculus:");
    await user.click(screen.getByRole("button", { name: "Create template" }));

    expect(await screen.findByText("Template Hygiene visit created.")).toBeInTheDocument();
    expect(screen.getByText(/Progress note \(required\)/)).toBeInTheDocument();
    const t = [...server.record.templates.values()][0];
    expect(t.sections).toEqual([{ section: "Progress", required: true, starterText: "Calculus:" }]);
  });

  it("asks for a name and at least one section before sending anything", async () => {
    const user = userEvent.setup();
    open(templatesPath);
    await user.click(await screen.findByRole("button", { name: "New template" }));
    await user.click(screen.getByRole("button", { name: "Create template" }));
    expect(screen.getByText("A name is required.")).toBeInTheDocument();
    expect(screen.getByText("Choose at least one note section.")).toBeInTheDocument();
    expect(server.callsToClinical("POST", "/templates")).toHaveLength(0);
  });

  it("keeps the typed form and says why when the name is already used", async () => {
    const user = userEvent.setup();
    server.record.addTemplate({ name: "SOAP note" });
    open(templatesPath);
    await user.click(await screen.findByRole("button", { name: "New template" }));
    await user.type(screen.getByLabelText("Template name"), "soap NOTE");
    await user.click(screen.getByLabelText("Include Plan"));
    await user.click(screen.getByRole("button", { name: "Create template" }));
    expect(await screen.findByText("That name is already used by another template.")).toBeInTheDocument();
    expect(screen.getByLabelText("Template name")).toHaveValue("soap NOTE");
    expect(server.record.templates.size).toBe(1);
  });

  it("takes a template out of use and back, and shows a stale edit as the conflict banner", async () => {
    const user = userEvent.setup();
    const t = server.record.addTemplate({ name: "SOAP note" });
    open(templatesPath);
    await user.click(await screen.findByRole("button", { name: "Take out of use: SOAP note" }));
    expect(await screen.findByText("Out of use")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Put back in use: SOAP note" }));
    expect(await screen.findByText("In use")).toBeInTheDocument();

    t.v++; // someone else edited it
    await user.click(screen.getByRole("button", { name: "Change template SOAP note" }));
    await user.type(screen.getByLabelText("Template name"), " v2");
    await user.click(screen.getByRole("button", { name: "Save template" }));
    expect(await screen.findByText("Someone else changed this while you were editing")).toBeInTheDocument();
    expect(screen.getByLabelText("Template name")).toHaveValue("SOAP note v2"); // typed values stay
  });

  it("shows a role that cannot configure templates the list without any controls", async () => {
    server.permissions = READ_ONLY;
    server.record.addTemplate({ name: "SOAP note" });
    server.record.addTemplate({ name: "Hidden", isActive: false });
    open(templatesPath);
    expect(await screen.findByText("SOAP note")).toBeInTheDocument();
    expect(screen.queryByText("Hidden")).not.toBeInTheDocument(); // taken-out-of-use templates are the configurator's to see
    expect(screen.getByText(/your role cannot change them/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /New template|Change template|Take out of use/ })).not.toBeInTheDocument();
  });

  it("has no accessibility violations on the list and on the template form", async () => {
    const user = userEvent.setup();
    server.record.addTemplate({ name: "SOAP note" });
    const { container } = open(templatesPath);
    await screen.findByText("SOAP note");
    expect(await axe(container)).toHaveNoViolations();
    await user.click(screen.getByRole("button", { name: "New template" }));
    await user.click(screen.getByLabelText("Include Plan"));
    expect(await axe(container)).toHaveNoViolations();
  });
});

describe("accessibility (axe, jsdom)", () => {
  it("has no violations on the clinical record with items, statements, and open forms", async () => {
    const user = userEvent.setup();
    server.record.addItem(A, { kind: "Allergy", name: "Penicillin", status: "Resolved" });
    server.record.addItem(A, { kind: "Medication", name: "Lisinopril", dose: "10 mg" });
    server.record.review(A, "DentalHistory", "NoneKnown");
    server.record.review(A, "MedicalHistory", "Unknown");
    const { container } = await openRecord();
    expect(await axe(container)).toHaveNoViolations();
    await user.click(within(card("Allergies")).getByRole("button", { name: "Change status of Penicillin" }));
    await user.click(within(card("Medications")).getByRole("button", { name: "Entered in error: Lisinopril" }));
    await user.click(within(card("Medications")).getByText("History"));
    expect(await axe(container)).toHaveNoViolations();
  });
});
