import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import type { PatientDetail } from "../../services/patientsApi";
import type { BillableProcedure, BillingAccount, BillingEntry } from "../../services/billingApi";
import { BillingPanel } from "./BillingPanel";

/**
 * STORY-007: the billing screen against a scripted API. The rules (the amount comes from the plan, a payment cannot exceed the balance, history is only added to) are proven by the backend tests and the
 * real-backend walkthrough; what is proven here is the UI's behaviour for each outcome: the server's balance and history shown as they are, a charge sent with no amount, a payment and an adjustment sent with
 * the account version, gaps named before anything is sent, a refusal beside its field with what was typed kept, a failed save that says nothing was recorded and retries under the same key, a stale account
 * shown as a conflict, honest empty and failed states, and a reader who sees everything but gets no controls.
 */
const P = "aaaaaaaa-0000-0000-0000-000000000001";
const patient = { id: P } as PatientDetail;
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

const entry = (over: Partial<BillingEntry> = {}): BillingEntry => ({
  entryNumber: 1, kind: "Charge", amount: 100, balanceAfter: 100, planItemId: "i1", procedureCode: "PERIO-1", procedureDescription: "Periodontal maintenance", paymentMethod: null, reason: null,
  recordedAtUtc: "2030-01-01T10:00:00Z", recordedByName: "Beth Billing", ...over,
});
const account = (over: Partial<BillingAccount> = {}): BillingAccount => ({ patientId: P, hasAccount: true, balanceOwed: 100, currency: "USD", rowVersion: "v1", entries: [entry()], ...over });
const ready = (over: Partial<BillableProcedure> = {}): BillableProcedure => ({ planItemId: "i2", procedureCode: "SCALE-1", description: "Scaling", toothKey: null, surface: null, fee: 70, currency: "USD", ...over });

interface Call { url: string; method: string; body: Record<string, unknown> | null }
interface Api { account: BillingAccount; ready: BillableProcedure[]; calls: Call[]; respond: Record<string, (body: Record<string, unknown> | null) => Response> }

function stubApi(over: Partial<Api> = {}): Api {
  const api: Api = { account: account(), ready: [ready()], calls: [], respond: {}, ...over };
  vi.stubGlobal("fetch", vi.fn().mockImplementation((url: RequestInfo | URL, init?: RequestInit) => {
    const u = String(url);
    const method = init?.method ?? "GET";
    const body = init?.body ? (JSON.parse(init.body as string) as Record<string, unknown>) : null;
    api.calls.push({ url: u, method, body });
    for (const [fragment, handler] of Object.entries(api.respond)) if (u.includes(fragment)) return Promise.resolve(handler(body));
    if (u.includes("/api/auth/csrf-token")) return Promise.resolve(json({ token: "csrf" }));
    if (u.includes("/billable-procedures")) return Promise.resolve(json(api.ready));
    if (method === "GET") return Promise.resolve(json(api.account));
    return Promise.resolve(json(api.account));
  }));
  return api;
}

