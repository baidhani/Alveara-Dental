import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { PatientRegistrationPage } from "./PatientRegistrationPage";

/**
 * ALV-003-C01: the duplicate comparison on the registration form. STORY-003's own page tests (PatientRegistrationPage.test.tsx)
 * are untouched and must keep passing; these cover what this story adds.
 */
const json = (status: number, body: unknown) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

interface Call { url: string; method: string; headers: Record<string, string>; body: Record<string, unknown> | null }

function mockApi(answer: (call: Call, n: number) => Response) {
  const calls: Call[] = [];
  vi.stubGlobal(
    "fetch",
    vi.fn(async (url: string, init?: RequestInit) => {
      if (String(url).includes("csrf-token")) return json(200, { token: "csrf-1" });
      const call: Call = { url: String(url), method: init?.method ?? "GET", headers: (init?.headers ?? {}) as Record<string, string>, body: init?.body ? JSON.parse(String(init.body)) : null };
      calls.push(call);
      return answer(call, calls.length);
    })
  );
  return calls;
}

const candidate = (over: Record<string, unknown> = {}) => ({
  id: "11111111-1111-1111-1111-111111111111", firstName: "Ann", middleName: null, lastName: "Lee", dateOfBirth: "1985-03-09", phone: "555-010-0100",
  email: "ann@example.test", city: "Austin", state: "TX", isActive: true, reasons: ["Same last name and date of birth"], exact: false, ...over,
});

const created = { id: "22222222-2222-2222-2222-222222222222", firstName: "Anna", lastName: "Lee", dateOfBirth: "1985-03-09", createdAtUtc: "2026-10-01T12:00:00Z" };

async function fillTwin(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText("First name *"), "Anna");
  await user.type(screen.getByLabelText("Last name *"), "Lee");
  await user.type(screen.getByLabelText("Date of birth *"), "1985-03-09");
  await user.type(screen.getByLabelText("Phone *"), "555-777-0000");
  await user.type(screen.getByLabelText("Address *"), "1 Main St");
  await user.type(screen.getByLabelText("City *"), "Austin");
  await user.type(screen.getByLabelText("State *"), "TX");
  await user.type(screen.getByLabelText("Postal code *"), "78701");
}

afterEach(() => vi.unstubAllGlobals());

