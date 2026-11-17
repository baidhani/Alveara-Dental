import { describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ChartSectionPanel } from "./ChartSectionPanel";
import type { EncounterSection, ExistingItem } from "../../services/clinicalApi";
import type { RecordItem } from "../../services/clinicalRecordApi";

// ALV-N022 A1 (Addendum 02): an encounter's allergy / medication / medical-history section shows the ONE chart. A refused add points to the existing item: the screen offers
// "update" (active) or "reactivate or update" (not active) with NO status preselected, medications and history may instead confirm the list, and cancel changes nothing.

const item = (over: Partial<RecordItem> = {}): RecordItem => ({
  id: "i1", kind: "Allergy", name: "Penicillin", detail: null, reaction: "Hives", severity: null, dose: null, frequency: null, status: "Active",
  createdAtUtc: "2026-11-01T10:00:00Z", createdByName: "Dee Dentist", updatedAtUtc: null, updatedByName: null, rowVersion: "AAAA", ...over,
});

const section = (over: Partial<EncounterSection> = {}): EncounterSection => ({
  kind: "Allergy", status: "Empty", entries: [], reviewedAtUtc: null, reviewedByUserId: null, source: "Chart", chartItems: [item()], recordedHere: [], reviewedHere: null,
  contradiction: null, ...over,
});

function setup(sec: EncounterSection, existing: ExistingItem | null = null, editable = true) {
  const props = {
    onDismissExisting: vi.fn(), onAdd: vi.fn(async () => null), onUpdate: vi.fn(async () => null), onSetStatus: vi.fn(async () => null), onRemove: vi.fn(async () => null),
    onConfirm: vi.fn(async () => {}), onMarkNone: vi.fn(async () => {}), onClearReview: vi.fn(async () => {}),
  };
  render(<ChartSectionPanel section={sec} editable={editable} busy={false} existing={existing} {...props} />);
  return props;
}

const noWrites = (p: ReturnType<typeof setup>) =>
  [p.onAdd, p.onUpdate, p.onSetStatus, p.onRemove, p.onConfirm, p.onMarkNone, p.onClearReview].forEach((f) => expect(f).not.toHaveBeenCalled());

describe("ChartSectionPanel", () => {
  it("lists the chart's items and, for a resolved match, offers reactivation with no status chosen until the user picks one", async () => {
    const resolved = item({ status: "Resolved" });
    const p = setup(section({ chartItems: [resolved] }), { id: "i1", kind: "Allergy", status: "Resolved" });
    const alert = screen.getByRole("alert");
    expect(alert).toHaveTextContent("Penicillin is already on the chart as resolved. Nothing was added.");
    expect(within(alert).queryByRole("button", { name: /confirm the list/i })).toBeNull(); // allergies: no "confirm current" escape

    await userEvent.click(within(alert).getByRole("button", { name: "Reactivate or update the existing item" }));
    const select = screen.getByLabelText(/new status for penicillin/i) as HTMLSelectElement;
    expect(select.value).toBe("");                                                  // nothing preselected
    expect(screen.getByRole("button", { name: "Save status" })).toBeDisabled();
    expect(p.onSetStatus).not.toHaveBeenCalled();

    await userEvent.selectOptions(select, "Active");
    await userEvent.click(screen.getByRole("button", { name: "Save status" }));
    expect(p.onSetStatus).toHaveBeenCalledWith(resolved, "Active");
  });

  it("for an active match offers to update the existing item", () => {
    setup(section(), { id: "i1", kind: "Allergy", status: "Active" });
    expect(screen.getByRole("alert")).toHaveTextContent("Penicillin is already on the chart. Nothing was added.");
    expect(screen.getByRole("button", { name: "Update the existing item" })).toBeEnabled();
  });

  it("cancel closes the notice and changes nothing", async () => {
    const p = setup(section(), { id: "i1", kind: "Allergy", status: "Active" });
    await userEvent.click(within(screen.getByRole("alert")).getByRole("button", { name: "Cancel" }));
    expect(p.onDismissExisting).toHaveBeenCalled();
    noWrites(p);
  });

  it("for a medication already listed also offers to confirm the list is current", async () => {
    const med = item({ kind: "Medication", name: "Lisinopril", reaction: null, dose: "10 mg" });
    const p = setup(section({ kind: "Medication", chartItems: [med] }), { id: "i1", kind: "Medication", status: "Active" });
    await userEvent.click(within(screen.getByRole("alert")).getByRole("button", { name: "Confirm the list is current" }));
    expect(p.onConfirm).toHaveBeenCalledWith("Medication");
  });

  it("offers Remove only for an item added in this encounter", () => {
    const here = item({ id: "i2", name: "Latex" });
    setup(section({
      status: "Recorded", chartItems: [item(), here],
      recordedHere: [{ itemId: "i2", versionNumber: 1, changeType: "Added", name: "Latex", status: "Active", occurredAtUtc: "2026-11-17T09:00:00Z", actorName: "Dee Dentist" }],
    }));
    expect(screen.getByRole("button", { name: "Remove Latex" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Remove Penicillin" })).toBeNull();
    expect(screen.getByText(/Recorded on the chart in this encounter/)).toBeInTheDocument();
  });

  it("offers 'none reported' only while no item is active", () => {
    setup(section());
    expect(screen.queryByRole("button", { name: /Reviewed - none reported/ })).toBeNull();
  });

  it("offers 'none reported' when the only items are resolved", () => {
    setup(section({ chartItems: [item({ status: "Resolved" })] }));
    expect(screen.getByRole("button", { name: /Reviewed - none reported/ })).toBeInTheDocument();
  });

  it("shows a stored contradiction and keeps both records on screen", () => {
    setup(section({ status: "NoneReported", contradiction: "Contradiction: marked none reported while 1 active item was listed on the chart. Both records are kept." }));
    expect(screen.getByRole("note")).toHaveTextContent(/Contradiction/);
    expect(screen.getByText("Penicillin")).toBeInTheDocument();
  });

  it("is read-only when the encounter cannot be edited", () => {
    setup(section(), null, false);
    expect(screen.queryAllByRole("button")).toHaveLength(0);
  });
});
