import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { PatientRegistrationPage } from "./PatientRegistrationPage";

const json = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

interface Call { url: string; method: string; headers: Record<string, string>; body: Record<string, string> | null }

/** Routes the CSRF token fetch and the POST; `answer` decides each registration response. */
function mockApi(answer: (call: Call, n: number) => Response | Promise<Response>) {
  const calls: Call[] = [];
  vi.stubGlobal(
    "fetch",
    vi.fn(async (url: string, init?: RequestInit) => {
      if (String(url).includes("csrf-token")) return json(200, { token: "csrf-1" });
      const call: Call = {
        url: String(url),
        method: init?.method ?? "GET",
        headers: (init?.headers ?? {}) as Record<string, string>,
        body: init?.body ? JSON.parse(String(init.body)) : null,
      };
      calls.push(call);
      return answer(call, calls.length);
    })
  );
  return calls;
}

const created = (over: Record<string, unknown> = {}) => ({
  id: "11111111-1111-1111-1111-111111111111", firstName: "Ann", lastName: "Lee", dateOfBirth: "1985-03-09", createdAtUtc: "2026-10-01T12:00:00Z", ...over,
});

async function fillValid(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText("First name *"), "Ann");
  await user.type(screen.getByLabelText("Last name *"), "Lee");
  await user.type(screen.getByLabelText("Date of birth *"), "1985-03-09");
  await user.type(screen.getByLabelText("Phone *"), "555-010-0100");
  await user.type(screen.getByLabelText("Address *"), "1 Main St");
  await user.type(screen.getByLabelText("City *"), "Austin");
  await user.type(screen.getByLabelText("State *"), "TX");
  await user.type(screen.getByLabelText("Postal code *"), "78701");
}

afterEach(() => vi.unstubAllGlobals());

