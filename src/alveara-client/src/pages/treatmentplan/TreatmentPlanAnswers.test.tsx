import { describe, it, expect, afterEach, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { item, json, patient, plan, stubApi } from "../../test/treatmentPlanFixtures";
import type { Api } from "../../test/treatmentPlanFixtures";
import { TreatmentPlansPanel } from "./TreatmentPlansPanel";

/**
 * ALV-015-C01: the patient's answer and what follows, against a scripted API. The rules are proven by the backend tests and the real-backend walkthrough; what is proven here is the UI's behaviour: every reader
 * sees where each procedure stands (answer, who gave it and who entered it, the reason for a decline, scheduled, completed); only people who may change plans get controls; a decline needs a reason before anything
 * is sent; the answer carries the plan's version; answering the whole plan shows the server's refusal when an opposite answer exists and keeps what was typed; scheduling is offered only after acceptance; completed
 * is final and offers nothing, not even withdrawing.
 */
const answered = {
  acceptance: "Accepted" as const, decidedByName: "Ann Alder (patient)", recordedByName: "Dr. Okafor", recordedAtUtc: "2030-01-03T10:00:00Z", decisionRevision: 2,
};
const mixed = () => plan({
  currentRevision: 2, acceptanceStatus: "Partial", activeItemCount: 4, estimateTotal: 260, acceptedEstimateTotal: 160,
  items: [
    item({ id: "it1", itemNumber: 1, ...answered }),
    item({ id: "it2", itemNumber: 2, procedureCode: "FILL-1", procedureDescription: "Filling", fee: 60, ...answered, scheduling: "Scheduled", plannedDate: "2030-05-20" }),
    item({ id: "it3", itemNumber: 3, procedureCode: "CROWN-1", procedureDescription: "Crown", fee: 40, acceptance: "Declined", decisionReason: "Cannot afford it now", recordedByName: "Dr. Okafor", recordedAtUtc: "2030-01-03T10:05:00Z", decisionRevision: 2 }),
    item({ id: "it4", itemNumber: 4, procedureCode: "POL-1", procedureDescription: "Polish", fee: 20 }),
  ],
});
const posts = (api: Api) => api.calls.filter((c) => c.method === "POST");
const open = async (canWrite: boolean) => {
  render(<TreatmentPlansPanel patient={patient} canWrite={canWrite} />);
  await screen.findByRole("heading", { name: "Plans on record" });
};
const row = (code: string) => screen.getByText(code, { exact: true }).closest("li")!;

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("where each procedure stands", () => {
  it("shows every reader the answer, who gave it, who entered it and when, the reason for a decline, the schedule and the plan's overall answer, with no controls", async () => {
    stubApi({ plans: [mixed()] });
    await open(false);
    expect(screen.getByText("Patient's answer: Partly accepted")).toBeInTheDocument();
    const first = within(row("PERIO-1"));
    expect(first.getByText("Accepted")).toBeInTheDocument();
    expect(first.getByText(/Answer given by Ann Alder \(patient\)\./)).toHaveTextContent("Entered by Dr. Okafor on");
    expect(first.getByText(/plan revision 2/)).toBeInTheDocument();
    expect(within(row("FILL-1")).getByText(/Scheduled for/)).toBeInTheDocument();
    const declined = within(row("CROWN-1"));
    expect(declined.getByText("Declined")).toBeInTheDocument();
    expect(declined.getByText(/Reason: Cannot afford it now/)).toBeInTheDocument();
    expect(within(row("POL-1")).getByText("Not yet answered")).toBeInTheDocument();
    expect(screen.getByText("Of that, accepted so far: $160.00")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Accept|Decline|Schedule|completed|Withdraw/ })).not.toBeInTheDocument();
  });

  it("shows Completed and offers nothing more for a completed procedure, not even withdrawing it", async () => {
    stubApi({ plans: [plan({ items: [item({ id: "it1", ...answered, completion: "Completed" }), item({ id: "it2", itemNumber: 2, procedureCode: "POL-1" })] })] });
    await open(true);
    expect(within(row("PERIO-1")).getByText("Completed")).toBeInTheDocument();
    expect(within(row("PERIO-1")).queryByRole("button")).not.toBeInTheDocument();
    expect(within(row("POL-1")).getByRole("button", { name: "Withdraw POL-1" })).toBeInTheDocument();
  });
});

