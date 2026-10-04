import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { App } from "./App";
import { FakeClinicalServer } from "./test/fakeClinicalServer";
import { json, makePatient } from "./test/fakePatientServer";

/**
 * ALV-N011: the patient-safety strip (patient header and open encounter) and the Safety tab, through the real <App /> against an in-memory fake of the safety API. The rules are
 * proven by the backend tests and the real-backend walkthrough; what is proven here is the UI's behaviour for each outcome: safety information in view before treatment, what is NOT
 * established said as such (nothing invented), acknowledged shown as different from resolved, a reason before anything is resolved, a source before anything is stated, the clearance
 * workflow with its missing-document state, and every failure path - a stale or refused change, a dropped connection, a failed load and a role that may only read.
 */
const A = "aaaaaaaa-0000-0000-0000-000000000001";
const READ_ONLY = ["ViewPatientRecords", "ViewClinicalDocumentation"];
let server: FakeClinicalServer;

function open(path: string) {
  window.history.pushState({}, "", path);
  return render(<App />);
}
async function openSafety() {
  const view = open(`/patients/${A}/safety`);
  await screen.findByRole("heading", { name: "Patient safety details" });
  return view;
}
const strip = () => screen.getByRole("region", { name: "Patient safety" });
const status = () => screen.getAllByRole("status").find((el) => el.className.includes("alv-clinical__status"))!;
const entry = (title: string) => screen.getByText(title, { selector: "p.alv-safety__entry-title" }).closest("li")!;

beforeEach(() => {
  server = new FakeClinicalServer();
  server.add(makePatient({ id: A, firstName: "Ann", lastName: "Lee" }));
  server.install();
});
afterEach(() => vi.unstubAllGlobals());

// ---------- the strip ----------

describe("the patient safety strip", () => {
  it("says nothing is recorded - and what is not established - for a patient with nothing on file, and invents no alert", async () => {
    open(`/patients/${A}`);
    expect(await screen.findByText("No alerts, active allergies or current medications are recorded.")).toBeInTheDocument();
    expect(within(strip()).getAllByText(/Nothing listed here does not mean none\./)).toHaveLength(3);
    expect(within(strip()).getByText("Allergies have not been reviewed. Nothing listed here does not mean none.")).toBeInTheDocument();
    expect(within(strip()).queryByText(/active alert/)).not.toBeInTheDocument();
    expect(server.safety.alerts.size).toBe(0);
  });

  it("shows counts and the highest severity in words, what you have not acknowledged and what needs review, and links to the details", async () => {
    server.safety.addAlert(A, { title: "Anticoagulant therapy", category: "Anticoagulant", severity: "Critical" });
    server.safety.addAlert(A, { title: "Prosthetic heart valve", severity: "High", attention: "The clinical-record item this alert was based on is now resolved. Review whether the alert still applies." });
    server.safety.addRecordEntry(A, { title: "Penicillin", category: "Allergy", severity: "High" });
    server.safety.addRecordEntry(A, { title: "Lisinopril", category: "Medication" });
    server.safety.addClearance(A, {});
    server.safety.establish(A, "Allergy", "Medication", "MedicalHistory");
    open(`/patients/${A}`);

    expect(await screen.findByText(/2 active alerts \(highest: Critical\) · 1 active allergy · 1 current medication · 1 clearance open/)).toBeInTheDocument();
    expect(within(strip()).getByText("2 alerts you have not acknowledged yet.")).toBeInTheDocument();
    expect(within(strip()).getByText("1 item needs review.")).toBeInTheDocument();
    expect(within(strip()).queryByText(/not been reviewed/)).not.toBeInTheDocument();       // established sections produce no gap
    expect(within(strip()).getByRole("link", { name: "Open safety details" })).toHaveAttribute("href", `/patients/${A}/safety`);
  });

  it("is also at the top of an open encounter, under its own name, before anything is documented", async () => {
    server.safety.addAlert(A, { title: "Pregnant", category: "Pregnancy", severity: "High" });
    server.addEncounter(A, { id: "e1" });
    open(`/patients/${A}/clinical/e1`);
    await screen.findByRole("heading", { name: /^Encounter on/ });
    const own = await screen.findByRole("region", { name: "Patient safety for this encounter" });
    expect(within(own).getByText(/1 active alert \(highest: High\)/)).toBeInTheDocument();
    expect(screen.getAllByRole("region", { name: /Patient safety/ })).toHaveLength(2);        // the header's and the encounter's, told apart
  });

  it("says it could not load - and not to assume there is none - then recovers when tried again", async () => {
    const user = userEvent.setup();
    server.failClinical(`GET /api/patients/${A}/safety`, json(500, { error: "server_error", message: "boom" }));
    open(`/patients/${A}`);
    expect(await screen.findByText(/could not be loaded, so do not assume there is none/)).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Try again" }));
    expect(await screen.findByText("No alerts, active allergies or current medications are recorded.")).toBeInTheDocument();
  });

  it("is not drawn, and nothing is requested, for a role that may not read clinical documentation", async () => {
    server.permissions = ["ViewPatientRecords", "RegisterPatients", "EditPatients"];
    open(`/patients/${A}`);
    await screen.findByText("Ann Lee", { selector: ".alv-patient-header__name" });
    expect(screen.queryByRole("region", { name: "Patient safety" })).not.toBeInTheDocument();
    expect(server.callsToClinical("GET", "/safety")).toHaveLength(0);
  });
});

