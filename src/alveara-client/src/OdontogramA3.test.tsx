import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { App } from "./App";
import { FakeClinicalServer } from "./test/fakeClinicalServer";
import { makePatient } from "./test/fakePatientServer";
import { isDrawnAbsent } from "./pages/odontogram/odontogramText";
import type { Finding } from "./services/odontogramApi";

/**
 * ALV-N022 A3, the odontogram's display corrections, through the real <App /> against the in-memory fake (the same setup as Odontogram.test):
 * - F-091 (OD-09, display only): the absent mark ONLY for an active Absent-condition finding in Existing or Completed; Diagnosed/Planned show their state without it.
 * - F-095: Planned and Completed are explained as chart states that create no plan or completed procedure.
 * - F-094: recording the identical finding again says "Already recorded: nothing changed."; F-093: selecting another tooth clears the last message.
 */
const A = "aaaaaaaa-0000-0000-0000-000000000001";
let server: FakeClinicalServer;

async function openChart() {
  window.history.pushState({}, "", `/patients/${A}/odontogram`);
  render(<App />);
  await screen.findByRole("heading", { name: "Odontogram" });
}
const tooth = (label: string) => screen.getByRole("button", { name: new RegExp(`^Tooth ${label},`) });

beforeEach(() => {
  server = new FakeClinicalServer();
  server.add(makePatient({ id: A, firstName: "Ann", lastName: "Lee" }));
  server.install();
});
afterEach(() => vi.unstubAllGlobals());

describe("F-091 the absent mark", () => {
  const f = (over: Partial<Finding>): Finding => ({ id: "f", toothKey: "16", surface: null, condition: "Missing", conditionLabel: "Missing tooth", conditionScope: "WholeTooth", state: "Existing",
    status: "Active", recordedByName: null, recordedAtUtc: "2030-01-01T00:00:00Z", updatedByName: null, updatedAtUtc: null, rowVersion: "r", links: [], ...over });
  const absent = new Set(["Missing"]);

  it("is drawn only for an active Absent-condition finding in Existing or Completed", () => {
    expect(isDrawnAbsent([f({ state: "Existing" })], absent)).toBe(true);
    expect(isDrawnAbsent([f({ state: "Completed" })], absent)).toBe(true);
    expect(isDrawnAbsent([f({ state: "Diagnosed" })], absent)).toBe(false);
    expect(isDrawnAbsent([f({ state: "Planned" })], absent)).toBe(false);
    expect(isDrawnAbsent([f({ status: "Withdrawn" })], absent)).toBe(false);
    expect(isDrawnAbsent([f({ condition: "Crown", conditionLabel: "Crown" })], absent)).toBe(false);
  });

  it("marks the tile and its name, and leaves a Diagnosed missing tooth with only its state", async () => {
    server.odontogram.addFinding(A, { toothKey: "36", condition: "Missing", state: "Existing" });   // Universal 19
    server.odontogram.addFinding(A, { toothKey: "46", condition: "Missing", state: "Diagnosed" });  // Universal 30
    await openChart();
    expect(tooth("19")).toHaveAccessibleName(/, not present$/);
    expect(tooth("19").querySelector("[data-absent-mark]")).not.toBeNull();
    expect(tooth("30")).not.toHaveAccessibleName(/not present/);
    expect(tooth("30").querySelector("[data-absent-mark]")).toBeNull();
    expect(tooth("30").querySelector(".alv-odonto__tooth-state")!.textContent).toBe("Diagnosed");
  });

  it("lists an implant and a missing finding on the same tooth both as recorded", async () => {
    server.odontogram.addFinding(A, { toothKey: "36", condition: "Missing", state: "Existing" });
    server.odontogram.addFinding(A, { toothKey: "36", condition: "Implant", state: "Existing" });
    await openChart();
    await userEvent.click(tooth("19"));
    const list = within(screen.getByRole("region", { name: /^Tooth 19:/ })).getByRole("list", { name: /^Findings on/ });
    expect(list).toHaveTextContent(/Missing tooth/);
    expect(list).toHaveTextContent(/Implant/);
  });
});

describe("F-095, F-093, F-094", () => {
  it("explains Planned and Completed as chart states only", async () => {
    await openChart();
    expect(screen.getByTestId("plan-complete-explained")).toHaveTextContent("They do not create a treatment plan or record a completed procedure");
  });

  it("says an identical repeat changed nothing, and clears the message when another tooth is selected", async () => {
    const user = userEvent.setup();
    await openChart();
    await user.click(tooth("8"));
    const record = async () => {
      await user.click(screen.getByRole("button", { name: "Record a finding on this tooth" }));
      const form = screen.getByRole("form", { name: "Record a finding" });
      await user.selectOptions(within(form).getByLabelText("Condition"), "Crown");
      await user.selectOptions(within(form).getByLabelText("State"), "Existing");
      await user.click(within(form).getByRole("button", { name: "Record finding" }));
    };
    await record();
    const status = () => document.querySelector(".alv-clinical__status")!;
    await vi.waitFor(() => expect(status().textContent).not.toMatch(/Saving|Already recorded/));
    const first = status().textContent;
    await record();
    await vi.waitFor(() => expect(status()).toHaveTextContent("Already recorded: nothing changed."));
    expect(first).not.toBe("Already recorded: nothing changed.");
    await user.click(tooth("9"));
    expect(status()).not.toHaveTextContent("Already recorded");
  });
});
