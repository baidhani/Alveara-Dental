import { describe, it, expect } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { axe } from "jest-axe";
import type { AppointmentEvent } from "../../services/schedulingApi";
import { AppointmentHistory } from "./AppointmentHistory";

/**
 * ALV-N021 A2 (F-046, F-049, F-055, F-062, F-074; M4): the history table says who did each thing and, for a move, where it went from and to exactly as the server recorded it. An event from before
 * M4 has no recorded destination and says only where it came from. The rules that decide what is recorded are proven by AppointmentMoveHistoryTests on the server.
 */
const event = (over: Partial<AppointmentEvent>): AppointmentEvent => ({
  eventType: "Scheduled", actorUserId: "u1", actorName: "Dana Desk", occurredAtUtc: "2030-01-14T13:00:00Z", detail: null,
  previousStartLocal: null, previousProviderName: null, previousOperatoryName: null, ...over,
});

const rowOf = (what: string) => {
  const cell = within(screen.getByRole("table", { name: /What happened to this appointment/ })).getByText(what, { selector: "td" });
  return within(cell.closest("tr") as HTMLElement);
};

describe("the appointment history", () => {
  it("names who did each thing, and says nobody rather than guessing when no person was recorded", () => {
    render(<AppointmentHistory history={[event({ detail: "Scheduled for 60 minutes." }), event({ eventType: "NotesChanged", actorUserId: null, actorName: null, detail: "Note updated." })]} />);
    expect(rowOf("Scheduled").getByText("Dana Desk")).toBeInTheDocument();
    expect(rowOf("Scheduled").getByText("Scheduled for 60 minutes.")).toBeInTheDocument();
    expect(rowOf("Note changed").getByText("—")).toBeInTheDocument();
  });

  it("says where a reschedule went from and to, as recorded", () => {
    render(<AppointmentHistory history={[event({
      eventType: "Rescheduled", detail: "Rescheduled for 60 minutes.",
      previousStartLocal: "2030-01-14T09:00", previousProviderName: "Dr. Rivera", previousOperatoryName: "Op 1",
      newStartLocal: "2030-01-14T11:00", newProviderName: "Dr. Patel", newOperatoryName: "Op 2",
    })]} />);
    expect(rowOf("Rescheduled").getByText("From 2030-01-14 09:00 with Dr. Rivera in Op 1 to 2030-01-14 11:00 with Dr. Patel in Op 2")).toBeInTheDocument();
  });

  it("an event recorded before the destination was kept says only where it came from", () => {
    render(<AppointmentHistory history={[event({
      eventType: "Rescheduled", detail: "Rescheduled for 60 minutes.", previousStartLocal: "2030-01-14T09:00", previousProviderName: "Dr. Rivera", previousOperatoryName: "Op 1",
    })]} />);
    expect(rowOf("Rescheduled").getByText("Was 2030-01-14 09:00 with Dr. Rivera in Op 1")).toBeInTheDocument();
    expect(screen.queryByText(/^From /)).not.toBeInTheDocument();                       // no invented destination
  });

  it("a provider change says the provider and, when it moved too, the room from and to, with the reason and without repeating its name", () => {
    render(<AppointmentHistory history={[event({
      eventType: "ProviderReassigned", detail: "Provider and room changed.", reason: "Dr. Rivera called in sick",
      previousProviderName: "Dr. Rivera", previousOperatoryName: "Op 1", newProviderName: "Dr. Patel", newOperatoryName: "Op 2",
    })]} />);
    expect(rowOf("Provider changed").getByText("Provider from Dr. Rivera to Dr. Patel; room from Op 1 to Op 2 · Reason: Dr. Rivera called in sick")).toBeInTheDocument();
    expect(screen.queryByText(/Provider and room changed/)).not.toBeInTheDocument();
  });

  it("a room change says only the room", () => {
    render(<AppointmentHistory history={[event({ eventType: "RoomChanged", detail: "Room changed.", previousProviderName: "Dr. Rivera", previousOperatoryName: "Op 1", newOperatoryName: "Op 2" })]} />);
    expect(rowOf("Room changed").getByText("Room from Op 1 to Op 2")).toBeInTheDocument();
  });

  it("says a visit move in words, keeps the minutes late, and does not repeat a no-show's name", () => {
    render(<AppointmentHistory history={[
      event({ eventType: "CheckedIn", detail: "Confirmed -> CheckedIn (12 minutes late)" }),
      event({ eventType: "CheckedOut", detail: "InTreatment -> CheckedOut" }),
      event({ eventType: "NoShow", detail: "Marked as a no-show." }),
    ]} />);
    expect(rowOf("Patient checked in").getByText("Confirmed to Checked in (12 minutes late)")).toBeInTheDocument();
    expect(rowOf("Ready for checkout").getByText("In treatment to Ready for checkout")).toBeInTheDocument();
    expect(rowOf("Marked as a no-show").getByText("—")).toBeInTheDocument();
    expect(screen.queryByText(/->/)).not.toBeInTheDocument();
  });

  it("has no accessibility violations", async () => {
    const { container } = render(<AppointmentHistory history={[event({ detail: "Scheduled for 60 minutes." })]} />);
    expect(await axe(container)).toHaveNoViolations();
  });
});
