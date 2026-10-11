import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import type { PatientDetail } from "../../services/patientsApi";
import type { BillingAccount, BillingEntry, BillingEntryStanding, GuarantorSummary, PaymentMethod, Responsibility } from "../../services/billingApi";
import { BillingPanel } from "./BillingPanel";

/**
 * ALV-007-C01: the billing screen's ledger features against a scripted API. The rules (allocation caps, reversal rules, responsibility, method configuration) are proven by the backend tests and the real-backend
 * walkthrough; what is proven here is the UI's behaviour for each outcome: what each entry stands for shown beside it, reversal and apply flows that name gaps before sending and keep what was typed, allocations
 * sent as chosen and refused beside their charge, a payment under a configured method and as paid by the responsible party, responsibility and payment-method changes, and secondary panels that disappear quietly
 * (never an alert, never a guess) when they cannot be read.
 */
const P = "aaaaaaaa-0000-0000-0000-000000000001";
const patient = { id: P, guarantor: { id: "g1", displayName: "Gus Guarantor", isActive: true } } as PatientDetail;
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

const entry = (over: Partial<BillingEntry>): BillingEntry => ({
  entryNumber: 1, kind: "Charge", amount: 100, balanceAfter: 100, planItemId: "i1", procedureCode: "PERIO-1", procedureDescription: "Periodontal maintenance", paymentMethod: null, reason: null,
  recordedAtUtc: "2030-01-01T10:00:00Z", recordedByName: "Beth Billing", ...over,
});
const standing = (n: number, over: Partial<BillingEntryStanding> = {}): BillingEntryStanding => ({
  entryNumber: n, isReversed: false, reversedByEntryNumber: null, paidAmount: null, openAmount: null, appliedAmount: null, unappliedAmount: null, ...over,
});

/** Two charges (100 and 70) and a payment of 60 that has not been applied to either: balance 110. */
const unapplied = (): BillingAccount => ({
  patientId: P, hasAccount: true, balanceOwed: 110, currency: "USD", rowVersion: "v1", unappliedTotal: 60, openChargeTotal: 170,
  entries: [
    entry({ entryNumber: 1 }),
    entry({ entryNumber: 2, procedureCode: "SCALE-1", procedureDescription: "Scaling", amount: 70, balanceAfter: 170, planItemId: "i2" }),
    entry({ entryNumber: 3, kind: "Payment", amount: -60, balanceAfter: 110, planItemId: null, procedureCode: null, procedureDescription: null, paymentMethod: "Cash", paymentMethodName: "Cash" }),
  ],
  standing: [standing(1, { paidAmount: 0, openAmount: 100 }), standing(2, { paidAmount: 0, openAmount: 70 }), standing(3, { appliedAmount: 0, unappliedAmount: 60 })],
  allocations: [],
});

/** A charge of 100, a payment of 50 that was applied to it and then reversed (entry 3): balance 100, the payment shown as reversed. */
const reversed = (): BillingAccount => ({
  patientId: P, hasAccount: true, balanceOwed: 100, currency: "USD", rowVersion: "v9", unappliedTotal: 0, openChargeTotal: 100,
  entries: [
    entry({ entryNumber: 1 }),
    entry({ entryNumber: 2, kind: "Payment", amount: -50, balanceAfter: 50, planItemId: null, procedureCode: null, procedureDescription: null, paymentMethod: "Cash", paymentMethodName: "Cash" }),
    entry({ entryNumber: 3, kind: "Reversal", amount: 50, balanceAfter: 100, planItemId: null, procedureCode: null, procedureDescription: null, reason: "Paid on the wrong account", reversesEntryNumber: 2 }),
  ],
  standing: [standing(1, { paidAmount: 0, openAmount: 100 }), standing(2, { isReversed: true, reversedByEntryNumber: 3, appliedAmount: 50, unappliedAmount: 0 }), standing(3)],
  allocations: [{ allocationNumber: 1, paymentEntryNumber: 2, chargeEntryNumber: 1, amount: 50, released: true, allocatedAtUtc: "2030-01-01T10:00:00Z", allocatedByName: "Beth Billing" }],
});

