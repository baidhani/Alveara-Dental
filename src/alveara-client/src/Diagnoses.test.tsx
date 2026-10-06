import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { App } from "./App";
import { FakeClinicalServer } from "./test/fakeClinicalServer";
import { json, makePatient } from "./test/fakePatientServer";

/**
 * STORY-013: the Diagnoses screen, through the real <App /> against the in-memory fake of the API. The rules are proven by the backend tests and the real-backend walkthrough; what is proven here is the
 * UI's behaviour for each outcome: a diagnosis recorded for an encounter with the tooth sent as its FDI key, the treatment-plan reference always shown as unresolved, entries checked before sending with
 * every problem listed and linked, the server's refusal keeping what was typed, corrections that keep the reference unless it is explicitly replaced or removed, withdrawal that keeps everything, a
 * stale change shown as a conflict, a dropped connection that makes one diagnosis, honest empty and failed states, and a role that may only read.
 */
const A = "aaaaaaaa-0000-0000-0000-000000000001";
const READ_ONLY = ["ViewPatientRecords", "ViewClinicalDocumentation"];
let server: FakeClinicalServer;
let encounter: string;

async function openTab() {
  window.history.pushState({}, "", `/patients/${A}/diagnoses`);
  const view = render(<App />);
  await screen.findByRole("heading", { name: "Diagnoses" });
  await screen.findByRole("heading", { name: "Diagnoses on record" });
  return view;
}
const form = () => screen.getByRole("form", { name: "Record a diagnosis" });
const status = () => screen.getAllByRole("status").find((el) => el.className.includes("alv-clinical__status"))!;
const posts = (suffix = "/diagnoses") => server.callsToClinical("POST", suffix);
const item = (label: string) => screen.getByRole("listitem", { name: `Diagnosis: ${label}` });

async function fill(user: ReturnType<typeof userEvent.setup>, over: { label?: string; tooth?: string; notes?: string; plan?: string } = {}) {
  const f = within(form());
  if (over.label !== undefined) await user.type(f.getByLabelText("Diagnosis"), over.label);
  if (over.tooth) await user.selectOptions(f.getByLabelText("Tooth (optional)"), over.tooth);
  if (over.notes) await user.type(f.getByLabelText(/^Notes/), over.notes);
  if (over.plan) await user.type(f.getByLabelText("Reference"), over.plan);
}
const submit = (user: ReturnType<typeof userEvent.setup>) => user.click(within(form()).getByRole("button", { name: "Record diagnosis" }));

beforeEach(() => {
  server = new FakeClinicalServer();
  server.add(makePatient({ id: A, firstName: "Ann", lastName: "Lee" }));
  server.install();
  encounter = server.addEncounter(A).id;
});
afterEach(() => vi.unstubAllGlobals());

describe("what is on the screen before anything is recorded", () => {
  it("says none has been recorded, and never that nothing is wrong", async () => {
    await openTab();
    expect(screen.getByText("No diagnosis has been recorded for this patient. That means none has been recorded, not that nothing is wrong.")).toBeInTheDocument();
    expect(status()).toHaveTextContent("Nothing is saved until you record a diagnosis.");
  });

  it("without an encounter there is no form, and it says why", async () => {
    server.encounters.clear();
    await openTab();
    expect(screen.queryByRole("form", { name: "Record a diagnosis" })).not.toBeInTheDocument();
    expect(screen.getByRole("note")).toHaveTextContent("This patient has no encounter yet, and a diagnosis is recorded for an encounter. Start an encounter in the Clinical tab first.");
  });

  it("a failed load says it could not load rather than showing an empty list", async () => {
    server.failClinical(`GET /api/patients/${A}/diagnoses`, json(500, { error: "server_error", message: "boom" }));
    window.history.pushState({}, "", `/patients/${A}/diagnoses`);
    render(<App />);
    expect(await screen.findByText("Could not load the diagnoses")).toBeInTheDocument();
    expect(screen.queryByText(/No diagnosis has been recorded/)).not.toBeInTheDocument();
  });
});