describe("recording an answer", () => {
  it("accepts a procedure with who gave the answer and the plan's version", async () => {
    const user = userEvent.setup();
    const api = stubApi({ plans: [mixed()] });
    await open(true);
    await user.click(screen.getByRole("button", { name: "Accept POL-1" }));
    const f = within(screen.getByRole("form", { name: "Accept POL-1" }));
    await user.type(f.getByLabelText(/^Who gave this answer/), "  Ann Alder (patient) ");
    await user.click(f.getByRole("button", { name: "Record acceptance" }));
    const [post] = posts(api);
    expect(post.url).toContain("/api/treatment-plans/pl1/items/it4/decision");
    expect(post.body).toEqual({ rowVersion: "R1", decision: "Accepted", reason: null, decidedByName: "Ann Alder (patient)" });
    expect(await screen.findByText("POL-1 accepted.")).toBeInTheDocument();
  });

  it("will not decline without a reason and sends nothing until there is one", async () => {
    const user = userEvent.setup();
    const api = stubApi({ plans: [mixed()] });
    await open(true);
    await user.click(screen.getByRole("button", { name: "Decline POL-1" }));
    const f = within(screen.getByRole("form", { name: "Decline POL-1" }));
    await user.click(f.getByRole("button", { name: "Record decline" }));
    expect(f.getByRole("alert")).toHaveTextContent("Not recorded: say why it was declined.");
    expect(posts(api)).toHaveLength(0);
    await user.type(f.getByLabelText("Why was it declined?"), "Wants to wait");
    await user.click(f.getByRole("button", { name: "Record decline" }));
    expect(posts(api)[0].body).toMatchObject({ decision: "Declined", reason: "Wants to wait", decidedByName: null });
    expect(await screen.findByText("POL-1 declined.")).toBeInTheDocument();
  });

  it("offers only what makes sense: no accept for an accepted procedure, no decline for a declined one, no scheduling before an answer", async () => {
    stubApi({ plans: [mixed()] });
    await open(true);
    expect(within(row("PERIO-1")).queryByRole("button", { name: "Accept PERIO-1" })).not.toBeInTheDocument();
    expect(within(row("PERIO-1")).getByRole("button", { name: "Decline PERIO-1" })).toBeInTheDocument();
    expect(within(row("CROWN-1")).queryByRole("button", { name: "Decline CROWN-1" })).not.toBeInTheDocument();
    expect(within(row("CROWN-1")).queryByRole("button", { name: /Schedule|completed/ })).not.toBeInTheDocument();
    expect(within(row("POL-1")).queryByRole("button", { name: /Schedule|completed/ })).not.toBeInTheDocument();
    expect(within(row("FILL-1")).getByRole("button", { name: "Change schedule of FILL-1" })).toBeInTheDocument();
  });

  it("shows the server's refusal when the whole plan is answered against an opposite answer, and records nothing", async () => {
    const user = userEvent.setup();
    const api = stubApi({ plans: [mixed()], respond: { "/api/treatment-plans/pl1/decision": () => json({ error: "partial_acceptance_conflict", message: "1 of the 4 procedures already declined. Decide those one by one, or leave them as they are." }, 409) } });
    await open(true);
    await user.click(screen.getByRole("button", { name: "Accept all pending" }));
    const f = within(screen.getByRole("form", { name: "Accept all pending procedures" }));
    await user.type(f.getByLabelText(/^Who gave this answer/), "Ann Alder");
    await user.click(f.getByRole("button", { name: "Record acceptance" }));
    expect(await f.findByRole("alert")).toHaveTextContent("Not recorded: 1 of the 4 procedures already declined. Decide those one by one, or leave them as they are.");
    expect(f.getByLabelText(/^Who gave this answer/)).toHaveValue("Ann Alder");
    expect(posts(api)).toHaveLength(1);
    expect(screen.getByText("Patient's answer: Partly accepted")).toBeInTheDocument();         // nothing on screen changed
  });

  it("answers the whole plan and says so", async () => {
    const user = userEvent.setup();
    const api = stubApi({ plans: [plan()] });
    await open(true);
    await user.click(screen.getByRole("button", { name: "Decline all pending" }));
    const f = within(screen.getByRole("form", { name: "Decline all pending procedures" }));
    await user.type(f.getByLabelText("Why was it declined?"), "Not this year");
    await user.click(f.getByRole("button", { name: "Record decline" }));
    expect(posts(api)[0].url).toContain("/api/treatment-plans/pl1/decision");
    expect(posts(api)[0].body).toMatchObject({ decision: "Declined", reason: "Not this year" });
    expect(await screen.findByText("All pending procedures declined.")).toBeInTheDocument();
  });

  it("shows the conflict banner when the plan changed since it was opened, and keeps what was typed", async () => {
    const user = userEvent.setup();
    stubApi({ plans: [mixed()], respond: { "/items/it4/decision": () => json({ error: "concurrency_conflict", message: "stale" }, 409) } });
    await open(true);
    await user.click(screen.getByRole("button", { name: "Decline POL-1" }));
    const f = within(screen.getByRole("form", { name: "Decline POL-1" }));
    await user.type(f.getByLabelText("Why was it declined?"), "Wants to wait");
    await user.click(f.getByRole("button", { name: "Record decline" }));
    expect(await screen.findByText("Someone else changed this while you were editing")).toBeInTheDocument();
    expect(f.getByRole("alert")).toHaveTextContent("Not recorded: this plan changed since you opened it.");
    expect(f.getByLabelText("Why was it declined?")).toHaveValue("Wants to wait");
  });
});

