import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { App } from "./App";
import { OdontogramPanel } from "./pages/odontogram/OdontogramPanel";
import { FakeClinicalServer } from "./test/fakeClinicalServer";
import { json, makePatient } from "./test/fakePatientServer";
import type { PatientDetail } from "./services/patientsApi";

/**
 * STORY-006 (read-only chart): the Odontogram tab through the real <App /> against an in-memory fake of the odontogram API. The rules are proven by the backend tests and the
 * real-backend walkthrough; what is proven here is the UI's behaviour: the 32 permanent teeth drawn with what is recorded on each as WORDS, nothing invented for an empty chart,
 * a tooth and a surface selectable, the numbering system a parameter that defaults to Universal, findings on teeth the chart does not draw never hidden, the patient-safety strip in
 * view, a role that may not read the chart never shown or asked for it, and a failed load saying so instead of showing an empty chart.
 */
const A = "aaaaaaaa-0000-0000-0000-000000000001";
let server: FakeClinicalServer;

function open(path: string) {
  window.history.pushState({}, "", path);
  return render(<App />);
}
async function openChart() {
  const view = open(`/patients/${A}/odontogram`);
  await screen.findByRole("heading", { name: "Odontogram" });
  return view;
}
const tooth = (label: string) => screen.getByRole("button", { name: new RegExp(`^Tooth ${label},`) });
const patient = () => makePatient({ id: A, firstName: "Ann", lastName: "Lee" }) as unknown as PatientDetail;

beforeEach(() => {
  server = new FakeClinicalServer();
  server.add(makePatient({ id: A, firstName: "Ann", lastName: "Lee" }));
  server.install();
});
afterEach(() => vi.unstubAllGlobals());

describe("the odontogram tab", () => {
  it("is in the patient workspace navigation for a role that may read clinical documentation, and leads to the chart", async () => {
    open(`/patients/${A}`);
    const link = await screen.findByRole("link", { name: "Odontogram" });
    expect(link).toHaveAttribute("href", `/patients/${A}/odontogram`);
  });

  it("is not shown, and nothing is requested, for a role that may not read clinical documentation", async () => {
    server.permissions = ["ViewPatientRecords", "RegisterPatients", "EditPatients"];
    open(`/patients/${A}`);
    await screen.findByText("Ann Lee", { selector: ".alv-patient-header__name" });
    expect(screen.queryByRole("link", { name: "Odontogram" })).not.toBeInTheDocument();
    expect(server.callsToClinical("GET", "/odontogram")).toHaveLength(0);
  });

  it("keeps the patient-safety strip in view above the chart", async () => {
    await openChart();
    expect(screen.getByRole("region", { name: "Patient safety" })).toBeInTheDocument();
  });
});