const selfResponsible = (): Responsibility => ({ patientId: P, responsiblePatientId: P, responsibleName: "Ann Alder", isSelf: true, source: "Self", effectiveFrom: null, rowVersion: "v1", history: [] });
const guarantorResponsible = (): Responsibility => ({
  patientId: P, responsiblePatientId: "g1", responsibleName: "Gus Guarantor", isSelf: false, source: "Recorded", effectiveFrom: "2030-01-01", rowVersion: "v1",
  history: [{ sequence: 1, responsiblePatientId: "g1", responsibleName: "Gus Guarantor", isSelf: false, effectiveFrom: "2030-01-01", reason: "Gus pays for Ann", setAtUtc: "2030-01-01T10:00:00Z", setByName: "Beth Billing" }],
});
const builtIns = (): PaymentMethod[] => ["Card", "Cash", "Check", "Other"].map((n) => ({ id: `m-${n}`, name: n, kind: n, isBuiltIn: true, isActive: true, rowVersion: "mv" }));
const venmo = (over: Partial<PaymentMethod> = {}): PaymentMethod => ({ id: "m-venmo", name: "Venmo", kind: "Other", isBuiltIn: false, isActive: true, rowVersion: "vv1", ...over });

interface Call { url: string; method: string; body: Record<string, unknown> | null }
type Handler = (body: Record<string, unknown> | null) => Response;
interface Api {
  account: BillingAccount;
  responsibility: Responsibility | null;
  summary: GuarantorSummary | null;
  methods: PaymentMethod[] | null;
  calls: Call[];
  post: Record<string, Handler>;
}

function stubApi(over: Partial<Api> = {}): Api {
  const api: Api = { account: unapplied(), responsibility: selfResponsible(), summary: null, methods: builtIns(), calls: [], post: {}, ...over };
  vi.stubGlobal("fetch", vi.fn().mockImplementation((url: RequestInfo | URL, init?: RequestInit) => {
    const u = String(url);
    const method = init?.method ?? "GET";
    const body = init?.body ? (JSON.parse(init.body as string) as Record<string, unknown>) : null;
    api.calls.push({ url: u, method, body });
    if (u.includes("/api/auth/csrf-token")) return Promise.resolve(json({ token: "csrf" }));
    if (method === "POST") {
      for (const [fragment, handler] of Object.entries(api.post)) if (u.includes(fragment)) return Promise.resolve(handler(body));
      return Promise.resolve(json(api.account));
    }
    if (u.includes("/billable-procedures")) return Promise.resolve(json([]));
    if (u.includes("/billing/responsibility")) return Promise.resolve(api.responsibility ? json(api.responsibility) : json({ error: "server_error", message: "boom" }, 500));
    if (u.includes("/billing/guarantor-summary")) return Promise.resolve(api.summary ? json(api.summary) : json({ error: "server_error", message: "boom" }, 500));
    if (u.includes("/api/billing/payment-methods")) return Promise.resolve(api.methods ? json(api.methods) : json({ error: "server_error", message: "boom" }, 500));
    return Promise.resolve(json(api.account));
  }));
  return api;
}