describe("recording a diagnosis (acceptance 1)", () => {
  it("saves it for the chosen encounter, sends the tooth as its FDI key whatever number is shown, and lists it with the reference as unresolved", async () => {
    const user = userEvent.setup();
    await openTab();
    await fill(user, { label: "Caries on the occlusal surface", tooth: "16", notes: "Sensitive to cold.", plan: "plan-2026-07" });
    await submit(user);

    await waitFor(() => expect(status()).toHaveTextContent("Diagnosis recorded."));
    const [call] = posts();
    expect(call.body).toMatchObject({ encounterId: encounter, label: "Caries on the occlusal surface", toothKey: "16", notes: "Sensitive to cold.", treatmentPlanReference: "plan-2026-07" });   // Universal 3 is FDI 16
    expect(String((call.body as { idempotencyKey: string }).idempotencyKey).length).toBeGreaterThan(8);
    const li = item("Caries on the occlusal surface");
    expect(li).toHaveTextContent("Tooth 3 (upper right first molar)");
    expect(li).toHaveTextContent("recorded by Dr. Okafor");
    expect(li).toHaveTextContent("Treatment plan reference (unresolved): plan-2026-07");
    expect(li).toHaveTextContent("it does not mean a treatment plan with this reference exists");
    expect(within(form()).getByLabelText("Diagnosis")).toHaveValue("");                              // the form is ready for the next one
  });

  it("a diagnosis with no plan reference says so and shows nothing about a plan", async () => {
    server.diagnoses.add(A, encounter, { label: "Gingivitis" });
    await openTab();
    expect(item("Gingivitis")).toHaveTextContent("No treatment plan reference.");
    expect(item("Gingivitis")).not.toHaveTextContent("unresolved");
  });

  it("sends an empty tooth, notes and reference box as not supplied", async () => {
    const user = userEvent.setup();
    await openTab();
    await fill(user, { label: "Gingivitis" });
    await submit(user);
    await waitFor(() => expect(posts()).toHaveLength(1));
    expect(posts()[0].body).toMatchObject({ toothKey: null, notes: null, treatmentPlanReference: null });
  });
});

describe("incorrect data (acceptance 2)", () => {
  it("sends nothing while an entry is wrong, lists every problem with a link to its box, and moves focus to the list", async () => {
    const user = userEvent.setup();
    await openTab();
    await fill(user, { plan: "x" });
    await user.clear(within(form()).getByLabelText("Reference"));
    await user.type(within(form()).getByLabelText("Reference"), "   ");
    await submit(user);

    const alert = await screen.findByRole("alert", {}, { timeout: 3000 });
    expect(alert).toHaveTextContent("Diagnosis: Enter the diagnosis.");
    expect(alert).toHaveTextContent("Treatment plan reference: The treatment-plan reference is blank. Enter the reference, or leave the field out if there is none.");
    expect(posts()).toHaveLength(0);
    expect(within(form()).getByLabelText("Diagnosis")).toHaveAttribute("aria-invalid", "true");
    await waitFor(() => expect(alert).toHaveFocus());
    await user.click(within(alert).getByRole("button", { name: "Diagnosis" }));
    expect(within(form()).getByLabelText("Diagnosis")).toHaveFocus();
  });

  it("when the server refuses it lists what to correct, keeps everything typed, and a later save goes through", async () => {
    const user = userEvent.setup();
    await openTab();
    await fill(user, { label: "Caries", tooth: "16", plan: "plan-7" });
    server.failClinical(`POST /api/patients/${A}/diagnoses`, json(400, {
      error: "validation_failed", message: "x", problems: [{ field: "toothKey", code: "unknown_tooth", message: "\"19\" is not a tooth. Use the two-digit FDI number, such as 16 or 55, or leave the tooth empty." }],
    }));
    await submit(user);
    expect(await screen.findByRole("alert")).toHaveTextContent("Tooth: \"19\" is not a tooth");
    expect(within(form()).getByLabelText("Diagnosis")).toHaveValue("Caries");
    expect(within(form()).getByLabelText("Reference")).toHaveValue("plan-7");
    expect(server.diagnoses.diagnoses).toHaveLength(0);
    await submit(user);
    await waitFor(() => expect(status()).toHaveTextContent("Diagnosis recorded."));
    expect(server.diagnoses.diagnoses).toHaveLength(1);
  });

  it("an encounter that is not this patient's is refused with a message and what was typed is kept", async () => {
    const user = userEvent.setup();
    await openTab();
    await fill(user, { label: "Caries" });
    server.failClinical(`POST /api/patients/${A}/diagnoses`, json(404, { error: "encounter_not_found", message: "That encounter was not found for this patient. Choose one of this patient's encounters.", problems: [{ field: "encounterId", code: "not_found", message: "Choose one of this patient's encounters." }] }));
    await submit(user);
    expect(await screen.findByRole("alert")).toHaveTextContent("Encounter: Choose one of this patient's encounters.");
    expect(within(form()).getByLabelText("Diagnosis")).toHaveValue("Caries");
  });
});