describe("the chart", () => {
  it("draws the 32 permanent teeth, Universal-numbered by default, and says an empty chart means nothing recorded - never healthy - and invents nothing", async () => {
    await openChart();
    const upper = within(screen.getByRole("group", { name: "Upper teeth" })).getAllByRole("button");
    const lower = within(screen.getByRole("group", { name: "Lower teeth" })).getAllByRole("button");
    expect(upper).toHaveLength(16);
    expect(lower).toHaveLength(16);
    expect(upper.map((b) => b.querySelector(".alv-odonto__tooth-number")!.textContent).join(" ")).toBe("1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16");
    expect(lower.map((b) => b.querySelector(".alv-odonto__tooth-number")!.textContent).join(" ")).toBe("32 31 30 29 28 27 26 25 24 23 22 21 20 19 18 17");
    expect(screen.getByText("No findings are recorded on any tooth.")).toBeInTheDocument();
    expect(screen.getByText(/An empty tooth means nothing is recorded, not that it is healthy/)).toBeInTheDocument();
    expect(tooth("3")).toHaveAccessibleName("Tooth 3, upper right first molar: nothing recorded");
    expect(server.odontogram.findings.size).toBe(0);
    expect(screen.queryByText(/Diagnosed|Planned|Completed/, { selector: ".alv-odonto__tooth-state" })).not.toBeInTheDocument();
  });

  it("writes each tooth's state as a word under its number, the most pressing first, with the count of the rest", async () => {
    server.odontogram.addFinding(A, { toothKey: "16", surface: "O", condition: "Caries", state: "Diagnosed" });
    server.odontogram.addFinding(A, { toothKey: "16", surface: "D", condition: "Restoration", state: "Completed" });
    server.odontogram.addFinding(A, { toothKey: "11", condition: "Crown", state: "Existing" });
    server.odontogram.addFinding(A, { toothKey: "26", surface: "M", condition: "Caries", state: "Planned" });
    await openChart();
    expect(tooth("3")).toHaveAccessibleName("Tooth 3, upper right first molar: 2 findings, Diagnosed");
    expect(tooth("3").querySelector(".alv-odonto__tooth-state")!.textContent).toBe("Diagnosed +1");     // diagnosed outranks completed
    expect(tooth("8").querySelector(".alv-odonto__tooth-state")!.textContent).toBe("Existing");
    expect(tooth("14").querySelector(".alv-odonto__tooth-state")!.textContent).toBe("Planned");
    expect(tooth("3").className).toContain("alv-odonto__state--diagnosed");
    expect(tooth("8").className).toContain("alv-odonto__state--existing");
    expect(tooth("14").className).toContain("alv-odonto__state--planned");
    expect(tooth("1").className).toContain("alv-odonto__state--none");
  });

  it("explains each state in a legend, in words", async () => {
    await openChart();
    const legend = screen.getByRole("list", { name: "Legend" });
    for (const [state, meaning] of [["Existing", "already in the mouth"], ["Diagnosed", "found, not yet planned"], ["Planned", "treatment planned"], ["Completed", "treatment completed"]])
      expect(within(legend).getByText(state).closest("li")).toHaveTextContent(meaning);
  });

  it("withdrawn findings are not on the chart", async () => {
    server.odontogram.addFinding(A, { toothKey: "16", surface: "O", condition: "Caries", status: "Withdrawn" });
    await openChart();
    expect(screen.getByText("No findings are recorded on any tooth.")).toBeInTheDocument();
  });

  it("lists findings on teeth the chart does not draw, so none is hidden", async () => {
    server.odontogram.addFinding(A, { toothKey: "54", surface: "O", condition: "Caries", state: "Diagnosed" });
    await openChart();
    const region = screen.getByRole("heading", { name: "Recorded on teeth not drawn on this chart" }).closest("section")!;
    expect(region).toHaveTextContent("1 finding is recorded on primary teeth");
    expect(region).toHaveTextContent("Tooth B (upper right primary first molar): Caries, Occlusal surface: Diagnosed");
    expect(screen.queryByText("No findings are recorded on any tooth.")).not.toBeInTheDocument();
  });
});

