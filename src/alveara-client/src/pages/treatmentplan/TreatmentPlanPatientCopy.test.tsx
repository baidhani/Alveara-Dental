import { describe, it, expect, afterEach, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import type { PatientDetail } from "../../services/patientsApi";
import { item, patient, plan, stubApi } from "../../test/treatmentPlanFixtures";
import { TreatmentPlansPanel } from "./TreatmentPlansPanel";

/**
 * ALV-015-C01: the patient copy, against a scripted API. What is proven here is the UI's behaviour: any reader can open a plain-language copy with the explanation in the dentist's words, the procedures in stages with
 * what each is for and its estimated fee, the patient's answers in plain words and the estimate with what it is not; it prints from one button; it leaves out everything meant for staff (the clinical rationale,
 * who entered what, the revision history, anything withdrawn); it never implies insurance has been consulted or says what the patient will pay; and a withdrawn plan offers no copy.
 */
const named = { ...patient, firstName: "Ann", lastName: "Alder" } as PatientDetail;
const phases = [{ phaseNumber: 1, name: "First visit", plannedVisit: "Today", targetDate: null }, { phaseNumber: 2, name: "After healing", plannedVisit: "Two weeks later", targetDate: "2030-06-01" }];
const forPatient = () => plan({
  currentRevision: 3, referenceCode: "PLAN-7", clinicalRationale: "STAFF ONLY: probing depths of 6 mm lower left.", patientExplanation: "We will clean below the gums first, then check how they heal.", phases,
  acceptedEstimateTotal: 100, estimateTotal: 120, activeItemCount: 2,
  items: [
    item({ id: "it1", phaseNumber: 1, procedureDescription: "Deep cleaning", fee: 100, acceptance: "Accepted", scheduling: "Scheduled", plannedDate: "2030-05-20", completion: "Completed", recordedByName: "Dr. Okafor", decidedByName: "Ann Alder" }),
    item({ id: "it2", itemNumber: 2, phaseNumber: 2, procedureCode: "FILL-1", procedureDescription: "Old filling replaced", fee: 60, isWithdrawn: true, withdrawnReason: "Patient declined it", withdrawnByName: "Dr. Okafor" }),
    item({ id: "it3", itemNumber: 3, phaseNumber: 2, procedureCode: "POL-1", procedureDescription: "Polish", fee: 20 }),
  ],
});
const open = async (p = forPatient(), canWrite = false) => {
  stubApi({ plans: [p] });
  render(<TreatmentPlansPanel patient={named} canWrite={canWrite} />);
  await screen.findByRole("heading", { name: "Plans on record" });
  await userEvent.setup().click(screen.getByRole("button", { name: "Show patient copy" }));
  return screen.getByRole("region", { name: "Patient copy of Gum health plan" });
};

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("the patient copy", () => {
  it("is plain words for the patient: who it is for, the dentist's explanation, the procedures in stages with what each is for, the fees and the answers", async () => {
    const copy = await open();
    expect(within(copy).getByRole("heading", { level: 3 })).toHaveTextContent("Your treatment plan: Gum health plan");
    expect(copy).toHaveTextContent(/Prepared for Ann Alder on/);
    expect(copy).toHaveTextContent("We will clean below the gums first, then check how they heal.");
    expect(within(copy).getAllByRole("heading", { level: 4 }).map((h) => h.textContent)).toEqual(["Phase 1: First visit", "Phase 2: After healing"]);
    expect(copy).toHaveTextContent("Today");
    expect(copy).toHaveTextContent("Two weeks later");
    expect(copy).toHaveTextContent("Deep cleaning");
    expect(copy).toHaveTextContent("For: Chronic periodontitis");
    expect(copy).toHaveTextContent("Estimated fee: $100.00 · Your answer: accepted · planned for");
    expect(copy).toHaveTextContent("Completed");                                                           // ALV-N023 (F-183): not the terse "done"
    expect(copy).toHaveTextContent("Polish");
    expect(copy).toHaveTextContent("Estimated fee: $20.00 · Your answer: not yet given");
    expect(copy).toHaveTextContent("Plan version 3 · Plan reference: PLAN-7");                              // ALV-N023 (DR-6): kept, clearly labelled
  });

  it("totals only what is still proposed, shows what has been accepted, and says what the figure is and is not", async () => {
    const copy = await open();
    expect(copy).toHaveTextContent("Estimated total: $120.00");                                           // $100 + $20; the withdrawn $60 is not in it
    expect(copy).toHaveTextContent("Of that, the part you have accepted so far: $100.00");
    expect(copy).not.toHaveTextContent("not an insurance estimate and not a guaranteed patient cost");      // ALV-N023 (F-183): one disclaimer, not two saying nearly the same
    expect(copy).toHaveTextContent("It does not include any insurance benefits, and it is not a quote of what you will be asked to pay.");
    expect(copy.textContent).not.toMatch(/covered|insurer|your insurance will|you will pay/i);
  });

  it("leaves out everything meant for staff: the clinical rationale, who entered what, the revision history and anything withdrawn", async () => {
    const copy = await open();
    const text = copy.textContent ?? "";
    expect(text).not.toContain("STAFF ONLY");
    expect(text).not.toContain("Clinical rationale");
    expect(text).not.toContain("Dr. Okafor");
    expect(text).not.toContain("Entered by");
    expect(text).not.toContain("revision history");
    expect(text).not.toContain("Old filling replaced");
    expect(text).not.toContain("Patient declined it");
    expect(text).not.toContain("Withdrawn");
    expect(within(copy).queryByRole("button", { name: /Revise|Withdraw|Accept|Decline|Schedule|completed/ })).not.toBeInTheDocument();
  });

  it("says plainly that the dentist has proposed the care when there is no explanation yet, and for a plan with no phases is one simple list", async () => {
    const copy = await open(plan({ patientExplanation: null, items: [item({ procedureDescription: "Deep cleaning" })] }));
    expect(copy).toHaveTextContent("Your dentist has proposed the care below. Please ask if anything is unclear.");
    expect(within(copy).queryAllByRole("heading", { level: 4 })).toHaveLength(0);
    expect(copy).toHaveTextContent("Deep cleaning");
  });

  it("prints from one button as a server PDF (ALV-N026 S7: issue, then open that issue's PDF; never the page) and closes again, and can be opened by a reader who may not change plans", async () => {
    const user = userEvent.setup();
    const print = vi.fn();
    vi.stubGlobal("print", print);
    const tab = { opener: {} as unknown, location: { href: "" }, close: vi.fn() };
    vi.spyOn(window, "open").mockReturnValue(tab as unknown as Window);
    vi.spyOn(URL, "createObjectURL").mockReturnValue("blob:copy");
    vi.spyOn(URL, "revokeObjectURL").mockImplementation(() => undefined);
    const api = stubApi({ plans: [forPatient()], respond: {
      "/copy-issues": () => new Response(JSON.stringify({ id: "iss1", planId: "p1", revisionNumber: 3, templateVersion: "v2", issuedAtUtc: "2030-01-01T00:00:00Z", replayed: false }), { status: 201, headers: { "Content-Type": "application/json" } }),
      "/treatment-plan-copy-issues/iss1/pdf": () => new Response(new Blob(["%PDF"]), { status: 200, headers: { "Content-Type": "application/pdf" } }),
    } });
    render(<TreatmentPlansPanel patient={named} canWrite={false} />);
    await screen.findByRole("heading", { name: "Plans on record" });
    await user.click(screen.getByRole("button", { name: "Show patient copy" }));
    const copy = screen.getByRole("region", { name: "Patient copy of Gum health plan" });
    await user.click(within(copy).getByRole("button", { name: "Print (PDF)" }));
    expect(await within(copy).findByRole("status")).toHaveTextContent("The patient copy was issued and opened as a PDF.");
    const issue = api.calls.find((c) => c.url.includes("/copy-issues"));
    expect(issue?.method).toBe("POST");
    expect(typeof issue?.body?.idempotencyKey).toBe("string");
    expect(api.calls.some((c) => c.url.includes("/treatment-plan-copy-issues/iss1/pdf"))).toBe(true);
    expect(api.calls.some((c) => c.url.includes("/print-events"))).toBe(false);                                // the server records the print, not the browser
    expect(tab.location.href).toBe("blob:copy");
    expect(print).not.toHaveBeenCalled();
    await user.click(within(copy).getByRole("button", { name: "Close patient copy" }));
    expect(screen.queryByRole("region", { name: "Patient copy of Gum health plan" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Show patient copy" })).toBeInTheDocument();
  });

  it("shows the server's reason and prints nothing when the copy is refused", async () => {
    const user = userEvent.setup();
    const tab = { opener: {} as unknown, location: { href: "" }, close: vi.fn() };
    vi.spyOn(window, "open").mockReturnValue(tab as unknown as Window);
    stubApi({ plans: [forPatient()], respond: {
      "/copy-issues": () => new Response(JSON.stringify({ id: "iss2", planId: "p1", revisionNumber: 3, templateVersion: "v2", issuedAtUtc: "2030-01-01T00:00:00Z", replayed: false }), { status: 201, headers: { "Content-Type": "application/json" } }),
      "/treatment-plan-copy-issues/iss2/pdf": () => new Response(JSON.stringify({ error: "integrity_mismatch", message: "This patient copy cannot be reproduced exactly from the recorded plan, so it was not produced." }), { status: 409, headers: { "Content-Type": "application/json" } }),
    } });
    render(<TreatmentPlansPanel patient={named} canWrite={false} />);
    await screen.findByRole("heading", { name: "Plans on record" });
    await user.click(screen.getByRole("button", { name: "Show patient copy" }));
    const copy = screen.getByRole("region", { name: "Patient copy of Gum health plan" });
    await user.click(within(copy).getByRole("button", { name: "Print (PDF)" }));
    expect(await within(copy).findByRole("status")).toHaveTextContent("Nothing was printed. This patient copy cannot be reproduced exactly from the recorded plan");
    expect(tab.close).toHaveBeenCalled();
  });

  it("is not offered for a withdrawn plan, which is no longer the proposal", async () => {
    stubApi({ plans: [plan({ status: "Withdrawn", withdrawnReason: "Entered in error", withdrawnByName: "Dr. Okafor" })] });
    render(<TreatmentPlansPanel patient={named} />);
    await screen.findByRole("heading", { name: "Plans on record" });
    expect(screen.queryByRole("button", { name: "Show patient copy" })).not.toBeInTheDocument();
  });

  it("has no accessibility violations", async () => {
    await open();
    expect(await axe(document.body)).toHaveNoViolations();
  });
});