describe("a save that fails, and retries", () => {
  it("a dropped connection says so, keeps what was typed, and the retry makes exactly one diagnosis under the same key", async () => {
    const user = userEvent.setup();
    await openTab();
    await fill(user, { label: "Caries", plan: "plan-7" });
    server.dropNextResponse("POST /diagnoses");                                                    // the server stores it, the answer is lost
    await submit(user);
    expect(await screen.findByText("Not saved: the connection dropped. What you entered is still here; save again to retry.")).toBeInTheDocument();
    expect(within(form()).getByLabelText("Diagnosis")).toHaveValue("Caries");
    await submit(user);
    await waitFor(() => expect(status()).toHaveTextContent("Diagnosis recorded."));
    const keys = posts().map((c) => (c.body as { idempotencyKey: string }).idempotencyKey);
    expect(keys[0]).toBe(keys[1]);
    expect(server.diagnoses.diagnoses).toHaveLength(1);
  });

  it("a failed save (503) says nothing was recorded and keeps the entries", async () => {
    const user = userEvent.setup();
    await openTab();
    await fill(user, { label: "Caries" });
    server.failClinical(`POST /api/patients/${A}/diagnoses`, json(503, { error: "save_failed", message: "The diagnosis could not be saved, so nothing was recorded. Try again." }));
    await submit(user);
    expect(await screen.findByText(/Not saved: The diagnosis could not be saved, so nothing was recorded\./)).toBeInTheDocument();
    expect(within(form()).getByLabelText("Diagnosis")).toHaveValue("Caries");
    expect(server.diagnoses.diagnoses).toHaveLength(0);
  });
});