describe("selecting a tooth and a surface", () => {
  it("selects a tooth, names it in every system, lists what is recorded by surface with who recorded it, and selects it off again", async () => {
    const user = userEvent.setup();
    server.odontogram.addFinding(A, { toothKey: "16", surface: "O", condition: "Caries", state: "Diagnosed", recordedByName: "Dr. Okafor" });
    server.odontogram.addFinding(A, { toothKey: "16", surface: "M", condition: "Restoration", state: "Existing" });
    server.odontogram.addFinding(A, { toothKey: "16", condition: "RootCanal", state: "Completed", updatedByName: "Hana Hygienist", updatedAtUtc: "2026-09-02T09:00:00Z" });
    await openChart();
    expect(screen.getByText("Select a tooth to see what is recorded on it.")).toBeInTheDocument();

    await user.click(tooth("3"));
    expect(tooth("3")).toHaveAttribute("aria-pressed", "true");
    const detail = screen.getByRole("region", { name: "Tooth 3: upper right first molar" });
    expect(within(detail).getByText("Also written FDI 16 · Palmer UR6.")).toBeInTheDocument();
    const rows = within(detail).getAllByRole("listitem").map((li) => li.textContent!.replace(/\s+/g, " "));
    expect(rows.some((r) => r.startsWith("Caries · Occlusal surface") && r.includes("Diagnosed") && r.includes("Recorded by Dr. Okafor"))).toBe(true);
    expect(rows.some((r) => r.startsWith("Root canal · Whole tooth") && r.includes("Completed") && r.includes("last changed by Hana Hygienist"))).toBe(true);
    expect(within(detail).getByRole("button", { name: /Occlusal/ })).toHaveTextContent("1 finding");
    expect(within(detail).getByRole("button", { name: /Distal/ })).toHaveTextContent("none recorded");

    await user.click(tooth("3"));
    expect(tooth("3")).toHaveAttribute("aria-pressed", "false");
    expect(screen.queryByRole("region", { name: /^Tooth 3:/ })).not.toBeInTheDocument();
  });

  it("offers only the surfaces that exist on the tooth: Occlusal on a molar, Incisal on an incisor", async () => {
    const user = userEvent.setup();
    await openChart();
    await user.click(tooth("3"));
    const surfaces = () => within(screen.getByRole("group", { name: "Surfaces" })).getAllByRole("button").map((b) => b.firstElementChild!.textContent);
    expect(surfaces()).toEqual(["Mesial", "Occlusal", "Distal", "Buccal", "Lingual"]);
    await user.click(tooth("8"));
    expect(surfaces()).toEqual(["Mesial", "Incisal", "Distal", "Facial", "Lingual"]);
  });

  it("selecting a surface narrows the list to that surface and the whole tooth, and selecting it again shows everything", async () => {
    const user = userEvent.setup();
    server.odontogram.addFinding(A, { toothKey: "16", surface: "O", condition: "Caries" });
    server.odontogram.addFinding(A, { toothKey: "16", surface: "M", condition: "Restoration" });
    server.odontogram.addFinding(A, { toothKey: "16", condition: "Crown" });
    await openChart();
    await user.click(tooth("3"));
    const list = () => within(screen.getByRole("region", { name: /^Tooth 3:/ })).getAllByRole("listitem").filter((li) => li.className.includes("alv-odonto__finding"));
    expect(list()).toHaveLength(3);

    await user.click(screen.getByRole("button", { name: /Occlusal/ }));
    expect(screen.getByRole("button", { name: /Occlusal/ })).toHaveAttribute("aria-pressed", "true");
    const texts = list().map((li) => li.textContent!);
    expect(texts).toHaveLength(2);
    expect(texts.some((t) => t.includes("Caries"))).toBe(true);
    expect(texts.some((t) => t.includes("Crown"))).toBe(true);                // the whole-tooth finding stays
    expect(texts.some((t) => t.includes("Restoration"))).toBe(false);         // the mesial one is filtered out

    await user.click(screen.getByRole("button", { name: /Distal/ }));
    expect(list().map((li) => li.textContent)).toEqual([expect.stringContaining("Crown")]);     // nothing on the distal surface; the whole-tooth finding still applies
    await user.click(screen.getByRole("button", { name: /Distal/ }));
    expect(list()).toHaveLength(3);
  });

  it("says nothing is recorded for a surface, and how to see everything again, when the tooth has only findings on other surfaces", async () => {
    const user = userEvent.setup();
    server.odontogram.addFinding(A, { toothKey: "16", surface: "O", condition: "Caries" });
    await openChart();
    await user.click(tooth("3"));
    await user.click(screen.getByRole("button", { name: /Distal/ }));
    expect(screen.getByText("Nothing is recorded for the distal surface. Select it again to see everything on this tooth.")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /Distal/ }));
    expect(screen.getByText("Caries", { selector: "strong" })).toBeInTheDocument();
  });

  it("says nothing is recorded for a tooth with no findings, without calling it healthy", async () => {
    const user = userEvent.setup();
    await openChart();
    await user.click(tooth("20"));
    expect(screen.getByText("Nothing is recorded for this tooth. This does not mean it is healthy.")).toBeInTheDocument();
  });

  it("a new tooth starts with no surface selected", async () => {
    const user = userEvent.setup();
    await openChart();
    await user.click(tooth("3"));
    await user.click(screen.getByRole("button", { name: /Occlusal/ }));
    await user.click(tooth("4"));
    expect(within(screen.getByRole("group", { name: "Surfaces" })).getAllByRole("button").every((b) => b.getAttribute("aria-pressed") === "false")).toBe(true);
  });

  it("works from the keyboard", async () => {
    const user = userEvent.setup();
    await openChart();
    tooth("3").focus();
    await user.keyboard("{Enter}");
    expect(tooth("3")).toHaveAttribute("aria-pressed", "true");
    await user.keyboard("{Tab}");
    expect(tooth("4")).toHaveFocus();
  });
});

