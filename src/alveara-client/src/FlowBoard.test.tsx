import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { App } from "./App";
import { FakeFlowServer } from "./test/fakeFlowServer";
import { dateOf } from "./services/schedulingApi";
import type { PatientFlowState } from "./services/schedulingApi";
import { addDays, practiceNow } from "./pages/calendar/calendarLayout";

/**
 * ALV-011-C01: the live visit board through the real <App /> against an in-memory fake of the visit API. The state rules, the audit trail, the room lock and
 * the races are proven by the backend tests and the real-backend walkthrough; what is proven here is that the board shows exactly what the server stored,
 * offers only the moves the server says are next and the person's role may make, asks correctly (with the version it read), explains every outcome in
 * words, refreshes on its own without ever blanking, and gives people without the permission no way to change anything.
 */
const DAY = "2030-01-14"; // a Monday
const ANN = "pat-ann", BO = "pat-bo", CY = "pat-cy";
const ALL = ["ViewPatientRecords", "ViewSchedule", "ViewSignedForms", "UpdateVisitFlow", "UpdateChairsideFlow"];

let server: FakeFlowServer;

beforeEach(() => {
  server = new FakeFlowServer();
  server.serverNow = "2030-01-14T14:59:30.000Z"; // 08:59:30 practice time
  server.permissions = ALL;
  server.install();
});
afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

function visit(o: { start: string; flow?: PatientFlowState; patientId?: string; name?: string; provider?: string; operatory?: string; changed?: string; status?: string }) {
  const a = server.seed({ start: o.start, provider: o.provider, operatory: o.operatory, patientName: o.name ?? "Ann Lee", status: o.status, flowState: o.flow });
  a.patientId = o.patientId ?? ANN;
  if (o.changed) a.flowChangedAtUtc = o.changed;
  return a;
}

