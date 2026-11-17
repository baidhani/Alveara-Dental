import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { App } from "./App";
import { FakeClinicalServer } from "./test/fakeClinicalServer";
import { makePatient } from "./test/fakePatientServer";
import { displaySex } from "./components/patientFields";
import { checkValues, emptyValues } from "./pages/externalcases/caseFormValues";
import { TreatmentPlanItemFields } from "./pages/treatmentplan/TreatmentPlanItemFields";
import { EMPTY_ITEM } from "./pages/treatmentplan/treatmentPlanRules";
import type { ItemDraft } from "./pages/treatmentplan/treatmentPlanRules";
import type { Diagnosis } from "./services/diagnosisApi";
import type { PlanningProcedure } from "./services/treatmentPlansApi";
import { backToPatient, templatesPath } from "./pages/clinical/notes/templatesPath";

/** ALV-N022 A3 slice 4, one focused case each: F-067, F-082, F-137, Q-5ai, Q-5j. */
const A = "aaaaaaaa-0000-0000-0000-000000000001";
let server: FakeClinicalServer;
const open = (path: string) => { window.history.pushState({}, "", path); render(<App />); };

beforeEach(() => {
  server = new FakeClinicalServer();
  server.add(makePatient({ id: A, firstName: "Ann", lastName: "Lee", sex: "female" }));
  server.install();
});
afterEach(() => vi.unstubAllGlobals());

describe("F-067 sex shown in one form", () => {
  it("shows the known values one way, anything else as stored", () => {
    expect(["male", "Male", " M ", "MALE"].map(displaySex)).toEqual(["Male", "Male", "Male", "Male"]);
    expect(["female", "f"].map(displaySex)).toEqual(["Female", "Female"]);
    expect(displaySex("Intersex")).toBe("Intersex");
    expect(displaySex(null)).toBe("");
  });

  it("the patient header shows an imported lowercase value as Female", async () => {
    open(`/patients/${A}`);
    const header = await screen.findByTestId("patient-header");
    expect(header).toHaveTextContent("Female");
    expect(header).not.toHaveTextContent(/\bfemale\b/);
  });
});

describe("F-082 the encounter row says what it counts", () => {
  it("names the count as history, allergy and medication entries", async () => {
    server.addEncounter(A, { id: "e1" });
    open(`/patients/${A}/clinical`);
    expect(await screen.findByText(/0 history, allergy and medication entries/)).toBeInTheDocument();
    expect(screen.queryByText(/· 0 entries/)).not.toBeInTheDocument();
  });
});

describe("F-137 the lab-case tooth is checked in the form", () => {
  it("flags a tooth that is not on the chart together with the other problems", () => {
    const found = checkValues("Lab", { ...emptyValues(), summary: "Crown", toothKey: "99" }, "");
    expect(found.toothKey).toBe("Choose a tooth from the chart (its two-digit number), or leave it blank.");
    expect(found.party).toBe("Choose the laboratory.");                                     // both at once, not one at a time
    expect(checkValues("Lab", { ...emptyValues(), summary: "Crown", toothKey: "36" }, "lab-1")).toEqual({});
    expect(checkValues("Lab", { ...emptyValues(), summary: "Crown", toothKey: "" }, "lab-1")).toEqual({});
  });
});

describe("Q-5ai the tooth is pre-filled from the chosen diagnosis", () => {
  const dx = (id: string, toothKey: string | null) => ({ id, label: `Dx ${id}`, toothKey, status: "Active" }) as Diagnosis;
  const crown: PlanningProcedure = { procedureId: "p1", versionId: "v1", versionNumber: 1, codeSystem: "Local", code: "C1", description: "Crown", category: "Restorative", scope: "Tooth", dentition: "Both", fee: 100, currency: "USD" };
  function Harness({ start }: { start: ItemDraft }) {
    const [v, setV] = useState(start);
    return <><TreatmentPlanItemFields idBase="t" diagnoses={[dx("d1", "16"), dx("d2", null)]} procedures={[crown]} value={v} onChange={setV} errors={{}} /><output data-testid="tooth">{v.toothKey}</output></>;
  }

  it("fills an empty tooth, never overwrites a chosen one", async () => {
    const user = userEvent.setup();
    const { unmount } = render(<Harness start={{ ...EMPTY_ITEM, procedure: crown }} />);
    await user.selectOptions(screen.getByLabelText("Diagnosis this procedure is for"), "d1");
    expect(screen.getByTestId("tooth")).toHaveTextContent("16");
    unmount();
    render(<Harness start={{ ...EMPTY_ITEM, procedure: crown, toothKey: "26" }} />);
    await user.selectOptions(screen.getByLabelText("Diagnosis this procedure is for"), "d1");
    expect(screen.getByTestId("tooth")).toHaveTextContent("26");
  });

  it("fills it when the procedure is chosen after the diagnosis, and not for a diagnosis without a tooth", async () => {
    const user = userEvent.setup();
    render(<Harness start={EMPTY_ITEM} />);
    await user.selectOptions(screen.getByLabelText("Diagnosis this procedure is for"), "d1");
    expect(screen.getByTestId("tooth")).toHaveTextContent("");                          // no tooth field yet
    await user.selectOptions(screen.getByLabelText("Procedure"), "p1");
    expect(screen.getByTestId("tooth")).toHaveTextContent("16");
    await user.selectOptions(screen.getByLabelText("Diagnosis this procedure is for"), "d2");
    expect(screen.getByTestId("tooth")).toHaveTextContent("16");                        // kept
  });
});

describe("Q-5j the note templates are practice-level", () => {
  it("builds the way back only from a patient id", () => {
    expect(templatesPath(A)).toBe(`/clinical/templates?from=${A}`);
    expect(templatesPath("../x")).toBe("/clinical/templates");
    expect(backToPatient(`?from=${A}`)).toBe(`/patients/${A}/clinical`);
    expect(backToPatient("?from=https://evil")).toBeNull();
    expect(backToPatient("")).toBeNull();
  });

  it("the old patient URL redirects to the practice page, which offers the way back", async () => {
    open(`/patients/${A}/clinical/templates`);
    expect(await screen.findByRole("heading", { name: "Note templates" })).toBeInTheDocument();
    expect(window.location.pathname).toBe("/clinical/templates");
    expect(screen.getByRole("link", { name: "Back to clinical documentation" })).toHaveAttribute("href", `/patients/${A}/clinical`);
  });

  it("opened directly, the practice page has no patient and no way back to one", async () => {
    open("/clinical/templates");
    expect(await screen.findByRole("heading", { name: "Note templates" })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Back to clinical documentation" })).not.toBeInTheDocument();
  });
});