describe("correcting a diagnosis", () => {
  const open = async (user: ReturnType<typeof userEvent.setup>, label = "Caries") => {
    await user.click(within(item(label)).getByRole("button", { name: "Correct" }));
    return screen.getByRole("form", { name: "Correct this diagnosis" });
  };

  it("keeps the treatment plan reference when it is left alone, needs a reason, and says in the history what changed and why", async () => {
    server.diagnoses.add(A, encounter, { label: "Caries", toothKey: "16", treatmentPlanReference: "plan-7" });
    const user = userEvent.setup();
    await openTab();
    const f = within(await open(user));
    expect(f.getByLabelText("Diagnosis")).toHaveValue("Caries");
    expect(f.getByLabelText(/Keep it/)).toBeChecked();
    await user.clear(f.getByLabelText("Diagnosis"));
    await user.type(f.getByLabelText("Diagnosis"), "Deep caries");
    await user.click(f.getByRole("button", { name: "Save correction" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Reason: Say why this diagnosis is being corrected.");
    expect(posts("/correct")).toHaveLength(0);                                                       // nothing is sent without a reason
    await user.type(f.getByLabelText("Why is this being corrected?"), "Clarified at review");
    await user.click(f.getByRole("button", { name: "Save correction" }));
    await waitFor(() => expect(status()).toHaveTextContent("Diagnosis corrected."));
    expect(posts("/correct")[0].body).toMatchObject({ label: "Deep caries", toothKey: "16", treatmentPlanReference: null, clearTreatmentPlanReference: false, reason: "Clarified at review" });
    expect(item("Deep caries")).toHaveTextContent("Treatment plan reference (unresolved): plan-7");
    await user.click(within(item("Deep caries")).getByRole("button", { name: "History" }));
    const rows = within(await screen.findByRole("table", { name: /History of this diagnosis/ })).getAllByRole("row").slice(1).map((r) => [...r.querySelectorAll("td")].map((c) => c.textContent));
    expect(rows.map((r) => [r[2], r[4], r[5]])).toEqual([["Recorded", "plan-7 (unresolved)", "-"], ["Corrected", "plan-7 (unresolved)", "Clarified at review"]]);
  });

  it("replaces the reference only when asked, and the old value stays in the history", async () => {
    server.diagnoses.add(A, encounter, { label: "Caries", treatmentPlanReference: "plan-7" });
    const user = userEvent.setup();
    await openTab();
    const f = within(await open(user));
    await user.click(f.getByLabelText("Replace it"));
    expect(await f.findByText(/Changing the treatment plan reference is recorded in the history with this reason/)).toBeInTheDocument();
    await user.type(f.getByLabelText("Reference"), "  plan-8 ");
    await user.type(f.getByLabelText("Why is this being corrected?"), "Wrong plan");
    await user.click(f.getByRole("button", { name: "Save correction" }));
    await waitFor(() => expect(item("Caries")).toHaveTextContent("plan-8"));
    expect(posts("/correct")[0].body).toMatchObject({ treatmentPlanReference: "plan-8", clearTreatmentPlanReference: false });
    expect(server.diagnoses.diagnoses[0].versions.map((v) => v.treatmentPlanReference)).toEqual(["plan-7", "plan-8"]);
  });

  it("removes the reference only by choosing to, which is sent as an explicit clear", async () => {
    server.diagnoses.add(A, encounter, { label: "Caries", treatmentPlanReference: "plan-7" });
    const user = userEvent.setup();
    await openTab();
    const f = within(await open(user));
    await user.click(f.getByLabelText("Remove it"));
    await user.type(f.getByLabelText("Why is this being corrected?"), "Entered by mistake");
    await user.click(f.getByRole("button", { name: "Save correction" }));
    await waitFor(() => expect(item("Caries")).toHaveTextContent("No treatment plan reference."));
    expect(posts("/correct")[0].body).toMatchObject({ treatmentPlanReference: null, clearTreatmentPlanReference: true });
    expect(server.diagnoses.diagnoses[0].versions.map((v) => v.treatmentPlanReference)).toEqual(["plan-7", null]);
  });

  it("a diagnosis changed by someone else shows the conflict banner, keeps what was typed, and after reloading the correction goes through", async () => {
    const d = server.diagnoses.add(A, encounter, { label: "Caries" });
    const user = userEvent.setup();
    await openTab();
    const f = within(await open(user));
    await user.clear(f.getByLabelText("Diagnosis"));
    await user.type(f.getByLabelText("Diagnosis"), "Mine");
    await user.type(f.getByLabelText("Why is this being corrected?"), "Because");
    server.diagnoses.touch(d.id);                                                                    // another person changed it first
    await user.click(f.getByRole("button", { name: "Save correction" }));
    expect(await screen.findByText("Someone else changed this while you were editing")).toBeInTheDocument();
    expect(f.getByLabelText("Diagnosis")).toHaveValue("Mine");
    await user.click(screen.getByRole("button", { name: "Reload current version" }));
    await waitFor(() => expect(screen.queryByText("Someone else changed this while you were editing")).not.toBeInTheDocument());
    await user.click(within(screen.getByRole("form", { name: "Correct this diagnosis" })).getByRole("button", { name: "Save correction" }));
    await waitFor(() => expect(status()).toHaveTextContent("Diagnosis corrected."));
    expect(item("Mine")).toBeInTheDocument();
  });
});

describe("withdrawing a diagnosis", () => {
  it("asks why first, then marks it withdrawn, keeps it and its reference, and shows it only on request", async () => {
    server.diagnoses.add(A, encounter, { label: "Caries", treatmentPlanReference: "plan-7" });
    const user = userEvent.setup();
    await openTab();
    await user.click(within(item("Caries")).getByRole("button", { name: "Withdraw" }));
    const f = within(screen.getByRole("form", { name: "Withdraw this diagnosis" }));
    expect(f.getByRole("button", { name: "Withdraw diagnosis" })).toBeDisabled();                    // not without a reason
    await user.type(f.getByLabelText("Why is this diagnosis being withdrawn?"), "Entered on the wrong patient");
    await user.click(f.getByRole("button", { name: "Withdraw diagnosis" }));
    await waitFor(() => expect(status()).toHaveTextContent("Diagnosis withdrawn."));
    expect(screen.queryByRole("listitem", { name: "Diagnosis: Caries" })).not.toBeInTheDocument();
    await user.click(screen.getByLabelText("Show withdrawn diagnoses"));
    const li = await screen.findByRole("listitem", { name: "Diagnosis: Caries" });
    expect(li).toHaveTextContent("Withdrawn");
    expect(li).toHaveTextContent("Entered on the wrong patient");
    expect(li).toHaveTextContent("Treatment plan reference (unresolved): plan-7");
    expect(within(li).queryByRole("button", { name: "Correct" })).not.toBeInTheDocument();           // a withdrawn diagnosis cannot be corrected
  });
});

describe("the history", () => {
  it("a history that could not load says so rather than showing none", async () => {
    const d = server.diagnoses.add(A, encounter, { label: "Caries" });
    server.failClinical(`GET /api/diagnoses/${d.id}/history`, json(500, { error: "server_error", message: "boom" }));
    const user = userEvent.setup();
    await openTab();
    await user.click(within(item("Caries")).getByRole("button", { name: "History" }));
    expect(await screen.findByText("Could not load the history. Do not assume there is none. Close and open it again.")).toBeInTheDocument();
  });
});

describe("a role that may only read", () => {
  it("sees the diagnoses, the reference as unresolved and the history, with no form and no way to change anything", async () => {
    server.permissions = READ_ONLY;
    server.diagnoses.add(A, encounter, { label: "Caries", treatmentPlanReference: "plan-7" });
    const user = userEvent.setup();
    await openTab();
    expect(screen.queryByRole("form", { name: "Record a diagnosis" })).not.toBeInTheDocument();
    expect(item("Caries")).toHaveTextContent("Treatment plan reference (unresolved): plan-7");
    expect(within(item("Caries")).queryByRole("button", { name: "Correct" })).not.toBeInTheDocument();
    expect(within(item("Caries")).queryByRole("button", { name: "Withdraw" })).not.toBeInTheDocument();
    expect(status()).toHaveTextContent("You can read this but your role cannot change it.");
    await user.click(within(item("Caries")).getByRole("button", { name: "History" }));
    expect(await screen.findByRole("table", { name: /History of this diagnosis/ })).toBeInTheDocument();
  });
});

describe("accessibility", () => {
  it("has no axe violations with diagnoses listed, with problems shown, with the correction form open and with the history open", async () => {
    server.diagnoses.add(A, encounter, { label: "Caries", toothKey: "16", treatmentPlanReference: "plan-7" });
    const user = userEvent.setup();
    const { container } = await openTab();
    expect(await axe(container)).toHaveNoViolations();
    await submit(user);                                                                              // an empty entry: problems are shown
    await screen.findByRole("alert");
    expect(await axe(container)).toHaveNoViolations();
    await user.click(within(item("Caries")).getByRole("button", { name: "Correct" }));
    await user.click(within(item("Caries")).getByRole("button", { name: "History" }));
    await screen.findByRole("table", { name: /History of this diagnosis/ });
    expect(await axe(container)).toHaveNoViolations();
  });
});
