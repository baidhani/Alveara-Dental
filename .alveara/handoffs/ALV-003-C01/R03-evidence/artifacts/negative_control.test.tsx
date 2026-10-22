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

describe("negative control: the previous assertion under a forced race", () => {
  afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks(); });
  it("PREVIOUS test body, with the focus frame landing between the click on City and the keystroke", async () => {
    const pending: FrameRequestCallback[] = [];
    vi.spyOn(window, "requestAnimationFrame").mockImplementation((cb) => { pending.push(cb); return pending.length; });
    mockApi(() => json(201, created()));
    const user = userEvent.setup();
    render(<PatientRegistrationPage />);
    await user.click(screen.getByRole("button", { name: "Register patient" }));
    expect(screen.getByText("City is required.")).toBeInTheDocument();
    // the previous body was: await user.type(City, "A"); expect(prompt gone). user.type is a click then a keyboard; the frame runs between them:
    await user.click(screen.getByLabelText("City *"));
    pending.splice(0).forEach((cb) => cb(0));
    await user.keyboard("A");
    expect(screen.queryByText("City is required.")).not.toBeInTheDocument();
  });
});