describe("the numbering system is a parameter that defaults to Universal", () => {
  it.each([
    ["Fdi" as const, "18", "28", "FDI"],
    ["Palmer" as const, "UR8", "UL8", "Palmer"],
    ["Universal" as const, "1", "16", "Universal"],
  ])("%s numbers the same teeth in its own way", async (system, first, lastUpper, name) => {
    render(<OdontogramPanel patient={patient()} numbering={system} />);
    await screen.findByRole("heading", { name: "Odontogram" });
    const upper = within(screen.getByRole("group", { name: "Upper teeth" })).getAllByRole("button").map((b) => b.querySelector(".alv-odonto__tooth-number")!.textContent);
    expect(upper[0]).toBe(first);
    expect(upper[15]).toBe(lastUpper);
    expect(screen.getByText(new RegExp(`numbered with the ${name} system`))).toBeInTheDocument();
  });

  it("the stored tooth does not change with the system: the same finding sits on the same tooth under all three", async () => {
    server.odontogram.addFinding(A, { toothKey: "16", surface: "O", condition: "Caries", state: "Diagnosed" });
    for (const [system, label] of [["Universal", "3"], ["Fdi", "16"], ["Palmer", "UR6"]] as const) {
      const { unmount } = render(<OdontogramPanel patient={patient()} numbering={system} />);
      await screen.findByRole("heading", { name: "Odontogram" });
      expect(tooth(label).querySelector(".alv-odonto__tooth-state")!.textContent).toBe("Diagnosed");
      unmount();
    }
  });
});

describe("failure", () => {
  it("says it could not load - and not to assume nothing is recorded - instead of showing an empty chart", async () => {
    server.failClinical(`GET /api/patients/${A}/odontogram`, json(500, { error: "server_error", message: "boom" }));
    open(`/patients/${A}/odontogram`);
    expect(await screen.findByText("Could not load the odontogram")).toBeInTheDocument();
    expect(screen.getByText(/Do not assume nothing is recorded/)).toBeInTheDocument();
    expect(screen.queryByRole("group", { name: "Upper teeth" })).not.toBeInTheDocument();
    expect(screen.queryByText("No findings are recorded on any tooth.")).not.toBeInTheDocument();
  });
});

describe("accessibility", () => {
  it("has no axe violations with a tooth and a surface selected", async () => {
    const user = userEvent.setup();
    server.odontogram.addFinding(A, { toothKey: "16", surface: "O", condition: "Caries", state: "Diagnosed" });
    server.odontogram.addFinding(A, { toothKey: "54", surface: "O", condition: "Caries", state: "Planned" });
    const { container } = await openChart();
    await user.click(tooth("3"));
    await user.click(screen.getByRole("button", { name: /Occlusal/ }));
    expect(await axe(container)).toHaveNoViolations();
  });
});