describe("PatientRegistrationPage", () => {
  it("captures demographics and contact details and sends them with an idempotency key and CSRF token", async () => {
    const calls = mockApi(() => json(201, created()));
    const user = userEvent.setup();
    render(<PatientRegistrationPage />);

    await fillValid(user);
    await user.type(screen.getByLabelText("Email"), "ann@example.test");
    await user.click(screen.getByRole("button", { name: "Register patient" }));

    expect(await screen.findByRole("status", { name: "Registration complete" })).toHaveTextContent("Ann Lee, born 1985-03-09");
    expect(screen.getByText(/Patient ID: 1111/)).toBeInTheDocument();
    expect(calls).toHaveLength(1);
    expect(calls[0]).toMatchObject({ url: "/api/patients", method: "POST" });
    expect(calls[0].headers["Idempotency-Key"]).toMatch(/\S+/);
    expect(calls[0].headers["X-CSRF-Token"]).toBe("csrf-1");
    expect(calls[0].body).toMatchObject({
      firstName: "Ann", lastName: "Lee", dateOfBirth: "1985-03-09", phone: "555-010-0100", email: "ann@example.test",
      addressLine1: "1 Main St", city: "Austin", state: "TX", postalCode: "78701",
    });
  });

  it("prompts for every missing required field, sends nothing, and focuses the first one", async () => {
    const calls = mockApi(() => json(201, created()));
    const user = userEvent.setup();
    render(<PatientRegistrationPage />);

    await user.type(screen.getByLabelText("Last name *"), "Lee");
    await user.click(screen.getByRole("button", { name: "Register patient" }));

    for (const label of ["First name", "Date of birth", "Phone", "Address", "City", "State", "Postal code"]) {
      expect(screen.getByText(`${label} is required.`)).toBeInTheDocument();
    }
    expect(screen.queryByText("Last name is required.")).not.toBeInTheDocument(); // what was filled in is not nagged about
    expect(screen.getByLabelText("First name *")).toHaveAttribute("aria-invalid", "true");
    await waitFor(() => expect(screen.getByLabelText("First name *")).toHaveFocus());
    expect(calls).toHaveLength(0);
  });

  it("clears a field's prompt as soon as the user starts fixing it", async () => {
    mockApi(() => json(201, created()));
    const user = userEvent.setup();
    render(<PatientRegistrationPage />);

    await user.click(screen.getByRole("button", { name: "Register patient" }));
    expect(screen.getByText("City is required.")).toBeInTheDocument();
    await user.type(screen.getByLabelText("City *"), "A");
    expect(screen.queryByText("City is required.")).not.toBeInTheDocument();
  });

  it("shows the server's per-field messages when it rejects a value the browser accepted", async () => {
    mockApi(() => json(400, { error: "validation_failed", message: "Some fields need attention.", fieldErrors: { email: "Email must look like name@example.com." } }));
    const user = userEvent.setup();
    render(<PatientRegistrationPage />);

    await fillValid(user);
    await user.type(screen.getByLabelText("Email"), "nope");
    await user.click(screen.getByRole("button", { name: "Register patient" }));

    expect(await screen.findByText("Email must look like name@example.com.")).toBeInTheDocument();
    expect(screen.getByLabelText("Email")).toHaveAttribute("aria-invalid", "true");
    await waitFor(() => expect(screen.getByLabelText("Email")).toHaveFocus());
  });

  it("tells the user a duplicate already exists and names the existing record, keeping what was typed", async () => {
    mockApi(() => json(409, { error: "duplicate_patient", message: "A patient with the same name and date of birth is already registered.", existingPatientId: "22222222-2222-2222-2222-222222222222" }));
    const user = userEvent.setup();
    render(<PatientRegistrationPage />);

    await fillValid(user);
    await user.click(screen.getByRole("button", { name: "Register patient" }));

    const banner = await screen.findByRole("alert");
    expect(banner).toHaveTextContent("already registered");
    expect(banner).toHaveTextContent("Existing patient ID: 22222222-2222-2222-2222-222222222222");
    expect(screen.getByLabelText("First name *")).toHaveValue("Ann");
  });

  it("reuses the same idempotency key when the user retries after a failure, then uses a fresh one for the next patient", async () => {
    const calls = mockApi((_c, n) => (n === 1 ? json(500, { error: "server_error", message: "Boom." }) : json(201, created())));
    const user = userEvent.setup();
    render(<PatientRegistrationPage />);

    await fillValid(user);
    await user.click(screen.getByRole("button", { name: "Register patient" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Boom.");
    await user.click(screen.getByRole("button", { name: "Register patient" }));
    await screen.findByRole("status", { name: "Registration complete" });

    expect(calls).toHaveLength(2);
    expect(calls[1].headers["Idempotency-Key"]).toBe(calls[0].headers["Idempotency-Key"]); // a retry is the same registration

    await user.click(screen.getByRole("button", { name: "Register another patient" }));
    expect(screen.getByLabelText("First name *")).toHaveValue("");
    await fillValid(user);
    await user.click(screen.getByRole("button", { name: "Register patient" }));
    await screen.findByRole("status", { name: "Registration complete" });
    expect(calls[2].headers["Idempotency-Key"]).not.toBe(calls[0].headers["Idempotency-Key"]);
  });

  it("reports a dropped connection without losing the typed values", async () => {
    mockApi(() => { throw new TypeError("network error"); });
    const user = userEvent.setup();
    render(<PatientRegistrationPage />);

    await fillValid(user);
    await user.click(screen.getByRole("button", { name: "Register patient" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Check your connection");
    expect(screen.getByLabelText("Last name *")).toHaveValue("Lee");
  });

  it("ignores a second click while a registration is in flight (no double submit)", async () => {
    let release: (r: Response) => void = () => {};
    const calls = mockApi(() => new Promise<Response>((resolve) => { release = resolve; }));
    const user = userEvent.setup();
    render(<PatientRegistrationPage />);

    await fillValid(user);
    await user.click(screen.getByRole("button", { name: "Register patient" }));
    const busy = await screen.findByRole("button", { name: "Registering…" });
    expect(busy).toBeDisabled();
    await user.click(busy);
    release(json(201, created()));

    await screen.findByRole("status", { name: "Registration complete" });
    expect(calls).toHaveLength(1);
  });

  it("does not include household or guarantor fields (owned by ALV-003-C01)", () => {
    mockApi(() => json(201, created()));
    render(<PatientRegistrationPage />);
    expect(screen.queryByLabelText(/household|guarantor|family/i)).not.toBeInTheDocument();
  });
});
