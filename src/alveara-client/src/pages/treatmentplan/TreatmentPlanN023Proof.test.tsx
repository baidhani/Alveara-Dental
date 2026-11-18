import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { P, item, patient, plan, stubApi } from "../../test/treatmentPlanFixtures";
import type { ClinicalHistoryEntry } from "../../services/treatmentPlansApi";
import { AuthContext } from "../../contexts/authContextObject";
import type { AuthContextValue } from "../../contexts/authContextObject";
import { TreatmentPlansPanel } from "./TreatmentPlansPanel";

/**
 * ALV-N023: the three client proofs brief section 6 names that the slice tests did not assert (found while mapping the acceptance evidence for R01):
 * F-102 the account entry links to the patient's account only for a billing reader; DR-8 (Q-5af) the visible "performed N days after the selected encounter" note;
 * D-03 the plan's actions sit in labelled groups apart from each procedure's own actions.
 */
const authoritative = (over: Partial<ClinicalHistoryEntry> = {}): ClinicalHistoryEntry => ({
  planId: "pl1", planTitle: "Gum health plan", itemId: "it1", itemNumber: 1, procedureCodeSystem: "Local", procedureCode: "PERIO-1", procedureDescription: "Periodontal maintenance", toothKey: null, surface: null,
  completedByName: "Dr. Okafor", completedAtUtc: "2030-02-03T10:00:00Z", planRevisionAtCompletion: 1,
  completedProcedureId: "cp1", providerName: "Dr. Okafor", performedAtUtc: "2030-02-03T10:00:00Z", encounterId: "e1", chargeEntryNumber: 3, chargeAmount: 120, chartOutcome: null, ...over,
});
const asUser = (permissions: string[]) => ({ hasPermission: (p: string) => permissions.includes(p) }) as unknown as AuthContextValue;

async function open(permissions: string[], canWrite = false) {
  render(
    <MemoryRouter>
      <AuthContext.Provider value={asUser(permissions)}>
        <TreatmentPlansPanel patient={patient} canWrite={canWrite} />
      </AuthContext.Provider>
    </MemoryRouter>,
  );
  await screen.findByRole("heading", { name: "Plans on record" });
}
const historyRow = async () => within(await screen.findByRole("list", { name: "Completed procedures" })).getAllByRole("listitem")[0];

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("F-102: the account entry", () => {
  it("is a link to the patient's account for someone who may read billing", async () => {
    stubApi({ history: [authoritative()] });
    await open(["ViewClinicalDocumentation", "ViewBilling"]);
    const link = within(await historyRow()).getByRole("link", { name: "account entry 3" });
    expect(link).toHaveAttribute("href", `/patients/${P}/billing`);
  });

  it("is plain text, with no link, for someone who may not", async () => {
    stubApi({ history: [authoritative()] });
    await open(["ViewClinicalDocumentation"]);
    const row = await historyRow();
    expect(row).toHaveTextContent("charged $120.00 (account entry 3)");
    expect(within(row).queryByRole("link")).not.toBeInTheDocument();
  });
});

describe("DR-8 (Q-5af): the performed-after-encounter note", () => {
  it("says how many days after the selected encounter the procedure was performed, and says nothing when it was not after", async () => {
    stubApi({ history: [authoritative({ performedAfterEncounterDays: 2 }), authoritative({ itemId: "it2", itemNumber: 2, procedureCode: "POL-1", performedAfterEncounterDays: null })] });
    await open(["ViewClinicalDocumentation"]);
    const rows = within(await screen.findByRole("list", { name: "Completed procedures" })).getAllByRole("listitem");
    expect(rows[0]).toHaveTextContent("performed 2 days after the selected encounter");
    expect(rows[1]).not.toHaveTextContent("after the selected encounter");
  });
});

describe("D-03: grouped actions", () => {
  it("puts the plan's actions in labelled groups and keeps each procedure's own actions out of them", async () => {
    stubApi({ plans: [plan({ items: [item()] })] });
    await open(["ViewClinicalDocumentation"], true);
    const answers = screen.getByRole("group", { name: "Answers and completion" });
    const changes = screen.getByRole("group", { name: "Plan changes" });
    const withdraw = screen.getByRole("group", { name: "Withdraw" });
    expect(within(answers).getByRole("button", { name: "Accept all unanswered" })).toBeInTheDocument();
    expect(within(changes).getByRole("button", { name: "Rename plan" })).toBeInTheDocument();
    expect(within(withdraw).getByRole("button", { name: "Withdraw plan" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Accept PERIO-1" })).toBeInTheDocument();              // the procedure's own action exists ...
    for (const group of [answers, changes, withdraw]) expect(within(group).queryByRole("button", { name: "Accept PERIO-1" })).not.toBeInTheDocument();   // ... but in no plan group
  });
});