const posts = (api: Api) => api.calls.filter((c) => c.method === "POST");
const openPanel = async (canWrite: boolean) => {
  render(<BillingPanel patient={patient} canWrite={canWrite} />);
  await screen.findByRole("heading", { name: "Account history" });
};
const paymentForm = () => within(screen.getByRole("form", { name: "Record a payment" }));
const adjustmentForm = () => within(screen.getByRole("form", { name: "Record an adjustment" }));
const fillPayment = async (user: ReturnType<typeof userEvent.setup>, amount: string, method = "Cash") => {
  await user.type(paymentForm().getByLabelText(/^Amount/), amount);
  await user.selectOptions(paymentForm().getByLabelText("How it was received"), method);
};

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("reading the account", () => {
  it("shows the server's balance and each entry with the balance after it, and a reader gets no controls", async () => {
    stubApi({ account: account({ balanceOwed: 40, entries: [entry(), entry({ entryNumber: 2, kind: "Payment", amount: -60, balanceAfter: 40, planItemId: null, procedureCode: null, procedureDescription: null, paymentMethod: "Cash" })] }) });
    await openPanel(false);
    expect(screen.getByText("Balance owed: $40.00")).toBeInTheDocument();
    const rows = within(screen.getByRole("table", { name: "Account history" })).getAllByRole("row");
    expect(rows).toHaveLength(3);
    expect(within(rows[1]).getAllByText("$100.00")).toHaveLength(2);                      // the charge, and the balance owed after it
    expect(within(rows[2]).getByText("-$60.00")).toBeInTheDocument();
    expect(within(rows[2]).getByText("$40.00")).toBeInTheDocument();
    expect(screen.getByRole("status")).toHaveTextContent("You can read this but your role cannot change it.");
    expect(screen.queryByRole("form")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Bill|Record/ })).not.toBeInTheDocument();
    expect(screen.getByText(/SCALE-1/)).toBeInTheDocument();     // the list of what is ready is visible, without its buttons
  });

  it("says nothing has been billed rather than showing an empty ledger as settled, and offers no payment before anything is owed", async () => {
    stubApi({ account: account({ hasAccount: false, balanceOwed: 0, rowVersion: null, entries: [] }) });
    await openPanel(true);
    expect(screen.getByText(/Nothing has been billed to this patient yet/)).toBeInTheDocument();
    expect(screen.getByText("Balance owed: $0.00")).toBeInTheDocument();
    expect(screen.queryByRole("form", { name: "Record a payment" })).not.toBeInTheDocument();
    expect(screen.queryByRole("form", { name: "Record an adjustment" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Bill SCALE-1" })).toBeInTheDocument();
  });

  it("a failed load says it could not load rather than showing a zero balance", async () => {
    stubApi({ respond: { "/billing": () => json({ error: "server_error", message: "boom" }, 500) } });
    render(<BillingPanel patient={patient} />);
    expect(await screen.findByText("Could not load the billing account")).toBeInTheDocument();
    expect(screen.queryByText(/Balance owed/)).not.toBeInTheDocument();
  });

  it("has no accessibility violations for someone who can bill", async () => {
    stubApi();
    await openPanel(true);
    expect(await axe(document.body)).toHaveNoViolations();
  });
});

describe("billing a completed procedure", () => {
  it("sends the procedure and a save key and no amount, then shows the new balance and the refreshed list", async () => {
    const user = userEvent.setup();
    const api = stubApi({ account: account({ hasAccount: false, balanceOwed: 0, rowVersion: null, entries: [] }) });
    api.respond["/charges"] = () => { api.account = account({ balanceOwed: 70, entries: [entry({ planItemId: "i2", procedureCode: "SCALE-1", procedureDescription: "Scaling", amount: 70, balanceAfter: 70 })] }); api.ready = []; return json(api.account); };
    await openPanel(true);
    await user.click(screen.getByRole("button", { name: "Bill SCALE-1" }));

    const [post] = posts(api);
    expect(post.url).toContain(`/api/patients/${P}/billing/charges`);
    expect(Object.keys(post.body ?? {}).sort()).toEqual(["idempotencyKey", "planItemId"]);      // the amount is the plan's: there is none to send
    expect(post.body?.planItemId).toBe("i2");
    expect(await screen.findByText("Balance owed: $70.00")).toBeInTheDocument();
    expect(screen.getByRole("status")).toHaveTextContent("SCALE-1 billed for $70.00. Balance owed is now $70.00.");
    expect(await screen.findByText("No completed procedure is waiting to be billed.")).toBeInTheDocument();
  });

  it("says which procedure was not billed and why, and keeps the same key for the retry", async () => {
    const user = userEvent.setup();
    const api = stubApi({ respond: { "/charges": () => json({ error: "not_completed", message: "Only a completed procedure can be billed. Mark it completed in the treatment plan first." }, 409) } });
    await openPanel(true);
    await user.click(screen.getByRole("button", { name: "Bill SCALE-1" }));
    expect(await screen.findByText(/SCALE-1 was not billed: Only a completed procedure can be billed/)).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Bill SCALE-1" }));
    const [first, second] = posts(api);
    expect(second.body?.idempotencyKey).toBe(first.body?.idempotencyKey);
  });
});

describe("taking a payment", () => {
  it("sends the amount, method and note with the account version and a key, and shows the new balance", async () => {
    const user = userEvent.setup();
    const api = stubApi();
    api.respond["/payments"] = () => { api.account = account({ balanceOwed: 40, rowVersion: "v2" }); return json(api.account); };
    await openPanel(true);
    await fillPayment(user, "60", "Card");
    await user.type(paymentForm().getByLabelText("Note (optional)"), "Front desk");
    await user.click(paymentForm().getByRole("button", { name: "Record payment" }));

    const [post] = posts(api);
    expect(post.url).toContain(`/api/patients/${P}/billing/payments`);
    expect(post.body).toMatchObject({ amount: 60, method: "Card", note: "Front desk", rowVersion: "v1" });
    expect(typeof post.body?.idempotencyKey).toBe("string");
    expect(await screen.findByText("Balance owed: $40.00")).toBeInTheDocument();
    expect(screen.getByRole("status")).toHaveTextContent("Payment of $60.00 recorded. Balance owed is now $40.00.");
    expect(paymentForm().getByLabelText(/^Amount/)).toHaveValue("");           // the form is ready for the next one
  });

  it("names the gaps beside their fields and sends nothing", async () => {
    const user = userEvent.setup();
    const api = stubApi();
    await openPanel(true);
    await user.click(paymentForm().getByRole("button", { name: "Record payment" }));
    expect(await screen.findByText("Enter the payment amount.")).toBeInTheDocument();
    expect(screen.getByText("Choose how the payment was received.")).toBeInTheDocument();
    expect(screen.getByRole("alert")).toHaveTextContent("Nothing was sent.");
    expect(posts(api)).toHaveLength(0);
  });

  it("shows the server's refusal beside the field and keeps what was typed", async () => {
    const user = userEvent.setup();
    stubApi({ respond: { "/payments": () => json({ error: "validation_failed", message: "Some entries need attention.", fieldErrors: { amount: "A payment cannot be more than the balance owed (100.00 USD). Check the amount and the patient." } }, 400) } });
    await openPanel(true);
    await fillPayment(user, "500");
    await user.click(paymentForm().getByRole("button", { name: "Record payment" }));
    expect(await screen.findByText(/A payment cannot be more than the balance owed \(100\.00 USD\)/)).toBeInTheDocument();
    expect(paymentForm().getByLabelText(/^Amount/)).toHaveValue("500");
    expect(screen.getAllByRole("alert")[0]).toHaveTextContent("Not recorded: some entries need correcting.");
  });

  it("when the server could not save it says nothing was recorded, keeps what was typed, and a retry carries the same key", async () => {
    const user = userEvent.setup();
    const api = stubApi({ respond: { "/payments": () => json({ error: "save_failed", message: "The entry could not be saved, so nothing was recorded. Try again; if it keeps failing, contact support.", fieldErrors: {} }, 503) } });
    await openPanel(true);
    await fillPayment(user, "30");
    await user.click(paymentForm().getByRole("button", { name: "Record payment" }));
    expect(await screen.findByText(/Not recorded: The entry could not be saved, so nothing was recorded/)).toBeInTheDocument();
    expect(paymentForm().getByLabelText(/^Amount/)).toHaveValue("30");
    expect(screen.getByText("Balance owed: $100.00")).toBeInTheDocument();
    await user.click(paymentForm().getByRole("button", { name: "Record payment" }));
    const [first, second] = posts(api);
    expect(second.body?.idempotencyKey).toBe(first.body?.idempotencyKey);      // a retry cannot record it twice
  });

  it("a dropped connection says so and keeps what was typed", async () => {
    const user = userEvent.setup();
    stubApi();
    await openPanel(true);
    vi.stubGlobal("fetch", vi.fn().mockImplementation((url: RequestInfo | URL) => String(url).includes("csrf") ? Promise.resolve(json({ token: "c" })) : Promise.reject(new TypeError("network"))));
    await fillPayment(user, "30");
    await user.click(paymentForm().getByRole("button", { name: "Record payment" }));
    expect(await screen.findByText(/Not recorded: the connection dropped/)).toBeInTheDocument();
    expect(paymentForm().getByLabelText(/^Amount/)).toHaveValue("30");
  });

  it("a stale account shows the conflict banner, says nothing was recorded, and reloads on request", async () => {
    const user = userEvent.setup();
    const api = stubApi({ respond: { "/payments": () => json({ error: "concurrency_conflict", message: "stale" }, 409) } });
    await openPanel(true);
    await fillPayment(user, "30");
    await user.click(paymentForm().getByRole("button", { name: "Record payment" }));
    expect(await screen.findByText(/Not recorded: someone else changed this account/)).toBeInTheDocument();
    const loads = api.calls.filter((c) => c.method === "GET" && !c.url.includes("csrf")).length;
    await user.click(screen.getByRole("button", { name: /reload/i }));
    expect(api.calls.filter((c) => c.method === "GET" && !c.url.includes("csrf")).length).toBeGreaterThan(loads);
    expect(screen.getByRole("status")).toHaveTextContent("Reloaded.");
  });
});

describe("correcting the balance", () => {
  it("sends a signed amount and the reason with the account version", async () => {
    const user = userEvent.setup();
    const api = stubApi();
    api.respond["/adjustments"] = () => { api.account = account({ balanceOwed: 75, rowVersion: "v2" }); return json(api.account); };
    await openPanel(true);
    await user.type(adjustmentForm().getByLabelText(/^Amount/), "-25");
    await user.type(adjustmentForm().getByLabelText("Reason"), "Fee entered wrongly");
    await user.click(adjustmentForm().getByRole("button", { name: "Record adjustment" }));
    expect(posts(api)[0].body).toMatchObject({ amount: -25, reason: "Fee entered wrongly", rowVersion: "v1" });
    expect(await screen.findByText("Balance owed: $75.00")).toBeInTheDocument();
    expect(screen.getByRole("status")).toHaveTextContent("Adjustment of -$25.00 recorded. Balance owed is now $75.00.");
  });

  it("needs a reason and an amount before anything is sent", async () => {
    const user = userEvent.setup();
    const api = stubApi();
    await openPanel(true);
    await user.click(adjustmentForm().getByRole("button", { name: "Record adjustment" }));
    expect(await screen.findByText("An adjustment needs a reason, so the history explains it.")).toBeInTheDocument();
    expect(screen.getByText(/Enter the adjustment amount/)).toBeInTheDocument();
    expect(posts(api)).toHaveLength(0);
  });

  it("offers no payment once nothing is owed but still allows a correction", async () => {
    stubApi({ account: account({ balanceOwed: 0 }) });
    await openPanel(true);
    expect(screen.queryByRole("form", { name: "Record a payment" })).not.toBeInTheDocument();
    expect(screen.getByRole("form", { name: "Record an adjustment" })).toBeInTheDocument();
  });
});