const posts = (api: Api, fragment?: string) => api.calls.filter((c) => c.method === "POST" && !c.url.includes("csrf") && (!fragment || c.url.includes(fragment)));
const openPanel = async (canWrite: boolean) => {
  render(<BillingPanel patient={patient} canWrite={canWrite} />);
  await screen.findByRole("heading", { name: "Account history" });
};
const status = () => screen.getByRole("status");

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("reading the ledger", () => {
  it("shows what each entry stands for beside it: paid and open on a charge, applied and unapplied on a payment, and reversed with the entry that undid it", async () => {
    stubApi({ account: reversed() });
    await openPanel(false);
    const rows = within(screen.getByRole("table", { name: "Account history" })).getAllByRole("row");
    expect(within(rows[1]).getByText(/Paid \$0\.00, open \$100\.00/)).toBeInTheDocument();
    expect(within(rows[2]).getByText(/Reversed by entry 3/)).toBeInTheDocument();
    expect(within(rows[3]).getByText(/Reverses entry 2/)).toBeInTheDocument();
    expect(within(rows[3]).getByText(/Paid on the wrong account/)).toBeInTheDocument();
    expect(screen.getByText(/Still to pay on charges: \$100\.00\. Payments not yet applied to a charge: \$0\.00\./)).toBeInTheDocument();
  });

  it("shows an unapplied payment's amounts and offers a reader no way to change anything", async () => {
    stubApi({ summary: { guarantorPatientId: P, guarantorName: "Ann Alder", currency: "USD", totalOwed: 110, accounts: [{ patientId: P, patientName: "Ann Alder", isGuarantor: true, balanceOwed: 110, openChargeTotal: 170, unappliedTotal: 60 }] } });
    await openPanel(false);
    expect(screen.getByText(/Applied \$0\.00, unapplied \$60\.00/)).toBeInTheDocument();
    expect(screen.queryByRole("form")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Reverse|Apply|Record|Add method|Stop using/ })).not.toBeInTheDocument();
    expect(screen.queryByText("Payment methods")).not.toBeInTheDocument();
    expect(screen.queryByText(/Accounts this patient is responsible for/)).not.toBeInTheDocument();     // only their own account: nothing beyond it to show
  });

  it("says who is responsible and where that comes from, with the history, and lists the accounts a guarantor answers for", async () => {
    stubApi({
      responsibility: guarantorResponsible(),
      summary: { guarantorPatientId: P, guarantorName: "Ann Alder", currency: "USD", totalOwed: 210, accounts: [
        { patientId: P, patientName: "Ann Alder", isGuarantor: true, balanceOwed: 110, openChargeTotal: 170, unappliedTotal: 60 },
        { patientId: "d1", patientName: "Dan Alder", isGuarantor: false, balanceOwed: 100, openChargeTotal: 100, unappliedTotal: 0 }] },
    });
    await openPanel(false);
    expect(screen.getByText(/Gus Guarantor/, { selector: "strong" })).toBeInTheDocument();
    expect(screen.getByText(/recorded by billing/)).toBeInTheDocument();
    expect(within(screen.getByRole("table", { name: "Who has been responsible" })).getByText("Gus pays for Ann")).toBeInTheDocument();
    const table = screen.getByRole("table", { name: "Accounts this patient is responsible for" });
    expect(within(table).getByText("Dan Alder")).toBeInTheDocument();
    expect(within(table).getByText("Ann Alder (this patient)")).toBeInTheDocument();
    expect(within(table).getByText("$210.00")).toBeInTheDocument();
  });

  it("leaves the responsibility, summary and method panels out without any alert when they cannot be read, and still offers the four built-in methods", async () => {
    const user = userEvent.setup();
    stubApi({ responsibility: null, summary: null, methods: null });
    await openPanel(true);
    expect(screen.queryByText("Who is responsible for this account")).not.toBeInTheDocument();
    expect(screen.queryByText("Payment methods")).not.toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    const select = within(screen.getByRole("form", { name: "Record a payment" })).getByLabelText("How it was received");
    expect(within(select).getAllByRole("option").map((o) => o.textContent)).toEqual(["Choose a method", "Cash", "Check", "Card", "Other"]);
    await user.click(screen.getByRole("button", { name: "Reverse entry 3" }));
    expect(screen.getByRole("form", { name: "Reverse entry 3" })).toBeInTheDocument();
  });
});