describe("duplicate comparison on the registration form", () => {
  it("a likely duplicate opens a side-by-side comparison with the reason, and nothing is registered yet", async () => {
    const calls = mockApi(() => json(409, { error: "possible_duplicate", message: "These patients may be the same person.", candidates: [candidate()] }));
    const user = userEvent.setup();
    render(<PatientRegistrationPage />);
    await fillTwin(user);

    await user.click(screen.getByRole("button", { name: "Register patient" }));

    const panel = await screen.findByRole("region", { name: "These patients may be the same person" });
    const table = within(panel).getByRole("table", { name: /compared with possible matches/ });
    expect(within(table).getByRole("columnheader", { name: "Existing: Ann Lee" })).toBeInTheDocument();
    const rows = Object.fromEntries(within(table).getAllByRole("row").slice(1).map((r) => [within(r).getByRole("rowheader").textContent, within(r).getAllByRole("cell").map((c) => c.textContent)]));
    expect(rows["Name"]).toEqual(["Anna Lee", "Ann Lee"]);
    expect(rows["Phone"]).toEqual(["555-777-0000", "555-010-0100"]);
    expect(rows["Why flagged"][1]).toContain("Same last name and date of birth");
    expect(within(table).getByRole("link", { name: "Open Ann Lee" })).toHaveAttribute("href", "/patients/11111111-1111-1111-1111-111111111111");
    expect(screen.queryByRole("status", { name: "Registration complete" })).not.toBeInTheDocument();
    expect(calls).toHaveLength(1);
    expect(calls[0].body).not.toHaveProperty("acknowledgedDuplicateIds"); // the first attempt acknowledged nothing
  });

  it("Register anyway resubmits with the flagged patients acknowledged and the SAME idempotency key", async () => {
    const calls = mockApi((_c, n) => (n === 1 ? json(409, { error: "possible_duplicate", message: "m", candidates: [candidate(), candidate({ id: "33333333-3333-3333-3333-333333333333", firstName: "Anne" })] }) : json(201, created)));
    const user = userEvent.setup();
    render(<PatientRegistrationPage />);
    await fillTwin(user);
    await user.click(screen.getByRole("button", { name: "Register patient" }));

    await user.click(await screen.findByRole("button", { name: "Register anyway — this is a different person" }));

    expect(await screen.findByRole("status", { name: "Registration complete" })).toHaveTextContent("Anna Lee");
    expect(calls).toHaveLength(2);
    expect(calls[1].body).toMatchObject({ firstName: "Anna", acknowledgedDuplicateIds: ["11111111-1111-1111-1111-111111111111", "33333333-3333-3333-3333-333333333333"] });
    expect(calls[1].headers["Idempotency-Key"]).toBe(calls[0].headers["Idempotency-Key"]);
  });

  it("an exact match is blocked: the existing record is offered and there is no way to register anyway", async () => {
    mockApi(() => json(409, { error: "duplicate_patient", message: "A patient with the same name and date of birth is already registered.", existingPatientId: "11111111-1111-1111-1111-111111111111", candidates: [candidate({ exact: true, reasons: ["Same name and date of birth"] })] }));
    const user = userEvent.setup();
    render(<PatientRegistrationPage />);
    await fillTwin(user);

    await user.click(screen.getByRole("button", { name: "Register patient" }));

    const panel = await screen.findByRole("region", { name: "This patient is already registered" });
    expect(within(panel).queryByRole("button", { name: /Register anyway/ })).not.toBeInTheDocument();
    expect(within(panel).getByRole("link", { name: "Open Ann Lee" })).toBeInTheDocument();
    expect(screen.getByRole("alert")).toHaveTextContent("Existing patient ID: 11111111-1111-1111-1111-111111111111"); // STORY-003's banner is kept
  });

  it("Back to the form closes the comparison and keeps what was typed; editing a field also closes it so a stale comparison is never acted on", async () => {
    mockApi(() => json(409, { error: "possible_duplicate", message: "m", candidates: [candidate()] }));
    const user = userEvent.setup();
    render(<PatientRegistrationPage />);
    await fillTwin(user);
    await user.click(screen.getByRole("button", { name: "Register patient" }));
    await screen.findByRole("region", { name: "These patients may be the same person" });

    await user.click(screen.getByRole("button", { name: "Back to the form" }));
    expect(screen.queryByRole("region", { name: "These patients may be the same person" })).not.toBeInTheDocument();
    expect(screen.getByLabelText("First name *")).toHaveValue("Anna");

    await user.click(screen.getByRole("button", { name: "Register patient" }));
    await screen.findByRole("region", { name: "These patients may be the same person" });
    await user.type(screen.getByLabelText("Middle name"), "M");
    expect(screen.queryByRole("region", { name: "These patients may be the same person" })).not.toBeInTheDocument();
  });

  it("after registering, offers the workspace and the household and guarantor step", async () => {
    mockApi(() => json(201, created));
    const user = userEvent.setup();
    render(<PatientRegistrationPage />);
    await fillTwin(user);
    await user.click(screen.getByRole("button", { name: "Register patient" }));

    await screen.findByRole("status", { name: "Registration complete" });
    expect(screen.getByRole("link", { name: "Open patient workspace" })).toHaveAttribute("href", "/patients/22222222-2222-2222-2222-222222222222");
    expect(screen.getByRole("link", { name: "Add household or guarantor" })).toHaveAttribute("href", "/patients/22222222-2222-2222-2222-222222222222/household");
    await waitFor(() => expect(screen.getByRole("button", { name: "Register another patient" })).toHaveFocus());
  });

  it("the comparison panel has no detectable accessibility violations", async () => {
    mockApi(() => json(409, { error: "possible_duplicate", message: "m", candidates: [candidate(), candidate({ id: "33333333-3333-3333-3333-333333333333", firstName: "Anne", isActive: false })] }));
    const user = userEvent.setup();
    const { container } = render(<PatientRegistrationPage />);
    await fillTwin(user);
    await user.click(screen.getByRole("button", { name: "Register patient" }));
    await screen.findByRole("region", { name: "These patients may be the same person" });

    expect(await axe(container)).toHaveNoViolations();
  });
});
