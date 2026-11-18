import { describe, it, expect, afterEach, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { item, json, patient, plan, stubApi } from "../../test/treatmentPlanFixtures";
import { TreatmentPlansPanel } from "./TreatmentPlansPanel";
import { TreatmentPlanComparison } from "./TreatmentPlanComparison";
import { activityWords, itemGaps, planStateWords } from "./treatmentPlanRules";
import { EMPTY_ITEM } from "./treatmentPlanRules";

/**
 * ALV-N023 (client, slice 4): the plan screen shows what the server derived, and only what can act.
 * DR-1/PD-15 booking and target date; PD-14/addendum 01 state and lock; DR-4 bulk; F-180/F-222 adopt; F-181/F-190 activity; F-182 before/after; F-183 patient copy; F-186 reason; F-191 $0.00;
 * F-179/F-220 banner; Q-5ae gaps.
 */
afterEach(() => vi.unstubAllGlobals());

const accepted = { acceptance: "Accepted" as const, answerRecordedAtUtc: "2030-01-02T10:00:00Z", recordedAtUtc: "2030-01-02T10:00:00Z" };
const open = async (p: ReturnType<typeof plan>, canWrite = true, extra = {}) => {
  const api = stubApi({ plans: [p], ...extra });
  render(<TreatmentPlansPanel patient={patient} canWrite={canWrite} />);
  return api;
};

describe("booking and target dates (DR-1, PD-15)", () => {
  it("shows Booked, cancelled-not-booked, Not booked, and a target date with Overdue - never 'Scheduled'", async () => {
    await open(plan({ items: [
      item({ id: "a", itemNumber: 1, procedureCode: "A-1", ...accepted, appointment: { appointmentId: "ap1", startUtc: "2030-02-04T15:00:00Z", providerName: "Dr. Rivera", status: "Scheduled", live: true, linkedByName: "Desk", linkedAtUtc: "2030-01-03T10:00:00Z" } }),
      item({ id: "b", itemNumber: 2, procedureCode: "B-1", ...accepted, appointment: { appointmentId: "ap2", startUtc: "2030-02-05T15:00:00Z", providerName: null, status: "Cancelled", live: false, linkedByName: null, linkedAtUtc: "2030-01-03T10:00:00Z" } }),
      item({ id: "c", itemNumber: 3, procedureCode: "C-1", ...accepted, scheduling: "Scheduled", plannedDate: "2020-01-10", targetOverdue: true }),
    ] }));
    const lines = await screen.findAllByTestId("booking");
    expect(lines[0]).toHaveTextContent(/^Booked: .* with Dr\. Rivera\.$/);
    expect(lines[1]).toHaveTextContent(/was cancelled, so it is not booked\.$/);
    expect(lines[2]).toHaveTextContent("Not booked.");
    expect(screen.getByText(/Target date/)).toBeInTheDocument();
    expect(screen.getByText("Overdue")).toBeInTheDocument();
    expect(screen.queryByText(/Scheduled for/)).not.toBeInTheDocument();
  });

  it("asks to confirm a past target date and then sends confirmPast", async () => {
    const api = await open(plan({ items: [item({ id: "a", procedureCode: "A-1", ...accepted })] }), true, {
      respond: { "/schedule": (body: Record<string, unknown> | null) => body?.confirmPast ? json(plan({ items: [item({ id: "a", procedureCode: "A-1", ...accepted })] }))
        : json({ error: "past_target_date", message: "That date has passed. Confirm to save it as an overdue target date.", fieldErrors: { plannedDate: "That date has passed." } }, 400) },
    });
    await userEvent.click(await screen.findByRole("button", { name: "Set target date of A-1" }));
    const form = screen.getByRole("form", { name: "Target date of A-1" });
    expect(form).toHaveTextContent("It does not book an appointment");
    await userEvent.type(within(form).getByLabelText("Target date"), "2020-01-10");
    await userEvent.click(within(form).getByRole("button", { name: "Save target date" }));
    expect(await within(form).findByRole("alert")).toHaveTextContent("Save it anyway as an overdue target date?");
    await userEvent.click(within(form).getByRole("button", { name: "Save as overdue" }));
    const posts = api.calls.filter((c) => c.url.includes("/schedule"));
    expect(posts.map((c) => c.body?.confirmPast)).toEqual([false, true]);
  });
});

describe("plan state and what a plan offers (PD-14, addendum 01, DR-4, F-180/F-222)", () => {
  it("names each state from the server's values", () => {
    expect(planStateWords({ status: "Proposed", planState: "InProgress" })).toBe("In progress");
    expect(planStateWords({ status: "Proposed", planState: "Closed", closedReason: "NoProcedures" })).toBe("Closed - no procedures remain");
    expect(planStateWords({ status: "Proposed", planState: "Closed", closedReason: "AllDeclined" })).toBe("Closed - all procedures declined");
    expect(planStateWords({ status: "Proposed", planState: "Completed" })).toBe("Completed");
  });

  it("a locked plan offers only Revise anyway - no Withdraw, no item changes", async () => {
    await open(plan({ planState: "Completed", locked: true, items: [item({ id: "a", procedureCode: "A-1", ...accepted, completion: "Completed" })] }));
    expect(await screen.findByTestId("plan-state")).toHaveTextContent("Completed");
    expect(screen.getByRole("button", { name: "Revise anyway…" })).toBeInTheDocument();
    for (const name of [/Withdraw/, /Add procedure/, /Rename plan/, /Accept all/, /Decline all/, /target date/]) expect(screen.queryByRole("button", { name })).not.toBeInTheDocument();
  });

  it("a Closed plan with no completed care stays correctable (not locked)", async () => {
    await open(plan({ planState: "Closed", closedReason: "AllDeclined", locked: false, items: [item({ id: "a", procedureCode: "A-1", acceptance: "Declined" })] }));
    expect(await screen.findByTestId("plan-state")).toHaveTextContent("Closed - all procedures declined");
    expect(screen.getByRole("button", { name: "Withdraw plan" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Accept A-1" })).toBeInTheDocument();
  });

  it("bulk answers appear only when something is unanswered and adopt only when the server says so", async () => {
    await open(plan({ unansweredCount: 0, hasAdoptable: false, items: [item({ id: "a", procedureCode: "A-1", ...accepted, completion: "Completed" })] }));
    await screen.findByTestId("plan-state");
    expect(screen.queryByRole("button", { name: /all unanswered/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Adopt earlier/ })).not.toBeInTheDocument();
  });

  it("reports how many unanswered procedures a bulk answer changed", async () => {
    const after = plan({ unansweredCount: 0, changedCount: 2, items: [item({ id: "a", ...accepted }), item({ id: "b", itemNumber: 2, ...accepted })] });
    await open(plan({ unansweredCount: 2, items: [item({ id: "a" }), item({ id: "b", itemNumber: 2 })] }), true, { respond: { "/decision": () => json(after) } });
    await userEvent.click(await screen.findByRole("button", { name: "Accept all unanswered" }));
    await userEvent.click(screen.getByRole("button", { name: /^Record/ }));
    expect(await screen.findByText(/2 unanswered procedures accepted\. Answers already given were left as they were\./)).toBeInTheDocument();
  });
});

describe("history and display corrections", () => {
  it("words every activity, including old 'scheduled' rows as target dates (F-181, F-190)", () => {
    expect(["ItemScheduled", "TargetDateSet", "AppointmentLinked", "ReferenceCodeSet", "ItemDeclined"].map(activityWords))
      .toEqual(["Target date set", "Target date set", "Appointment linked (booked)", "Reference code set", "Answer recorded: declined"]);
  });

  it("puts Before and After on separate lines (F-182)", () => {
    const rev = (n: number) => ({ revisionNumber: n, changeType: "Renamed", title: "t", actorName: "Dr", createdAtUtc: "2030-01-02T10:00:00Z", procedureCount: 1, estimateTotal: 110, phaseCount: 0, reason: null });
    render(<TreatmentPlanComparison comparison={{ from: rev(1), to: rev(2), title: { from: "Gum plan", to: "Gum plan v2" }, clinicalRationale: null, patientExplanation: null, estimateFrom: 110, estimateTo: 110, estimateDelta: 0,
      itemsAdded: [], itemsWithdrawn: [], itemsMoved: [], phasesAdded: [], phasesRemoved: [], phasesChanged: [] } as never} />);
    expect(screen.getByText("Before: Gum plan")).toBeInTheDocument();
    expect(screen.getByText("After: Gum plan v2")).toBeInTheDocument();
  });

  it("shows $0.00 accepted, an explained empty reason, and no stale create-form banner (F-191, F-186, F-179)", async () => {
    await open(plan({ acceptedEstimateTotal: 0, activeItemCount: 1, items: [item({ id: "a", procedureCode: "A-1" })] }));
    expect(await screen.findByText("Of that, accepted so far: $0.00")).toBeInTheDocument();
    expect(screen.queryByText("Nothing is saved until you create a plan.")).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Withdraw A-1" }));
    const withdraw = screen.getByRole("button", { name: "Withdraw procedure" });
    expect(withdraw).toBeEnabled();
    await userEvent.click(withdraw);
    expect(await screen.findByText("Say why.")).toBeInTheDocument();
  });

  it("the patient copy shows the practice, 'Completed' and a labelled plan reference (F-183, DR-6)", async () => {
    await open(plan({ referenceCode: "plan-2030-01", items: [item({ id: "a", ...accepted, completion: "Completed" })] }), true, {
      respond: { "/practice-header": () => json({ name: "Bright Smiles Dental", phone: "555-010-0000", addressLine: "1 Main St, Austin TX" }) },
    });
    await userEvent.click(await screen.findByRole("button", { name: "Show patient copy" }));
    expect(await screen.findByTestId("copy-practice")).toHaveTextContent("Bright Smiles Dental1 Main St, Austin TXPhone: 555-010-0000");
    const copy = screen.getByRole("region", { name: /Patient copy/ });
    expect(copy).toHaveTextContent("Completed");
    expect(copy).not.toHaveTextContent("· done");                                                        // the procedure line says Completed
    expect(copy).toHaveTextContent("Plan reference: plan-2030-01");
  });

  it("names both missing choices of a procedure at once (Q-5ae)", () => {
    expect(itemGaps(EMPTY_ITEM)).toEqual({ diagnosisId: "Choose the diagnosis this procedure is for.", procedureId: "Choose the procedure to propose." });
  });
});