describe("reversing an entry", () => {
  it("offers a reversal for an entry that can be reversed and none for a reversal or an entry already reversed", async () => {
    stubApi({ account: reversed() });
    await openPanel(true);
    expect(screen.getByRole("button", { name: "Reverse entry 1" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Reverse entry 2" })).not.toBeInTheDocument();      // already reversed
    expect(screen.queryByRole("button", { name: "Reverse entry 3" })).not.toBeInTheDocument();      // a reversal
  });

  it("needs a reason before anything is sent, then sends the entry, the reason, the account version and a key, and shows the new balance", async () => {
    const user = userEvent.setup();
    const api = stubApi({ account: unapplied() });
    api.post["/reversals"] = () => json({ ...reversed(), balanceOwed: 170 });
    await openPanel(true);
    await user.click(screen.getByRole("button", { name: "Reverse entry 3" }));
    const form = within(screen.getByRole("form", { name: "Reverse entry 3" }));
    await user.click(form.getByRole("button", { name: "Reverse entry" }));
    expect(await screen.findByText("A reversal needs a reason, so the history explains it.")).toBeInTheDocument();
    expect(posts(api)).toHaveLength(0);

    await user.type(form.getByLabelText("Why is it being reversed?"), "Paid on the wrong account");
    await user.click(form.getByRole("button", { name: "Reverse entry" }));
    const [post] = posts(api, "/reversals");
    expect(post.body).toMatchObject({ entryNumber: 3, reason: "Paid on the wrong account", rowVersion: "v1" });
    expect(typeof post.body?.idempotencyKey).toBe("string");
    expect(await screen.findByText("Balance owed: $170.00")).toBeInTheDocument();
    expect(status()).toHaveTextContent("Payment 3 reversed. Balance owed is now $170.00.");
    expect(screen.queryByRole("form", { name: "Reverse entry 3" })).not.toBeInTheDocument();
  });

  it("shows why a charge that still has payments cannot be reversed, keeps the reason typed and retries under the same key", async () => {
    const user = userEvent.setup();
    const api = stubApi();
    api.post["/reversals"] = () => json({ error: "charge_has_payments", message: "Charge 1 has payments applied to it. Reverse those payments first, then reverse the charge.", fieldErrors: {} }, 409);
    await openPanel(true);
    await user.click(screen.getByRole("button", { name: "Reverse entry 1" }));
    const form = within(screen.getByRole("form", { name: "Reverse entry 1" }));
    await user.type(form.getByLabelText("Why is it being reversed?"), "Billed in error");
    await user.click(form.getByRole("button", { name: "Reverse entry" }));
    expect(await screen.findByText(/Not recorded: Charge 1 has payments applied to it/)).toBeInTheDocument();
    expect(form.getByLabelText("Why is it being reversed?")).toHaveValue("Billed in error");
    await user.click(form.getByRole("button", { name: "Reverse entry" }));
    const [first, second] = posts(api, "/reversals");
    expect(second.body?.idempotencyKey).toBe(first.body?.idempotencyKey);
  });

  it("a stale account shows the conflict banner and says nothing was recorded", async () => {
    const user = userEvent.setup();
    const api = stubApi();
    api.post["/reversals"] = () => json({ error: "concurrency_conflict", message: "stale" }, 409);
    await openPanel(true);
    await user.click(screen.getByRole("button", { name: "Reverse entry 3" }));
    await user.type(screen.getByLabelText("Why is it being reversed?"), "Wrong account");
    await user.click(screen.getByRole("button", { name: "Reverse entry" }));
    expect(await screen.findByText(/Not recorded: someone else changed this account/)).toBeInTheDocument();
    expect(screen.getByText("Someone else changed this while you were editing")).toBeInTheDocument();
  });
});

describe("applying what a payment still holds", () => {
  it("is offered only for a payment with something unapplied, and by default sends no charges so the oldest are paid first", async () => {
    const user = userEvent.setup();
    const api = stubApi();
    api.post["/payments/apply"] = () => json({ ...unapplied(), unappliedTotal: 0 });
    await openPanel(true);
    await user.click(screen.getByRole("button", { name: "Apply payment 3" }));
    await user.click(within(screen.getByRole("form", { name: "Apply payment 3" })).getByRole("button", { name: "Apply payment" }));
    const [post] = posts(api, "/payments/apply");
    expect(post.body).toMatchObject({ paymentEntryNumber: 3, rowVersion: "v1" });
    expect(post.body).not.toHaveProperty("allocations");
    expect(await screen.findByText(/Payment 3 applied\. \$0\.00 of payments is still unapplied\./)).toBeInTheDocument();
  });

  it("sends the charges that were chosen and shows a refusal beside the charge it is about", async () => {
    const user = userEvent.setup();
    const api = stubApi();
    api.post["/payments/apply"] = () => json({ error: "validation_failed", message: "Some entries need attention.", fieldErrors: { "allocations[0]": "Charge 2 has only 70.00 USD left to pay." } }, 400);
    await openPanel(true);
    await user.click(screen.getByRole("button", { name: "Apply payment 3" }));
    const form = within(screen.getByRole("form", { name: "Apply payment 3" }));
    await user.click(form.getByLabelText("Choose which charges to pay"));
    await user.type(form.getByLabelText("Apply to charge 2 (US dollars)"), "80");
    await user.click(form.getByRole("button", { name: "Apply payment" }));
    expect(posts(api, "/payments/apply")[0].body).toMatchObject({ paymentEntryNumber: 3, allocations: [{ chargeEntryNumber: 2, amount: 80 }] });
    expect(await screen.findByText("Charge 2 has only 70.00 USD left to pay.")).toBeInTheDocument();
    expect(form.getByLabelText("Apply to charge 2 (US dollars)")).toHaveValue("80");                         // what was typed is kept
  });

  it("is not offered for a payment that is fully applied", async () => {
    stubApi({ account: { ...unapplied(), standing: unapplied().standing!.map((s) => (s.entryNumber === 3 ? { ...s, appliedAmount: 60, unappliedAmount: 0 } : s)) } });
    await openPanel(true);
    expect(screen.queryByRole("button", { name: "Apply payment 3" })).not.toBeInTheDocument();
  });
});

describe("taking a payment with the practice's methods, charges and payer", () => {
  it("offers the configured methods, sends the chosen charges and who paid, and refuses to send a choice that names no charge", async () => {
    const user = userEvent.setup();
    const api = stubApi({ methods: [...builtIns(), venmo()], responsibility: guarantorResponsible() });
    api.post["/billing/payments"] = () => json(unapplied());
    await openPanel(true);
    const form = within(screen.getByRole("form", { name: "Record a payment" }));
    expect(within(form.getByLabelText("How it was received")).getAllByRole("option").map((o) => o.textContent)).toContain("Venmo");
    expect(within(form.getByLabelText("Who paid")).getAllByRole("option").map((o) => o.textContent)).toEqual(["The patient", "Gus Guarantor (responsible for this account)"]);

    await user.type(form.getByLabelText(/^Amount/), "50");
    await user.selectOptions(form.getByLabelText("How it was received"), "Venmo");
    await user.selectOptions(form.getByLabelText("Who paid"), "g1");
    await user.click(form.getByLabelText(/Choose which charges this pays/));
    await user.click(form.getByRole("button", { name: "Record payment" }));
    expect(await screen.findByText(/Name at least one charge/)).toBeInTheDocument();
    expect(posts(api, "/billing/payments")).toHaveLength(0);

    await user.type(form.getByLabelText("Apply to charge 1 (US dollars)"), "50");
    await user.click(form.getByRole("button", { name: "Record payment" }));
    expect(posts(api, "/billing/payments")[0].body).toMatchObject({ amount: 50, method: "Venmo", paidByPatientId: "g1", rowVersion: "v1", allocations: [{ chargeEntryNumber: 1, amount: 50 }] });
  });

  it("shows a refusal of one allocation beside its charge and what the server said about the payer beside the payer", async () => {
    const user = userEvent.setup();
    const api = stubApi({ responsibility: guarantorResponsible() });
    api.post["/billing/payments"] = () => json({ error: "validation_failed", message: "Some entries need attention.", fieldErrors: { "allocations[0]": "Charge 1 has only 100.00 USD left to pay.", paidBy: "Only the patient, or someone recorded as responsible for this account, can be recorded as having paid it." } }, 400);
    await openPanel(true);
    const form = within(screen.getByRole("form", { name: "Record a payment" }));
    await user.type(form.getByLabelText(/^Amount/), "150");
    await user.selectOptions(form.getByLabelText("How it was received"), "Cash");
    await user.selectOptions(form.getByLabelText("Who paid"), "g1");
    await user.click(form.getByLabelText(/Choose which charges this pays/));
    await user.type(form.getByLabelText("Apply to charge 1 (US dollars)"), "150");
    await user.click(form.getByRole("button", { name: "Record payment" }));
    expect(await screen.findByText("Charge 1 has only 100.00 USD left to pay.")).toBeInTheDocument();
    expect(screen.getByText(/Only the patient, or someone recorded as responsible/)).toBeInTheDocument();
    expect(form.getByLabelText("Apply to charge 1 (US dollars)")).toHaveValue("150");
  });
});

describe("who is responsible, and the payment methods", () => {
  it("records the patient or the registered guarantor as responsible with the account's version and a reason", async () => {
    const user = userEvent.setup();
    const api = stubApi();
    api.post["/billing/responsibility"] = () => json(guarantorResponsible());
    await openPanel(true);
    const form = within(screen.getByRole("form", { name: "Record who is responsible" }));
    expect(within(form.getByLabelText("Responsible party")).getAllByRole("option").map((o) => o.textContent)).toEqual(["The patient", "Gus Guarantor (registered guarantor)"]);
    await user.selectOptions(form.getByLabelText("Responsible party"), "g1");
    await user.type(form.getByLabelText("Why (optional)"), "Gus pays for Ann");
    await user.click(form.getByRole("button", { name: "Record responsibility" }));
    expect(posts(api, "/billing/responsibility")[0].body).toMatchObject({ responsiblePatientId: "g1", reason: "Gus pays for Ann", rowVersion: "v1" });
    expect(await screen.findByText("Gus Guarantor is recorded as responsible for this account.")).toBeInTheDocument();
  });

  it("always offers the party who is currently responsible, even when they are neither the patient nor the registered guarantor", async () => {
    stubApi({ responsibility: { ...guarantorResponsible(), responsiblePatientId: "x9", responsibleName: "Xena Payer" } });
    await openPanel(true);
    const select = within(screen.getByRole("form", { name: "Record who is responsible" })).getByLabelText("Responsible party");
    expect(within(select).getAllByRole("option").map((o) => o.textContent)).toEqual(["The patient", "Gus Guarantor (registered guarantor)", "Xena Payer (currently responsible)"]);
    expect(select).toHaveValue("x9");                                                                          // the form starts on who is responsible now, never on a different choice
  });

  it("offers the form to record who is responsible only once something has been billed, like the payment and adjustment forms, and says so before that", async () => {
    stubApi({ account: { ...unapplied(), hasAccount: false, rowVersion: null, balanceOwed: 0, entries: [], standing: [], unappliedTotal: 0, openChargeTotal: 0 } });
    await openPanel(true);
    for (const name of ["Record who is responsible", "Record a payment", "Record an adjustment"]) expect(screen.queryByRole("form", { name })).not.toBeInTheDocument();
    expect(screen.getByText("Who is responsible can be recorded once something has been billed to this account.")).toBeInTheDocument();
    expect(screen.getByText(/the patient, because no guarantor is registered/)).toBeInTheDocument();     // who is responsible is still shown
  });

  it("says nothing was changed and keeps the form when billing refuses the change", async () => {
    const user = userEvent.setup();
    const api = stubApi();
    api.post["/billing/responsibility"] = () => json({ error: "validation_failed", message: "Some entries need attention.", fieldErrors: { responsiblePatientId: "Only an active patient can be responsible for an account." } }, 400);
    await openPanel(true);
    await user.selectOptions(screen.getByLabelText("Responsible party"), "g1");
    await user.click(screen.getByRole("button", { name: "Record responsibility" }));
    expect(await screen.findByText(/Not changed: some entries need correcting/)).toBeInTheDocument();
    expect(screen.getByText("Only an active patient can be responsible for an account.")).toBeInTheDocument();
  });

  it("lists the methods with built-ins always in use, adds a named method of one of the four kinds, and stops using one without deleting it", async () => {
    const user = userEvent.setup();
    const api = stubApi({ methods: [...builtIns(), venmo()] });
    api.post["/payment-methods/m-venmo/active"] = () => json(venmo({ isActive: false, rowVersion: "vv2" }));
    api.post["/api/billing/payment-methods"] = () => json(venmo({ id: "m-zelle", name: "Zelle" }));
    await openPanel(true);
    const section = screen.getByText("Payment methods").closest("details")!;
    await user.click(screen.getByText("Payment methods"));
    const table = within(section).getByRole("table", { name: "Payment methods" });
    expect(within(table).getAllByRole("button").map((b) => b.textContent)).toEqual(["Stop using Venmo"]);       // a built-in has no button

    await user.type(within(section).getByLabelText("Method name"), "Zelle");
    await user.click(within(section).getByRole("button", { name: "Add method" }));
    expect(posts(api, "/api/billing/payment-methods").find((c) => !c.url.includes("/active"))?.body).toEqual({ name: "Zelle", kind: "Other" });
    expect(await screen.findByText("Payment method Zelle is available.")).toBeInTheDocument();

    await user.click(within(section).getByRole("button", { name: "Stop using Venmo" }));
    expect(posts(api, "/active")[0].body).toEqual({ active: false, rowVersion: "vv1" });
    expect(await screen.findByText("Venmo is no longer offered for new payments.")).toBeInTheDocument();
  });

  it("shows why a method could not be added and keeps what was typed", async () => {
    const user = userEvent.setup();
    const api = stubApi({ methods: builtIns() });
    api.post["/api/billing/payment-methods"] = () => json({ error: "method_exists", message: "A payment method named Cash already exists (as Cash).", fieldErrors: {} }, 409);
    await openPanel(true);
    await user.click(screen.getByText("Payment methods"));
    await user.type(screen.getByLabelText("Method name"), "cash");
    await user.click(screen.getByRole("button", { name: "Add method" }));
    expect(await screen.findByText(/Not added: A payment method named Cash already exists/)).toBeInTheDocument();
    expect(screen.getByLabelText("Method name")).toHaveValue("cash");
  });
});

describe("accessibility", () => {
  it("has no violations with the responsibility form, the payment form with its charge chooser, a reversal form and the method settings open", async () => {
    const user = userEvent.setup();
    stubApi({ methods: [...builtIns(), venmo()], responsibility: guarantorResponsible(), summary: { guarantorPatientId: P, guarantorName: "Ann Alder", currency: "USD", totalOwed: 210, accounts: [
      { patientId: P, patientName: "Ann Alder", isGuarantor: true, balanceOwed: 110, openChargeTotal: 170, unappliedTotal: 60 }, { patientId: "d1", patientName: "Dan Alder", isGuarantor: false, balanceOwed: 100, openChargeTotal: 100, unappliedTotal: 0 }] } });
    await openPanel(true);
    await user.click(screen.getByLabelText(/Choose which charges this pays/));
    await user.click(screen.getByRole("button", { name: "Reverse entry 3" }));
    await user.click(screen.getByText("Payment methods"));
    expect(await axe(document.body)).toHaveNoViolations();
  });

  it("has no violations for a reader looking at a reversed payment and a guarantor's accounts", async () => {
    stubApi({ account: reversed(), responsibility: guarantorResponsible() });
    await openPanel(false);
    expect(await axe(document.body)).toHaveNoViolations();
  });
});