describe("scheduling and completing", () => {
  it("schedules an accepted procedure with a date and takes it off the schedule", async () => {
    const user = userEvent.setup();
    const api = stubApi({ plans: [mixed()] });
    await open(true);
    await user.click(within(row("PERIO-1")).getByRole("button", { name: "Schedule PERIO-1" }));
    const f = within(screen.getByRole("form", { name: "Schedule PERIO-1" }));
    expect(f.getByText("This records that PERIO-1 is scheduled. It does not book an appointment.")).toBeInTheDocument();
    expect(f.queryByRole("button", { name: "Take off the schedule" })).not.toBeInTheDocument();
    await user.type(f.getByLabelText("Planned date (optional)"), "2030-06-15");
    await user.click(f.getByRole("button", { name: "Save schedule" }));
    expect(posts(api)[0].url).toContain("/api/treatment-plans/pl1/items/it1/schedule");
    expect(posts(api)[0].body).toEqual({ scheduled: true, plannedDate: "2030-06-15" });
    expect(await screen.findByText("PERIO-1 scheduled.")).toBeInTheDocument();

    await user.click(within(row("FILL-1")).getByRole("button", { name: "Change schedule of FILL-1" }));
    await user.click(within(screen.getByRole("form", { name: "Schedule FILL-1" })).getByRole("button", { name: "Take off the schedule" }));
    expect(posts(api)[1].body).toEqual({ scheduled: false, plannedDate: null });
  });

  it("completes an accepted procedure only after confirming that it is final", async () => {
    const user = userEvent.setup();
    const api = stubApi({ plans: [mixed()] });
    await open(true);
    await user.click(screen.getByRole("button", { name: "Mark PERIO-1 completed" }));
    const f = within(screen.getByRole("form", { name: "Mark PERIO-1 completed" }));
    expect(f.getByText(/Completed is final/)).toBeInTheDocument();
    expect(posts(api)).toHaveLength(0);
    await user.click(f.getByRole("button", { name: "Confirm completed" }));
    expect(posts(api)[0].url).toContain("/api/treatment-plans/pl1/items/it1/complete");
    expect(await screen.findByText("PERIO-1 marked completed.")).toBeInTheDocument();
  });

  it("shows the server's refusal and keeps the form open", async () => {
    const user = userEvent.setup();
    stubApi({ plans: [mixed()], respond: { "/items/it1/complete": () => json({ error: "item_not_accepted", message: "Only an accepted procedure can be scheduled or completed. Accept it first." }, 409) } });
    await open(true);
    await user.click(screen.getByRole("button", { name: "Mark PERIO-1 completed" }));
    const f = within(screen.getByRole("form", { name: "Mark PERIO-1 completed" }));
    await user.click(f.getByRole("button", { name: "Confirm completed" }));
    expect(await f.findByRole("alert")).toHaveTextContent("Not saved: Only an accepted procedure can be scheduled or completed. Accept it first.");
  });

  it("has no accessibility violations with an answer form open", async () => {
    const user = userEvent.setup();
    stubApi({ plans: [mixed()] });
    await open(true);
    await user.click(screen.getByRole("button", { name: "Decline POL-1" }));
    expect(await axe(document.body)).toHaveNoViolations();
  });
});