async function openBoard(day: string | null = DAY, user = userEvent) {
  window.history.pushState({}, "", "/flow");
  const view = render(<App />);
  const date = await screen.findByLabelText("Date");
  if (day) fireEvent.change(date, { target: { value: day } });
  await waitFor(() => expect(screen.getByRole("region", { name: /^Scheduled \(/ })).toBeInTheDocument());
  void user;
  return view;
}

const card = (id: string) => document.querySelector(`[data-visit="${id}"]`) as HTMLElement;
const column = (name: RegExp) => screen.getByRole("region", { name });
const posts = (suffix: string) => server.callsToSchedule("POST", "/api/visits").filter((c) => c.url.endsWith(suffix));
const puts = () => server.callsToSchedule("PUT", "/api/visits");
const press = (a: { id: string; patientName: string }, name: string) => userEvent.click(within(card(a.id)).getByRole("button", { name: `${name} ${a.patientName}` }));

describe("what the board shows", () => {
  it("has a column per state with a count, each patient in the column the server stored, and cancelled and no-show apart", async () => {
    visit({ start: `${DAY}T09:30`, name: "Ann Lee" });
    visit({ start: `${DAY}T10:00`, flow: "Confirmed", name: "Bo Kim" });
    visit({ start: `${DAY}T10:30`, flow: "Seated", name: "Cy Poe", provider: "prov-b", operatory: "op-2" });
    visit({ start: `${DAY}T08:00`, flow: "Completed", name: "Di Lo" });
    visit({ start: `${DAY}T11:00`, status: "Cancelled", name: "Ed Ray" });
    visit({ start: `${DAY}T11:30`, status: "NoShow", name: "Flo Ng" });
    await openBoard();

    for (const [heading, patient] of [[/^Scheduled \(1\)$/, "Ann Lee"], [/^Confirmed \(1\)$/, "Bo Kim"], [/^Seated \(1\)$/, "Cy Poe"], [/^Completed \(1\)$/, "Di Lo"]] as const)
      expect(within(column(heading)).getByRole("heading", { name: patient })).toBeInTheDocument();
    for (const heading of [/^Checked in \(0\)$/, /^Ready \(0\)$/, /^In treatment \(0\)$/, /^Checked out \(0\)$/]) {
      expect(within(column(heading)).getByText("No patients")).toBeInTheDocument();
      expect(column(heading)).toHaveClass("flow-column--empty"); // an empty column shrinks so the busy ones get the room
    }
    expect(column(/^Seated \(1\)$/)).not.toHaveClass("flow-column--empty");

    const closed = column(/^Cancelled and no-show \(2\)$/);
    expect(within(closed).getByRole("heading", { name: "Ed Ray" })).toBeInTheDocument();
    expect(within(closed).getByText(/· Cancelled/)).toBeInTheDocument(); // the status is written, never colour alone
    expect(within(closed).getByText(/· No-show/)).toBeInTheDocument();
    expect(within(closed).queryByRole("button")).not.toBeInTheDocument(); // they offer no moves
  });

  it("shows where each patient actually is, and where the visit was booked when that differs", async () => {
    const a = visit({ start: `${DAY}T09:30` });
    Object.assign(a, { visitProviderId: "prov-b", visitProviderName: "Dr. Patel", visitOperatoryId: "op-2", visitOperatoryName: "Op 2" });
    const b = visit({ start: `${DAY}T10:30`, name: "Bo Kim", patientId: BO });
    await openBoard();

    expect(within(card(a.id)).getByText("Dr. Patel · Op 2")).toBeInTheDocument();
    expect(within(card(a.id)).getByText("Booked: Dr. Rivera · Op 1")).toBeInTheDocument();
    expect(within(card(b.id)).getByText("Dr. Rivera · Op 1")).toBeInTheDocument();
    expect(within(card(b.id)).queryByText(/^Booked:/)).not.toBeInTheDocument();
  });

  it("shows elapsed time from the server's clock: when a waiting patient is due and how long a patient has been in this step", async () => {
    visit({ start: `${DAY}T09:30` });
    const seated = visit({ start: `${DAY}T08:00`, flow: "Seated", name: "Bo Kim", patientId: BO, provider: "prov-b", operatory: "op-2", changed: "2030-01-14T14:47:00.000Z" });
    await openBoard();

    expect(screen.getByText("Starts in 30 min")).toBeInTheDocument();
    expect(within(card(seated.id)).getByText("In this step 12 min")).toBeInTheDocument();
  });

  it("carries a visit left open from an earlier day onto today's board and says so", async () => {
    const today = dateOf(practiceNow("America/Chicago"));
    server.today = today;
    const earlier = addDays(today, -3);
    const old = visit({ start: `${earlier}T09:00`, flow: "Seated" });
    await openBoard(null);

    expect(within(column(/^Seated \(1\)$/)).getByRole("heading", { name: "Ann Lee" })).toBeInTheDocument();
    expect(within(card(old.id)).getByText(`Carried over from ${earlier} and still open.`)).toBeInTheDocument();
  });

  it("shows the check-in form cue accurately: nothing required, some outstanding, all complete - and none once the patient is in the chair", async () => {
    const none = visit({ start: `${DAY}T09:30`, name: "Ann Lee", patientId: ANN });
    const open = visit({ start: `${DAY}T10:00`, name: "Bo Kim", patientId: BO });
    const done = visit({ start: `${DAY}T10:30`, name: "Cy Poe", patientId: CY });
    const seated = visit({ start: `${DAY}T08:00`, flow: "Seated", name: "Di Lo", patientId: "pat-di", provider: "prov-b", operatory: "op-2" });
    server.readiness[ANN] = { ready: true, requiredCount: 0, completeCount: 0, items: [] };
    server.readiness[BO] = { ready: false, requiredCount: 2, completeCount: 1, items: [
      { templateId: "t1", templateKey: "privacy-notice", title: "Privacy notice", category: "Privacy", status: "SignedEarlierVersion", currentVersionNumber: 2, signedVersionNumber: 1 },
      { templateId: "t2", templateKey: "financial", title: "Financial policy", category: "Financial", status: "Complete", currentVersionNumber: 1, signedVersionNumber: 1 },
    ] };
    server.readiness[CY] = { ready: true, requiredCount: 2, completeCount: 2, items: [] };
    await openBoard();

    expect(within(card(none.id)).getByText("No forms are required at check-in.")).toBeInTheDocument();
    expect(within(card(open.id)).getByText("Forms: 1 of 2 complete.")).toBeInTheDocument();
    expect(within(card(open.id)).getByText("Privacy notice — Signed an earlier version (version 1; version 2 is current)")).toBeInTheDocument();
    expect(within(card(open.id)).queryByText(/Financial policy/)).not.toBeInTheDocument(); // only what is still outstanding is listed
    expect(within(card(done.id)).getByText("Required forms complete (2 of 2).")).toBeInTheDocument();
    expect(within(card(seated.id)).queryByText(/forms/i)).not.toBeInTheDocument();
  });
});

describe("who sees which buttons", () => {
  it("offers a front-office person confirm and check in on a waiting patient, and nothing chairside", async () => {
    server.permissions = ["ViewSchedule", "UpdateVisitFlow"];
    const a = visit({ start: `${DAY}T09:30` });
    const ci = visit({ start: `${DAY}T10:30`, flow: "CheckedIn", name: "Bo Kim", patientId: BO });
    await openBoard();

    expect(within(card(a.id)).getByRole("button", { name: "Confirm Ann Lee" })).toBeEnabled();
    expect(within(card(a.id)).getByRole("button", { name: "Check in Ann Lee" })).toBeEnabled();
    for (const name of ["Mark ready Bo Kim", "Start treatment Bo Kim", "Complete visit Bo Kim"]) expect(within(card(ci.id)).queryByRole("button", { name })).not.toBeInTheDocument();
  });

  it("offers a chairside person the chairside moves and not the front-office ones", async () => {
    server.permissions = ["ViewSchedule", "UpdateChairsideFlow"];
    const waiting = visit({ start: `${DAY}T09:30` });
    const ci = visit({ start: `${DAY}T10:30`, flow: "CheckedIn", name: "Bo Kim", patientId: BO });
    await openBoard();

    expect(within(card(waiting.id)).queryByRole("button", { name: /Confirm|Check in/ })).not.toBeInTheDocument();
    for (const name of ["Mark ready Bo Kim", "Start treatment Bo Kim", "Complete visit Bo Kim"]) expect(within(card(ci.id)).getByRole("button", { name })).toBeEnabled();
  });

  it("gives a person who can only look no buttons and says why", async () => {
    server.permissions = ["ViewSchedule"];
    const a = visit({ start: `${DAY}T09:30` });
    await openBoard();

    expect(within(card(a.id)).queryByRole("button")).not.toBeInTheDocument();
    expect(screen.getByText("You can see the board but your role cannot change it.")).toBeInTheDocument();
  });
});

describe("making a move", () => {
  it("is a single press, asks with the version it read, moves the card to its new column from what the server returns, announces it and keeps focus", async () => {
    const a = visit({ start: `${DAY}T09:30` });
    await openBoard();
    const readsBefore = server.boardReads;

    await press(a, "Check in");

    expect(await screen.findByText("Ann Lee is now checked in.")).toBeInTheDocument();
    expect(posts(`/${a.id}/state`).at(-1)!.body).toEqual({ target: "CheckedIn", rowVersion: "a1" });
    await waitFor(() => expect(within(column(/^Checked in \(1\)$/)).getByRole("heading", { name: "Ann Lee" })).toBeInTheDocument());
    expect(a.flowState).toBe("CheckedIn");
    await waitFor(() => expect(server.boardReads).toBeGreaterThan(readsBefore)); // and the whole board was read again from the server
    expect(document.activeElement).toBe(document.getElementById(`visit-title-${a.id}`)); // the pressed button is gone: focus stays on the visit
    expect(within(card(a.id)).getByRole("button", { name: "Mark ready Ann Lee" })).toBeInTheDocument(); // and the next moves are offered
  });

  it("asks before completing a visit, because that is final, and a refusal to confirm changes nothing", async () => {
    const a = visit({ start: `${DAY}T09:30`, flow: "InTreatment", operatory: "op-1" });
    await openBoard();

    await press(a, "Complete visit");
    expect(within(card(a.id)).getByText("Complete the visit for Ann Lee? This cannot be undone.")).toBeInTheDocument();
    expect(posts(`/${a.id}/state`)).toHaveLength(0);
    await userEvent.click(within(card(a.id)).getByRole("button", { name: "Keep it open" }));
    expect(posts(`/${a.id}/state`)).toHaveLength(0);
    expect(a.flowState).toBe("InTreatment");

    await press(a, "Complete visit");
    await userEvent.click(within(card(a.id)).getByRole("button", { name: "Yes, complete the visit" }));
    await waitFor(() => expect(within(column(/^Completed \(1\)$/)).getByRole("heading", { name: "Ann Lee" })).toBeInTheDocument());
    expect(posts(`/${a.id}/state`).at(-1)!.body).toMatchObject({ target: "Completed" });
  });

  it("walks a patient down the chain one press at a time, each press offering the next moves", async () => {
    const a = visit({ start: `${DAY}T09:30` });
    await openBoard();
    for (const [button, column_] of [["Check in", /^Checked in/], ["Mark ready", /^Ready/], ["Seat patient", /^Seated/], ["Start treatment", /^In treatment/], ["Check out", /^Checked out/]] as const) {
      await press(a, button);
      await waitFor(() => expect(within(column(column_)).getByRole("heading", { name: "Ann Lee" })).toBeInTheDocument());
    }
    expect(a.flowState).toBe("CheckedOut");
  });

  it("says a room is occupied in plain words, names the room, and leaves the patient where they were", async () => {
    visit({ start: `${DAY}T09:00`, flow: "Seated", name: "Bo Kim", patientId: BO });
    const waiting = visit({ start: `${DAY}T11:00`, flow: "Ready", name: "Ann Lee" });
    await openBoard();

    await press(waiting, "Seat patient");

    expect(await within(card(waiting.id)).findByRole("alert")).toHaveTextContent("Op 1 already has a patient who is seated or in treatment. Nothing was changed.");
    expect(within(column(/^Ready \(1\)$/)).getByRole("heading", { name: "Ann Lee" })).toBeInTheDocument();
    expect(waiting.flowState).toBe("Ready");
    expect(within(card(waiting.id)).getByRole("button", { name: "Seat patient Ann Lee" })).toBeEnabled(); // and can be tried again
  });

  it("reports a change someone else made first as a conflict, applies nothing, and reloading shows the current state", async () => {
    const a = visit({ start: `${DAY}T09:30` });
    await openBoard();
    server.moveBehindTheScenes(a.id, "Confirmed"); // another user confirmed the patient while this card was on screen

    await press(a, "Check in");

    expect(await screen.findByText("Someone else changed this while you were editing")).toBeInTheDocument();
    expect(a.flowState).toBe("Confirmed"); // not checked in on top of their change
    await userEvent.click(screen.getByRole("button", { name: /Reload/ }));
    await waitFor(() => expect(within(column(/^Confirmed \(1\)$/)).getByRole("heading", { name: "Ann Lee" })).toBeInTheDocument());
    expect(screen.queryByText("Someone else changed this while you were editing")).not.toBeInTheDocument();
  });

  it("says it cannot tell when the connection drops after the server applied the move, and pressing again does not move the patient twice", async () => {
    const a = visit({ start: `${DAY}T09:30` });
    await openBoard();
    server.dropNextMoveTo = "CheckedIn";

    await press(a, "Check in");
    expect(await within(card(a.id)).findByRole("alert")).toHaveTextContent("could not confirm whether that was saved");
    expect(a.flowState).toBe("CheckedIn"); // it was applied

    await press(a, "Check in"); // the card still says Scheduled and holds the old version: the server sees the patient is already in
    expect(await screen.findByText("Ann Lee is now checked in.")).toBeInTheDocument();
    expect(a.flowState).toBe("CheckedIn");
  });
});

describe("changing the room or provider", () => {
  it("asks for both, saves with the version it read, shows the new place and keeps the booking visible", async () => {
    const a = visit({ start: `${DAY}T09:30` });
    await openBoard();

    await userEvent.click(within(card(a.id)).getByRole("button", { name: "Change room or provider for Ann Lee" }));
    const form = within(card(a.id)).getByRole("form", { name: "Change room or provider for Ann Lee" });
    expect(within(form).getByRole("button", { name: "Save" })).toBeDisabled(); // nothing changed yet
    await userEvent.selectOptions(within(form).getByLabelText("Provider"), "Dr. Patel");
    await userEvent.selectOptions(within(form).getByLabelText("Operatory"), "Op 2");
    await userEvent.click(within(form).getByRole("button", { name: "Save" }));

    expect(await screen.findByText("Ann Lee was moved to the new room or provider.")).toBeInTheDocument();
    expect(puts().at(-1)!.body).toEqual({ providerId: "prov-b", operatoryId: "op-2", rowVersion: "a1" });
    await waitFor(() => expect(within(card(a.id)).getByText("Dr. Patel · Op 2")).toBeInTheDocument());
    expect(within(card(a.id)).getByText("Booked: Dr. Rivera · Op 1")).toBeInTheDocument();
    expect(a.providerName).toBe("Dr. Rivera"); // the booking itself did not move
  });

  it("explains an occupied room and leaves the visit where it was; Cancel closes the form", async () => {
    visit({ start: `${DAY}T09:00`, flow: "Seated", name: "Bo Kim", patientId: BO, provider: "prov-b", operatory: "op-2" });
    const seated = visit({ start: `${DAY}T11:00`, flow: "Seated", name: "Ann Lee" });
    await openBoard();

    await userEvent.click(within(card(seated.id)).getByRole("button", { name: "Change room or provider for Ann Lee" }));
    await userEvent.selectOptions(within(card(seated.id)).getByLabelText("Operatory"), "Op 2");
    await userEvent.click(within(card(seated.id)).getByRole("button", { name: "Save" }));
    expect(await within(card(seated.id)).findByRole("alert")).toHaveTextContent("Op 2 already has a patient who is seated or in treatment. Nothing was changed.");
    expect(seated.visitOperatoryId).toBeUndefined();

    await userEvent.click(within(card(seated.id)).getByRole("button", { name: "Change room or provider for Ann Lee" }));
    await userEvent.click(within(card(seated.id)).getByRole("button", { name: "Cancel" }));
    expect(within(card(seated.id)).queryByRole("form")).not.toBeInTheDocument();
  });

  it("is not offered to a finished visit, to a cancelled one, or to someone who cannot change visits", async () => {
    const done = visit({ start: `${DAY}T09:00`, flow: "Completed", name: "Bo Kim", patientId: BO });
    visit({ start: `${DAY}T10:00`, status: "Cancelled", name: "Cy Poe" });
    await openBoard();
    expect(within(card(done.id)).queryByRole("button", { name: /Change room/ })).not.toBeInTheDocument();
    expect(screen.queryAllByRole("button", { name: /Change room/ })).toHaveLength(0);
  });
});

describe("staying live without ever blanking", () => {
  it("re-reads the board on its own every 15 seconds so what someone else did shows up", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const a = visit({ start: `${DAY}T09:30` });
    await openBoard();
    expect(within(column(/^Scheduled \(1\)$/)).getByRole("heading", { name: "Ann Lee" })).toBeInTheDocument();
    const reads = server.boardReads;

    server.moveBehindTheScenes(a.id, "CheckedIn"); // another workstation checks the patient in
    await vi.advanceTimersByTimeAsync(15_000);

    await waitFor(() => expect(within(column(/^Checked in \(1\)$/)).getByRole("heading", { name: "Ann Lee" })).toBeInTheDocument());
    expect(server.boardReads).toBeGreaterThan(reads);
  });

  it("keeps the last good board and says it is out of date when a refresh fails, then recovers", async () => {
    visit({ start: `${DAY}T09:30` });
    await openBoard();
    server.failBoardReads = 1;

    await userEvent.click(screen.getByRole("button", { name: "Refresh now" }));

    expect(await screen.findByText(/Could not refresh just now - this is the board as of/)).toBeInTheDocument();
    expect(within(column(/^Scheduled \(1\)$/)).getByRole("heading", { name: "Ann Lee" })).toBeInTheDocument(); // still there
    await userEvent.click(screen.getByRole("button", { name: "Refresh now" }));
    await waitFor(() => expect(screen.queryByText(/Could not refresh just now/)).not.toBeInTheDocument());
  });

  it("gives up on a read that takes longer than 10 seconds, keeps the board, and recovers when the server answers again", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    visit({ start: `${DAY}T09:30` });
    await openBoard();
    server.hangBoard = true;

    fireEvent.click(screen.getByRole("button", { name: "Refresh now" }));
    await vi.advanceTimersByTimeAsync(9_000);
    expect(screen.queryByText(/Could not refresh just now/)).not.toBeInTheDocument(); // still waiting
    await vi.advanceTimersByTimeAsync(1_500);

    expect(await screen.findByText(/Could not refresh just now/)).toBeInTheDocument();
    expect(within(column(/^Scheduled \(1\)$/)).getByRole("heading", { name: "Ann Lee" })).toBeInTheDocument();
    server.hangBoard = false;
    fireEvent.click(screen.getByRole("button", { name: "Refresh now" }));
    await waitFor(() => expect(screen.queryByText(/Could not refresh just now/)).not.toBeInTheDocument());
  });

  it("never shows one day's visits under another day's heading when the date changes", async () => {
    visit({ start: `${DAY}T09:30` });
    await openBoard();

    await userEvent.click(screen.getByRole("button", { name: "Next day" }));

    await waitFor(() => expect(screen.getByRole("heading", { name: /2030-01-15/ })).toBeInTheDocument());
    await waitFor(() => expect(within(column(/^Scheduled \(0\)$/)).getByText("No patients")).toBeInTheDocument());
    expect(screen.queryByRole("heading", { name: "Ann Lee" })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Previous day" }));
    expect(await screen.findByRole("heading", { name: "Ann Lee" })).toBeInTheDocument();
  });
  it("shows loading - not the previous day's patients - while the next day's board is still on its way", async () => {
    visit({ start: `${DAY}T09:30` });
    await openBoard();
    server.hangBoard = true; // the next read does not answer yet

    await userEvent.click(screen.getByRole("button", { name: "Next day" }));

    expect(await screen.findByRole("heading", { name: /2030-01-15/ })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Ann Lee" })).not.toBeInTheDocument(); // never Monday's patients under Tuesday's heading
    expect(screen.queryByRole("region", { name: /^Scheduled \(/ })).not.toBeInTheDocument();
    expect(screen.getByText("Loading the board…")).toBeInTheDocument();
    server.hangBoard = false;
    await userEvent.click(screen.getByRole("button", { name: "Refresh now" }));
    await waitFor(() => expect(within(column(/^Scheduled \(0\)$/)).getByText("No patients")).toBeInTheDocument());
  });
});

describe("accessibility", () => {
  it("a busy board in every state has no axe violations and a named region per column", async () => {
    visit({ start: `${DAY}T09:30`, name: "Ann Lee" });
    visit({ start: `${DAY}T10:00`, flow: "Ready", name: "Bo Kim", patientId: BO });
    visit({ start: `${DAY}T10:30`, flow: "Seated", name: "Cy Poe", patientId: CY, provider: "prov-b", operatory: "op-2", changed: "2030-01-14T14:47:00.000Z" });
    visit({ start: `${DAY}T11:00`, status: "Cancelled", name: "Di Lo", patientId: "pat-di" });
    server.readiness[BO] = { ready: false, requiredCount: 1, completeCount: 0, items: [{ templateId: "t1", templateKey: "p", title: "Privacy notice", category: "Privacy", status: "Missing", currentVersionNumber: 1, signedVersionNumber: null }] };
    const view = await openBoard();

    expect(screen.getAllByRole("region").length).toBeGreaterThanOrEqual(9); // eight states and the closed group
    expect(await axe(view.container)).toHaveNoViolations();
  });
});