// ---------- the safety details ----------

describe("the safety details", () => {
  it("lists active entries most urgent first with severity, kind, source and who last updated them; record entries are read-only and point to the record", async () => {
    server.safety.addAlert(A, { title: "Needs premedication", category: "Custom", severity: "Low", source: "Dr. Okafor's note" });
    server.safety.addAlert(A, { title: "Anticoagulant therapy", category: "Anticoagulant", severity: "Critical", detail: "Warfarin, INR 2.5" });
    server.safety.addRecordEntry(A, { title: "Penicillin", category: "Allergy", severity: "High", detail: "Hives" });
    server.safety.addRecordEntry(A, { title: "Latex", category: "Allergy", needsAttention: true, attentionReason: "Severity is not recorded; it is not assumed." });
    server.safety.addRecordEntry(A, { title: "Lisinopril", category: "Medication" });
    await openSafety();
    expect(entry("Lisinopril").textContent).not.toContain("Severity");                      // a medication has no severity: nothing is said about one

    const titles = screen.getAllByText(/./, { selector: "p.alv-safety__entry-title" }).map((p) => p.textContent!.replace(/\s+/g, " ").trim());
    expect(titles[0]).toMatch(/^Anticoagulant therapy Critical Anticoagulant Active$/);
    expect(titles[1]).toMatch(/^Penicillin High Allergy$/);
    expect(titles.find((t) => t.startsWith("Latex"))).toMatch(/^Latex Severity not recorded Allergy$/);   // never guessed
    expect(within(entry("Anticoagulant therapy")).getByText(/Source: Reported by the patient at intake · Last updated .* by Dr\. Okafor/)).toBeInTheDocument();
    expect(within(entry("Anticoagulant therapy")).getByText("Warfarin, INR 2.5")).toBeInTheDocument();
    expect(within(entry("Penicillin")).getByText(/Source: Clinical record: allergies/)).toBeInTheDocument();
    expect(within(entry("Penicillin")).getByRole("link", { name: "Change it in the clinical record" })).toHaveAttribute("href", `/patients/${A}/clinical`);
    expect(within(entry("Penicillin")).queryByRole("button")).not.toBeInTheDocument();      // one source of truth: not changed here
    expect(within(entry("Latex")).getByText(/Needs review: Severity is not recorded/)).toBeInTheDocument();
  });

  it("says plainly when nothing is recorded, as a statement of what is recorded and not of what is true", async () => {
    await openSafety();
    expect(screen.getByText(/No alerts, active allergies or current medications are recorded\. This is not a statement that there are none\./)).toBeInTheDocument();
    expect(screen.getByText("Not established")).toBeInTheDocument();
    expect(screen.getByText("No clearances have been requested.")).toBeInTheDocument();
  });

  it("acknowledging records that you saw the alert, says it is STILL ACTIVE, and the strip's unacknowledged count follows", async () => {
    const user = userEvent.setup();
    server.safety.addAlert(A, { title: "Anticoagulant therapy", category: "Anticoagulant", severity: "Critical" });
    await openSafety();
    expect(within(strip()).getByText("1 alert you have not acknowledged yet.")).toBeInTheDocument();
    expect(within(entry("Anticoagulant therapy")).getByText("Records that you have seen this. It does not resolve it.")).toBeInTheDocument();

    await user.click(within(entry("Anticoagulant therapy")).getByRole("button", { name: "Acknowledge: Anticoagulant therapy" }));
    expect(await within(entry("Anticoagulant therapy")).findByText(/You acknowledged this on .*It is still active - acknowledging does not resolve it\./)).toBeInTheDocument();
    expect(within(entry("Anticoagulant therapy")).getByText("Active", { selector: "span" })).toBeInTheDocument();
    expect(within(entry("Anticoagulant therapy")).queryByText("Resolved", { selector: "span" })).not.toBeInTheDocument();
    expect(server.callsToClinical("POST", "/acknowledge")[0].body).toEqual({ revision: 1 });
    await waitFor(() => expect(within(strip()).queryByText(/you have not acknowledged/)).not.toBeInTheDocument()); // the header re-read
    expect(server.safety.alerts.get([...server.safety.alerts.keys()][0])!.status).toBe("Active");
  });

  it("refuses to acknowledge a revision that changed since it was opened, says so, and keeps the button", async () => {
    const user = userEvent.setup();
    const a = server.safety.addAlert(A, { title: "Prosthetic heart valve" });
    await openSafety();
    a.revision = 2; // another clinician changed it
    await user.click(within(entry("Prosthetic heart valve")).getByRole("button", { name: "Acknowledge: Prosthetic heart valve" }));
    expect(await screen.findByText(/Not saved: This alert changed since you opened it/)).toBeInTheDocument();
    expect(within(entry("Prosthetic heart valve")).getByRole("button", { name: "Acknowledge: Prosthetic heart valve" })).toBeInTheDocument();
    expect(a.acked).toBe(false);
  });

  it("will not resolve without a reason, then resolves it into the resolved list with who and why, and it can be reopened only with a reason", async () => {
    const user = userEvent.setup();
    server.safety.addAlert(A, { title: "Prosthetic heart valve" });
    await openSafety();
    await user.click(within(entry("Prosthetic heart valve")).getByRole("button", { name: "Resolve alert: Prosthetic heart valve" }));
    await user.click(screen.getByRole("button", { name: "Resolve alert" }));
    expect(screen.getByText("Say why.")).toBeInTheDocument();
    expect(server.callsToClinical("POST", "/resolve")).toHaveLength(0);

    await user.type(screen.getByLabelText(/Why is "Prosthetic heart valve" resolved\?/), "Entered on the wrong chart");
    await user.click(screen.getByRole("button", { name: "Resolve alert" }));
    expect(await screen.findByText("Resolved alerts (1)")).toBeInTheDocument();
    expect(screen.getByText("No alerts, active allergies or current medications are recorded. This is not a statement that there are none.", { exact: false })).toBeInTheDocument();
    await user.click(screen.getByText("Resolved alerts (1)"));
    expect(screen.getByText(/Resolved .* by Dr\. Okafor: Entered on the wrong chart/)).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Reopen alert: Prosthetic heart valve" }));
    await user.click(screen.getByRole("button", { name: "Reopen alert" }));
    expect(screen.getByText("Say why.")).toBeInTheDocument();
    await user.type(screen.getByLabelText(/Why is "Prosthetic heart valve" being reopened\?/), "It was correct after all");
    await user.click(screen.getByRole("button", { name: "Reopen alert" }));
    await waitFor(() => expect(screen.queryByText("Resolved alerts (1)")).not.toBeInTheDocument());
    expect(entry("Prosthetic heart valve")).toBeInTheDocument();
  });

  it("states an alert only with a source: a missing one is stopped on screen, and a refusal keeps every typed value", async () => {
    const user = userEvent.setup();
    server.safety.addAlert(A, { title: "Pregnant", category: "Pregnancy" });
    await openSafety();
    await user.click(screen.getByRole("button", { name: "Add an alert" }));
    await user.type(screen.getByLabelText("Title"), "Asthma");
    await user.selectOptions(screen.getByLabelText("Severity"), "Moderate");
    await user.click(screen.getByRole("button", { name: "Add alert" }));
    expect(screen.getByText("Say where this information came from.")).toBeInTheDocument();
    expect(server.callsToClinical("POST", "/alerts")).toHaveLength(0);

    await user.type(screen.getByLabelText("Source of this information"), "Reported by the patient");
    await user.click(screen.getByRole("button", { name: "Add alert" }));
    expect(await screen.findByText("Asthma", { selector: "p.alv-safety__entry-title" })).toBeInTheDocument();
    expect(server.safety.alerts.size).toBe(2);
    expect(server.callsToClinical("POST", "/alerts")[0].body).toMatchObject({ category: "Condition", title: "Asthma", severity: "Moderate", sourceNote: "Reported by the patient" });
  });

  it("keeps what was typed and says why when an alert already exists, and after a dropped connection sending again adds it once", async () => {
    const user = userEvent.setup();
    server.safety.addAlert(A, { title: "Pregnant", category: "Condition" });
    await openSafety();
    await user.click(screen.getByRole("button", { name: "Add an alert" }));
    await user.type(screen.getByLabelText("Title"), "pregnant");
    await user.selectOptions(screen.getByLabelText("Severity"), "High");
    await user.type(screen.getByLabelText("Source of this information"), "Intake form");
    await user.click(screen.getByRole("button", { name: "Add alert" }));
    expect(await screen.findByText(/Not saved: There is already an active alert named 'Pregnant'/)).toBeInTheDocument();
    expect(screen.getByLabelText("Title")).toHaveValue("pregnant");

    await user.clear(screen.getByLabelText("Title"));
    await user.type(screen.getByLabelText("Title"), "Latex sensitivity");
    server.dropNextResponse("POST /alerts");
    await user.click(screen.getByRole("button", { name: "Add alert" }));
    expect(await screen.findByText(/Not saved: the connection dropped/)).toBeInTheDocument();
    expect(screen.getByLabelText("Title")).toHaveValue("Latex sensitivity");
    expect(server.safety.alerts.size).toBe(2);                                               // stored; only the answer was lost
    await user.click(screen.getByRole("button", { name: "Add alert" }));
    await waitFor(() => expect(screen.queryByLabelText("Title")).not.toBeInTheDocument());
    expect(server.safety.alerts.size).toBe(2);                                               // the retry was recognised as the same alert
  });

  it("shows the conflict banner for a stale edit and reloads in place so the typed change survives", async () => {
    const user = userEvent.setup();
    const a = server.safety.addAlert(A, { title: "Prosthetic heart valve", detail: "Original" });
    await openSafety();
    await user.click(within(entry("Prosthetic heart valve")).getByRole("button", { name: "Change alert: Prosthetic heart valve" }));
    await user.clear(screen.getByLabelText("Details"));
    await user.type(screen.getByLabelText("Details"), "My change");
    a.v++; // another clinician saved first
    await user.click(screen.getByRole("button", { name: "Save changes" }));
    expect(await screen.findByText("Someone else changed this while you were editing")).toBeInTheDocument();
    expect(status()).toHaveTextContent("Not saved: someone else changed this.");
    await user.click(screen.getByRole("button", { name: /Reload/ }));
    await waitFor(() => expect(screen.queryByText("Someone else changed this while you were editing")).not.toBeInTheDocument());
    expect(screen.getByLabelText("Details")).toHaveValue("My change");
    await user.click(screen.getByRole("button", { name: "Save changes" }));
    expect(await screen.findByText("My change")).toBeInTheDocument();
  });

  it("opens an alert's history with who changed it, when and why", async () => {
    const user = userEvent.setup();
    server.safety.addAlert(A, { title: "Prosthetic heart valve" });
    await openSafety();
    await user.click(within(entry("Prosthetic heart valve")).getByText("History"));
    expect(await within(entry("Prosthetic heart valve")).findByText("Created")).toBeInTheDocument();
    expect(within(entry("Prosthetic heart valve")).getAllByText(/by Dr\. Okafor/).length).toBeGreaterThan(0);
  });

  it("says it could not load the safety information and not to assume there is none", async () => {
    server.failClinical(`GET /api/patients/${A}/safety`, json(500, { error: "server_error", message: "boom" }));
    server.failClinical(`GET /api/patients/${A}/safety`, json(500, { error: "server_error", message: "boom" }));
    open(`/patients/${A}/safety`);
    expect(await screen.findByText("Could not load the patient's safety information")).toBeInTheDocument();
    expect(screen.getByText(/Do not assume there is none/)).toBeInTheDocument();
  });
});

