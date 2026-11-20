import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { PatientRegistrationPage } from "./PatientRegistrationPage";
import { DemographicReviewPage } from "./DemographicReviewPage";
import { PatientFieldsForm, NOT_IN_LIST_HINT } from "../components/PatientFieldsForm";
import { EMPTY_PATIENT } from "../components/patientFields";
import { resetDemographicOptionsForTest } from "../components/useDemographicOptions";

/**
 * ALV-N025 slice 4 (F-032, F-033; owner decisions DEM-2..DEM-4): Sex and Country are lists; State is the US list for the US and free text "State / Province / Region"
 * elsewhere; the country starts at the practice's default; a stored value not on its list stays shown, marked, with a prompt; the review page lists off-list values.
 */
const json = (status: number, body: unknown) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

const OPTIONS = {
  sexValues: ["Female", "Male", "Other", "Unknown/unspecified"],
  usStates: [{ code: "IL", name: "Illinois" }, { code: "TX", name: "Texas" }, { code: "AE", name: "Armed Forces Europe" }],
  countries: [{ code: "US", name: "United States" }, { code: "CA", name: "Canada" }, { code: "GB", name: "United Kingdom" }],
  defaultCountry: "CA",
};

function mockApi(extra: (url: string, init?: RequestInit) => Response | undefined = () => undefined) {
  const posts: Record<string, unknown>[] = [];
  vi.stubGlobal("fetch", vi.fn(async (url: string, init?: RequestInit) => {
    const u = String(url);
    if (u.includes("csrf-token")) return json(200, { token: "csrf-1" });
    if (u.includes("demographic-options")) return json(200, OPTIONS);
    const answered = extra(u, init);
    if (answered) return answered;
    if (init?.method === "POST") {
      posts.push(JSON.parse(String(init.body)));
      return json(201, { id: "11111111-1111-1111-1111-111111111111", firstName: "Ann", lastName: "Lee", dateOfBirth: "1985-03-09", createdAtUtc: "2026-10-01T12:00:00Z", mrn: 63898432711 });
    }
    return json(404, { error: "not_found" });
  }));
  return posts;
}

beforeEach(() => resetDemographicOptionsForTest());
afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks(); });

describe("ALV-N025 demographics", () => {
  it("offers Sex and Country lists, starts at the practice's country, and switches State between free text and the US list", async () => {
    const posts = mockApi();
    const user = userEvent.setup();
    render(<PatientRegistrationPage />);

    const country = await screen.findByLabelText("Country *");
    await screen.findByRole("option", { name: "Canada" });
    expect(country).toHaveValue("CA");                                                                  // the practice's default
    expect(screen.getByLabelText("State / Province / Region *").tagName).toBe("INPUT");                // free text outside the US
    expect(within(screen.getByLabelText("Sex")).getAllByRole("option").map((o) => o.textContent)).toEqual(["Not specified", "Female", "Male", "Other", "Unknown/unspecified"]);

    await user.selectOptions(country, "US");
    const state = screen.getByLabelText("State *");
    expect(state.tagName).toBe("SELECT");
    expect(within(state).getByRole("option", { name: "Armed Forces Europe (AE)" })).toBeInTheDocument();

    await user.type(screen.getByLabelText("First name *"), "Ann");
    await user.type(screen.getByLabelText("Last name *"), "Lee");
    await user.type(screen.getByLabelText("Date of birth *"), "1985-03-09");
    await user.selectOptions(screen.getByLabelText("Sex"), "Female");
    await user.type(screen.getByLabelText("Phone *"), "555-010-0100");
    await user.type(screen.getByLabelText("Address *"), "1 Main St");
    await user.type(screen.getByLabelText("City *"), "Chicago");
    await user.selectOptions(state, "IL");
    await user.type(screen.getByLabelText("Postal code *"), "60601");
    await user.click(screen.getByRole("button", { name: "Register patient" }));

    expect(await screen.findByRole("status", { name: "Registration complete" })).toBeInTheDocument();
    expect(posts[0]).toMatchObject({ sex: "Female", state: "IL", country: "US" });
  });

  it("keeps a stored value that is not on its list, marked, with a prompt to choose", async () => {
    mockApi();
    render(<PatientFieldsForm values={{ ...EMPTY_PATIENT, sex: "F", state: "Ill.", country: "US" }} errors={{}} onChange={() => () => {}} />);
    await screen.findByRole("option", { name: "Illinois (IL)" });
    expect(screen.getByLabelText("Sex")).toHaveValue("F");
    expect(screen.getByRole("option", { name: "F (not in the list)" })).toBeInTheDocument();
    expect(screen.getByLabelText("State *")).toHaveValue("Ill.");
    expect(screen.getAllByText(NOT_IN_LIST_HINT)).toHaveLength(2);
  });

  it("the review page lists each off-list value with a link to the patient", async () => {
    mockApi((u) => u.includes("demographic-review")
      ? json(200, { items: [{ patientId: "22222222-2222-2222-2222-222222222222", displayName: "Cal Norm", mrn: 63898430001, field: "sex", value: "F" }], truncated: false })
      : undefined);
    render(<MemoryRouter><DemographicReviewPage /></MemoryRouter>);
    const row = (await screen.findByRole("link", { name: "Cal Norm" })).closest("tr")!;
    expect(within(row).getByText("Sex")).toBeInTheDocument();
    expect(within(row).getByText("F")).toBeInTheDocument();
    expect(within(row).getByText("63898430001")).toBeInTheDocument();
  });

  it("the review page says when there is nothing to review", async () => {
    mockApi((u) => (u.includes("demographic-review") ? json(200, { items: [], truncated: false }) : undefined));
    render(<MemoryRouter><DemographicReviewPage /></MemoryRouter>);
    expect(await screen.findByText("Nothing to review")).toBeInTheDocument();
  });
});