// ---------- clearances ----------

describe("clearances", () => {
  it("runs the workflow: requested (no resolve offered), received without its document (said in words), document attached later, resolved with a reason", async () => {
    const user = userEvent.setup();
    await openSafety();
    await user.click(screen.getByRole("button", { name: "Request a clearance" }));
    await user.click(screen.getByRole("button", { name: "Request clearance" }));
    expect(screen.getByText("Say why the clearance is needed.")).toBeInTheDocument();
    expect(server.callsToClinical("POST", "/clearances")).toHaveLength(0);
    await user.type(screen.getByLabelText("Why is it needed?"), "Cardiac clearance before extraction");
    await user.type(screen.getByLabelText("Requested from (optional)"), "Dr. Singh");
    await user.click(screen.getByRole("button", { name: "Request clearance" }));

    const label = "Medical clearance: Cardiac clearance before extraction";
    expect(await screen.findByText("Requested - waiting")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: `Resolve: ${label}` })).not.toBeInTheDocument();    // a clearance still waiting cannot be resolved
    expect(within(strip()).getByText(/1 clearance open/)).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: `Mark as received: ${label}` }));
    await user.click(screen.getByRole("button", { name: "Mark as received" }));
    expect(await screen.findByText("Received - not yet resolved")).toBeInTheDocument();             // received is NOT resolved
    expect(screen.getByText("Supporting document not yet attached. Attach the reference when it arrives.")).toBeInTheDocument();
    expect(within(strip()).getByText(/1 clearance open/)).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: `Attach document: ${label}` }));
    await user.type(screen.getByLabelText("Document reference"), "cardiac-letter.pdf");
    await user.click(screen.getByRole("button", { name: "Attach document" }));
    expect(await screen.findByText("Supporting document: cardiac-letter.pdf")).toBeInTheDocument();
    expect(screen.queryByText(/Supporting document not yet attached/)).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: `Resolve: ${label}` }));
    await user.click(screen.getByRole("button", { name: "Resolve clearance" }));
    expect(screen.getByText("Say why.")).toBeInTheDocument();
    await user.type(screen.getByLabelText("Why is this clearance resolved?"), "Cleared with epinephrine limits");
    await user.click(screen.getByRole("button", { name: "Resolve clearance" }));
    expect(await screen.findByText(/Resolved .* by Dr\. Okafor: Cleared with epinephrine limits/)).toBeInTheDocument();
    await waitFor(() => expect(within(strip()).queryByText(/clearance open/)).not.toBeInTheDocument());
  });

  it("cancels a clearance that is no longer needed only with a reason, and it stays listed as cancelled", async () => {
    const user = userEvent.setup();
    server.safety.addClearance(A, { kind: "Dental", reason: "Specialist opinion on the implant site" });
    await openSafety();
    const label = "Dental clearance: Specialist opinion on the implant site";
    await user.click(screen.getByRole("button", { name: `Cancel: ${label}` }));
    await user.click(screen.getByRole("button", { name: "Cancel clearance" }));
    expect(screen.getByText("Say why.")).toBeInTheDocument();
    await user.type(screen.getByLabelText("Why is this clearance no longer needed?"), "Patient chose another treatment");
    await user.click(screen.getByRole("button", { name: "Cancel clearance" }));
    expect(await screen.findByText(/Cancelled .* by Dr\. Okafor: Patient chose another treatment/)).toBeInTheDocument();
    expect(screen.getByText("Cancelled", { selector: "span" })).toBeInTheDocument();
  });

  it("shows the server's refusal when a clearance was resolved elsewhere first, and keeps the form open", async () => {
    const user = userEvent.setup();
    const c = server.safety.addClearance(A, { status: "Received", receivedAtUtc: "2026-09-03T10:00:00Z", receivedByName: "Dr. Okafor" });
    await openSafety();
    await user.click(screen.getByRole("button", { name: /^Resolve: / }));
    await user.type(screen.getByLabelText("Why is this clearance resolved?"), "Cleared");
    Object.assign(c, { status: "Resolved", closedAtUtc: "2026-10-03T14:00:00Z", closedByName: "Hana Hygienist", closingReason: "Cleared already" }); c.v++;
    await user.click(screen.getByRole("button", { name: "Resolve clearance" }));
    expect(await screen.findByText("Someone else changed this while you were editing")).toBeInTheDocument();
    expect(screen.getByLabelText("Why is this clearance resolved?")).toHaveValue("Cleared");
  });
});

// ---------- roles and accessibility ----------

describe("a role that may only read", () => {
  it("sees the safety picture and can acknowledge an alert, but has no way to add, change, resolve or run clearances", async () => {
    const user = userEvent.setup();
    server.permissions = READ_ONLY;
    server.safety.addAlert(A, { title: "Prosthetic heart valve" });
    server.safety.addClearance(A, {});
    await openSafety();
    expect(screen.getByText(/your role cannot change it/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Add an alert|Request a clearance|Change alert|Resolve|Reopen|Mark as received|Attach document|Cancel clearance/ })).not.toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Acknowledge: Prosthetic heart valve" }));
    expect(await within(entry("Prosthetic heart valve")).findByText(/You acknowledged this/)).toBeInTheDocument();
  });
});

describe("accessibility (axe, jsdom)", () => {
  it("has no violations on the strip, the safety details with entries, gaps and clearances, and every open form", async () => {
    const user = userEvent.setup();
    server.safety.addAlert(A, { title: "Anticoagulant therapy", category: "Anticoagulant", severity: "Critical" });
    server.safety.addAlert(A, { title: "Old", status: "Resolved", resolvedAt: "2026-09-02T10:00:00Z", resolvedBy: "Dr. Okafor", reason: "Entered in error" });
    server.safety.addRecordEntry(A, { title: "Penicillin", category: "Allergy", severity: "High" });
    server.safety.addClearance(A, {});
    server.safety.addClearance(A, { kind: "Dental", reason: "Implant", status: "Received", receivedAtUtc: "2026-09-03T10:00:00Z", receivedByName: "Dr. Okafor" });
    const { container } = await openSafety();
    expect(await axe(container)).toHaveNoViolations();

    await user.click(screen.getByRole("button", { name: "Add an alert" }));
    await user.click(screen.getByRole("button", { name: "Request a clearance" }));
    await user.click(within(entry("Anticoagulant therapy")).getByRole("button", { name: "Resolve alert: Anticoagulant therapy" }));
    await user.click(screen.getByRole("button", { name: /^Mark as received: Medical/ }));
    await user.click(screen.getByText("Resolved alerts (1)"));
    expect(await axe(container)).toHaveNoViolations();
  });
});
