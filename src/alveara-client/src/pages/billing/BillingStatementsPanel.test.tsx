import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import type { PatientDetail } from "../../services/patientsApi";
import type { BillingAccount, BillingEntry, BillingEntryStanding, PaymentMethod } from "../../services/billingApi";
import type { AgingView, DocumentPreview, IssuedDocument, IssuedDocumentDetail, StatementContent, TimelineEvent } from "../../services/billingStatementsApi";
import { BillingPanel } from "./BillingPanel";

/**
 * ALV-007-C02: the billing screen's refund, move, correction, statement, receipt, timeline and aging features against a scripted API. The rules are proven by the backend tests and the real-backend walkthrough;
 * what is proven here is the UI's behaviour for each outcome: each form says what it will do before anything is sent, names gaps before sending and keeps what was typed, sends the account version and a key that
 * is kept while the request is unchanged, shows a refusal beside its field, previews a document and issues exactly that preview (re-previewing when the server says it is stale), shows an issued document as
 * issued with what has happened since written apart, refuses to show one that cannot be rebuilt, and gives a reader the receipt and statement previews but no way to change anything.
 */
const P = "aaaaaaaa-0000-0000-0000-000000000001";
const patient = { id: P, firstName: "Ann", lastName: "Alder", guarantor: null } as unknown as PatientDetail;
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

const entry = (over: Partial<BillingEntry>): BillingEntry => ({
  entryNumber: 1, kind: "Charge", amount: 100, balanceAfter: 100, planItemId: "i1", procedureCode: "PERIO-1", procedureDescription: "Periodontal maintenance", paymentMethod: null, reason: null,
  recordedAtUtc: "2030-01-01T10:00:00Z", recordedByName: "Beth Billing", ...over,
});
const standing = (n: number, over: Partial<BillingEntryStanding> = {}): BillingEntryStanding => ({
  entryNumber: n, isReversed: false, reversedByEntryNumber: null, paidAmount: null, openAmount: null, appliedAmount: null, unappliedAmount: null, ...over,
});

/** Charges 1 (100) and 2 (70); payment 3 of 90: 50 on charge 1 and 40 on charge 2. Balance 80. */
const paid = (): BillingAccount => ({
  patientId: P, hasAccount: true, balanceOwed: 80, currency: "USD", rowVersion: "v1", unappliedTotal: 0, openChargeTotal: 80,
  entries: [
    entry({ entryNumber: 1 }),
    entry({ entryNumber: 2, procedureCode: "SCALE-1", procedureDescription: "Scaling", amount: 70, balanceAfter: 170, planItemId: "i2" }),
    entry({ entryNumber: 3, kind: "Payment", amount: -90, balanceAfter: 80, planItemId: null, procedureCode: null, procedureDescription: null, paymentMethod: "Cash", paymentMethodName: "Cash" }),
  ],
  standing: [
    standing(1, { paidAmount: 50, openAmount: 50 }), standing(2, { paidAmount: 40, openAmount: 30 }),
    standing(3, { appliedAmount: 90, unappliedAmount: 0, refundedAmount: 0, refundableAmount: 90 }),
  ],
  allocations: [
    { allocationNumber: 1, paymentEntryNumber: 3, chargeEntryNumber: 1, amount: 50, released: false, allocatedAtUtc: "2030-01-01T10:00:00Z", allocatedByName: "Beth Billing", releasedAmount: 0 },
    { allocationNumber: 2, paymentEntryNumber: 3, chargeEntryNumber: 2, amount: 40, released: false, allocatedAtUtc: "2030-01-01T10:00:00Z", allocatedByName: "Beth Billing", releasedAmount: 0 },
  ],
});

const builtIns = (): PaymentMethod[] => ["Card", "Cash", "Check", "Other"].map((n) => ({ id: `m-${n}`, name: n, kind: n, isBuiltIn: true, isActive: true, rowVersion: "mv" }));
const party = { patientId: P, name: "Ann Alder", isSelf: true };
const aging = { current: "80.00", days31To60: "0.00", days61To90: "0.00", over90: "0.00" };
const statement = (balance = "80.00"): StatementContent => ({
  documentType: "Statement", templateVersion: 1, asOfUtc: "2030-02-01T10:00:00Z", currency: "USD", patient: party, addressee: { patientId: "g1", name: "Gus Guarantor", isSelf: false },
  lines: [{ number: 1, kind: "Charge", amount: "100.00", balanceAfter: "100.00", procedureCode: "PERIO-1", procedureDescription: "Periodontal maintenance", method: null, reason: null, recordedAtUtc: "2030-01-01T10:00:00Z", reversesNumber: null, refundsNumber: null, replacesNumber: null }],
  openCharges: [{ number: 1, procedureCode: "PERIO-1", procedureDescription: "Periodontal maintenance", amount: "100.00", paid: "50.00", open: "50.00", serviceDateUtc: "2030-01-01T10:00:00Z", ageDays: 31, bucket: "31-60" }],
  unapplied: [], aging, balance, openChargeTotal: "80.00", unappliedTotal: "0.00", practice: { name: "Alder Dental", phone: "555-0100", addressLine: "1 Main St" },
});
const preview = (token = "tok-1", balance?: string): DocumentPreview => ({
  documentType: "Statement", cutOffs: { entryNumber: 3, allocationNumber: 2, releaseNumber: 0, responsibilitySequence: 0, asOfUtc: "2030-02-01T10:00:00Z" }, templateVersion: 1, contentHash: "a".repeat(64), previewToken: token, statement: statement(balance), receipt: null,
});
const receiptPreview = (): DocumentPreview => ({
  ...preview("tok-r"), documentType: "Receipt", statement: null,
  receipt: { documentType: "Receipt", templateVersion: 1, asOfUtc: "2030-02-01T10:00:00Z", currency: "USD", patient: party, receivedFrom: party, paymentNumber: 3, amount: "90.00", method: "Cash", receivedAtUtc: "2030-01-01T10:00:00Z",
    applications: [{ chargeNumber: 1, procedureCode: "PERIO-1", procedureDescription: "Periodontal maintenance", amount: "50.00" }], unapplied: "0.00", refundedToDate: "0.00", balanceOwed: "80.00" },
});
const issuedDoc = (): IssuedDocument => ({ id: "d1", documentType: "Statement", documentNumber: 1, paymentEntryNumber: null, cutOffs: preview().cutOffs, templateVersion: 1, contentHash: "a".repeat(64), issuedAtUtc: "2030-02-01T10:00:01Z", issuedByName: "Beth Billing" });
const detailOf = (notes: string[] = []): IssuedDocumentDetail => ({ document: issuedDoc(), statement: statement(), receipt: null, currentStatus: notes });

interface Call { url: string; method: string; body: Record<string, unknown> | null }
type Handler = (body: Record<string, unknown> | null) => Response;
interface Api {
  account: BillingAccount;
  calls: Call[];
  post: Record<string, Handler>;
  preview: () => Response;
  documents: IssuedDocument[];
  detail: () => Response;
  timeline: () => Response;
  aging: () => Response;
}
const agingView = (): AgingView => ({ asOfUtc: "2030-02-01T10:00:00Z", currency: "USD", buckets: aging, openCharges: statement().openCharges });
const timelineOf = (): TimelineEvent[] => [
  { atUtc: "2030-01-01T10:00:00Z", type: "Charge", number: 1, summary: "Charge for PERIO-1 Periodontal maintenance", amount: 100, source: "Completed procedure performed 2030-01-01", actorName: "Beth Billing", planItemId: "i1", completedProcedureId: "cp1" },
  { atUtc: "2030-01-02T10:00:00Z", type: "Payment", number: 3, summary: "Payment by Cash", amount: -90, source: null, actorName: "Beth Billing" },
];

function stubApi(over: Partial<Api> = {}): Api {
  const api: Api = { account: paid(), calls: [], post: {}, preview: () => json(preview()), documents: [], detail: () => json(detailOf()), timeline: () => json(timelineOf()), aging: () => json(agingView()), ...over };
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
    if (u.includes("/billing/responsibility")) return Promise.resolve(json({ patientId: P, responsiblePatientId: P, responsibleName: "Ann Alder", isSelf: true, source: "Self", effectiveFrom: null, rowVersion: "v1", history: [] }));
    if (u.includes("/billing/guarantor-summary")) return Promise.resolve(json({ error: "server_error", message: "boom" }, 500));
    if (u.includes("/api/billing/payment-methods")) return Promise.resolve(json(builtIns()));
    if (u.includes("/documents/preview")) return Promise.resolve(api.preview());
    if (/\/documents\/[^/]+$/.test(u)) return Promise.resolve(api.detail());
    if (u.includes("/billing/documents")) return Promise.resolve(json(api.documents));
    if (u.includes("/billing/timeline")) return Promise.resolve(api.timeline());
    if (u.includes("/billing/aging")) return Promise.resolve(api.aging());
    return Promise.resolve(json(api.account));
  }));
  return api;
}

const posts = (api: Api, fragment: string) => api.calls.filter((c) => c.method === "POST" && c.url.includes(fragment));
const openPanel = async (canWrite: boolean) => {
  render(<BillingPanel patient={patient} canWrite={canWrite} />);
  await screen.findByRole("heading", { name: "Account history" });
};
const typeInto = async (label: string | RegExp, text: string) => userEvent.type(screen.getByLabelText(label), text);

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("what each person is offered", () => {
  it("offers a reader a receipt and a statement preview and no way to refund, move, correct, reverse or issue", async () => {
    stubApi();
    await openPanel(false);
    expect(screen.getByRole("button", { name: "Receipt for payment 3" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Preview a statement" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Refund|Move payment|Correct entry|Reverse entry/ })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Preview a statement" }));
    expect(await screen.findByText("Preview: not issued yet.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Issue this/ })).not.toBeInTheDocument();
  });

  it("offers a billing person each action only where the server's numbers allow it", async () => {
    const account = paid();
    account.standing![2] = standing(3, { appliedAmount: 90, unappliedAmount: 0, refundedAmount: 10, refundableAmount: 80 });
    stubApi({ account });
    await openPanel(true);
    expect(screen.getByRole("button", { name: "Refund payment 3" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Move payment 3" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Correct entry 3" })).not.toBeInTheDocument();      // a refunded payment is not corrected...
    expect(screen.queryByRole("button", { name: "Reverse entry 3" })).not.toBeInTheDocument();       // ...or reversed
    expect(screen.getByText(/refunded \$10\.00/)).toBeInTheDocument();
  });
});

describe("refunding", () => {
  it("says what will happen, names gaps before sending, sends the version and a kept key, and shows the new balance", async () => {
    const api = stubApi({ post: { "/billing/refunds": () => json({ ...paid(), balanceOwed: 130, rowVersion: "v2" }) } });
    await openPanel(true);
    await userEvent.click(screen.getByRole("button", { name: "Refund payment 3" }));
    const form = screen.getByRole("form", { name: "Refund payment 3" });
    expect(within(form).getByText(/The money goes back to Ann Alder, who paid it; it cannot be sent to anyone else\. Up to \$90\.00 can still be refunded/)).toBeInTheDocument();
    expect(within(form).getByText(/newest first, which become open again/)).toBeInTheDocument();

    await userEvent.click(within(form).getByRole("button", { name: /Refund to Ann Alder/ }));
    expect(within(form).getByRole("alert")).toHaveTextContent("Nothing was sent");
    expect(within(form).getByText("Enter the amount to refund.")).toBeInTheDocument();
    expect(within(form).getByText("Choose how the money is returned.")).toBeInTheDocument();
    expect(within(form).getByText("A refund needs a reason, so the history explains it.")).toBeInTheDocument();
    expect(posts(api, "/billing/refunds")).toHaveLength(0);

    await typeInto("Amount to refund (US dollars)", "80");
    await userEvent.selectOptions(screen.getByLabelText("How the money is returned"), "Cash");
    await typeInto("Why is it being refunded?", "Patient request");
    await userEvent.click(within(form).getByRole("button", { name: /Refund to Ann Alder/ }));
    expect(await screen.findByText("Balance owed: $130.00")).toBeInTheDocument();
    expect(posts(api, "/billing/refunds")[0].body).toMatchObject({ paymentEntryNumber: 3, amount: 80, method: "Cash", reason: "Patient request", rowVersion: "v1" });
    expect(typeof posts(api, "/billing/refunds")[0].body!.idempotencyKey).toBe("string");
    expect(screen.getByRole("status")).toHaveTextContent("$80.00 refunded to Ann Alder.");
  });

  it("shows the server's refusal beside the amount, keeps what was typed and keeps the same key for an unchanged retry", async () => {
    let attempts = 0;
    const api = stubApi({
      post: { "/billing/refunds": () => (++attempts === 1 ? json({ error: "validation_failed", message: "x", fieldErrors: { amount: "Payment 3 can be refunded for 90.00 USD at most." } }, 400) : json({ ...paid(), rowVersion: "v2" })) },
    });
    await openPanel(true);
    await userEvent.click(screen.getByRole("button", { name: "Refund payment 3" }));
    await typeInto("Amount to refund (US dollars)", "95");
    await userEvent.selectOptions(screen.getByLabelText("How the money is returned"), "Cash");
    await typeInto("Why is it being refunded?", "Patient request");
    const form = screen.getByRole("form", { name: "Refund payment 3" });
    await userEvent.click(within(form).getByRole("button", { name: /Refund to/ }));
    expect(await within(form).findByText("Payment 3 can be refunded for 90.00 USD at most.")).toBeInTheDocument();
    expect(screen.getByLabelText("Amount to refund (US dollars)")).toHaveValue("95");
    await userEvent.click(within(form).getByRole("button", { name: /Refund to/ }));
    await screen.findByText(/refunded to Ann Alder/);
    const [first, second] = posts(api, "/billing/refunds");
    expect(second.body!.idempotencyKey).toBe(first.body!.idempotencyKey);
  });

  it("says the account changed under the person on a conflict and records nothing", async () => {
    stubApi({ post: { "/billing/refunds": () => json({ error: "concurrency_conflict", message: "changed", entityType: "billing account", entityId: P }, 409) } });
    await openPanel(true);
    await userEvent.click(screen.getByRole("button", { name: "Refund payment 3" }));
    await typeInto("Amount to refund (US dollars)", "10");
    await userEvent.selectOptions(screen.getByLabelText("How the money is returned"), "Cash");
    await typeInto("Why is it being refunded?", "x");
    await userEvent.click(screen.getByRole("button", { name: /Refund to/ }));
    expect(await screen.findByText(/someone else changed this account/)).toBeInTheDocument();
  });
});

describe("moving and correcting", () => {
  it("moves what a payment pays between two named charges for the same amount", async () => {
    const api = stubApi();
    await openPanel(true);
    await userEvent.click(screen.getByRole("button", { name: "Move payment 3" }));
    const form = screen.getByRole("form", { name: "Move what payment 3 pays" });
    expect(within(form).getByText(/The payment, its amount and the balance owed do not change/)).toBeInTheDocument();
    await userEvent.click(within(form).getByRole("button", { name: "Move" }));
    expect(within(form).getByText("Choose the charge to take the money off.")).toBeInTheDocument();
    expect(posts(api, "/allocations/move")).toHaveLength(0);

    await userEvent.selectOptions(screen.getByLabelText("Take it off"), "1");
    await userEvent.selectOptions(screen.getByLabelText("Put it on"), "2");
    await typeInto("Amount to move (US dollars)", "20");
    await typeInto("Why is it being moved?", "Wrong procedure");
    await userEvent.click(within(form).getByRole("button", { name: "Move" }));
    await screen.findByText(/moved from charge 1 to charge 2/);
    expect(posts(api, "/allocations/move")[0].body).toMatchObject({ paymentEntryNumber: 3, releases: [{ chargeEntryNumber: 1, amount: 20 }], allocations: [{ chargeEntryNumber: 2, amount: 20 }], reason: "Wrong procedure", rowVersion: "v1" });
  });

  it("corrects a payment by reversing and replacing it and says the replacement starts unapplied", async () => {
    const api = stubApi();
    await openPanel(true);
    await userEvent.click(screen.getByRole("button", { name: "Correct entry 3" }));
    const form = screen.getByRole("form", { name: "Correct payment 3" });
    expect(within(form).getByText(/reverses entry 3 exactly.*The replacement payment starts unapplied/i)).toBeInTheDocument();
    await userEvent.click(within(form).getByRole("button", { name: "Reverse and replace" }));
    expect(within(form).getByText("Enter the corrected payment amount.")).toBeInTheDocument();
    expect(posts(api, "/billing/corrections")).toHaveLength(0);
    await typeInto(/Corrected payment amount/, "60");
    await userEvent.selectOptions(within(form).getByLabelText("How it was received"), "Cash");
    await typeInto("Why is it being corrected?", "Typed wrong amount");
    await userEvent.click(within(form).getByRole("button", { name: "Reverse and replace" }));
    await screen.findByText(/corrected: reversed and replaced/);
    expect(posts(api, "/billing/corrections")[0].body).toMatchObject({ entryNumber: 3, amount: 60, method: "Cash", reason: "Typed wrong amount", rowVersion: "v1" });
  });
});

describe("statements and receipts", () => {
  it("previews a statement, issues exactly that preview and then shows it as issued with what happened since written apart", async () => {
    const api = stubApi({
      post: { "/billing/documents": () => json(issuedDoc()) },
      detail: () => json(detailOf(["2 ledger entries have been recorded since this statement was issued; the account's current balance is 130.00 USD."])),
    });
    await openPanel(true);
    await userEvent.click(screen.getByRole("button", { name: "Preview a statement" }));
    expect(await screen.findByText("Preview: not issued yet.")).toBeInTheDocument();
    expect(screen.getByText(/Addressed to: Gus Guarantor \(responsible for this account\)/)).toBeInTheDocument();
    expect(within(screen.getByRole("article", { name: "Statement" })).getByText("Alder Dental")).toBeInTheDocument();      // the practice as issued heads the document
    expect(within(screen.getByRole("article", { name: "Statement" })).getByText("Balance owed: $80.00")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Issue this statement" }));
    expect(await screen.findByText(/Shown exactly as issued/)).toBeInTheDocument();
    expect(posts(api, "/billing/documents")[0].body).toMatchObject({ previewToken: "tok-1" });
    expect(typeof posts(api, "/billing/documents")[0].body!.idempotencyKey).toBe("string");
    const notice = screen.getByRole("note", { name: "Since this document was issued" });
    expect(within(notice).getByText(/Since it was issued \(not part of the document\)/)).toBeInTheDocument();
    expect(within(notice).getByText(/2 ledger entries have been recorded since/)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Download CSV" })).toHaveAttribute("href", "/api/patients/" + P + "/billing/documents/d1/csv");
    expect(screen.getByRole("button", { name: "Print" })).toBeInTheDocument();
  });

  it("says a stale preview was not issued and shows the new preview to review instead", async () => {
    let previews = 0;
    const api = stubApi({
      preview: () => json(++previews === 1 ? preview("tok-1", "80.00") : preview("tok-2", "130.00")),
      post: { "/billing/documents": () => json({ error: "document_preview_stale", message: "The account has changed since this preview was made. Preview the document again.", fieldErrors: {} }, 409) },
    });
    await openPanel(true);
    await userEvent.click(screen.getByRole("button", { name: "Preview a statement" }));
    await userEvent.click(await screen.findByRole("button", { name: "Issue this statement" }));
    expect(await screen.findByText(/nothing was issued\. Review the new preview below/)).toBeInTheDocument();
    expect(within(await screen.findByRole("article", { name: "Statement" })).getByText("Balance owed: $130.00")).toBeInTheDocument();   // the new preview, not issued
    await userEvent.click(screen.getByRole("button", { name: "Issue this statement" }));
    expect(posts(api, "/billing/documents").map((c) => c.body!.previewToken)).toEqual(["tok-1", "tok-2"]);
    expect(posts(api, "/billing/documents")[1].body!.idempotencyKey).not.toBe(posts(api, "/billing/documents")[0].body!.idempotencyKey);   // another preview is another request
  });

  it("previews a receipt from a payment and does not show a document that can no longer be rebuilt as issued", async () => {
    stubApi({ preview: () => json(receiptPreview()), documents: [issuedDoc()], detail: () => json({ error: "document_content_changed", message: "x", fieldErrors: {} }, 409) });
    await openPanel(true);
    await userEvent.click(screen.getByRole("button", { name: "Receipt for payment 3" }));
    expect(await screen.findByRole("heading", { name: "Receipt for payment 3" })).toBeInTheDocument();
    expect(screen.getByText(/Amount received: \$90\.00/)).toBeInTheDocument();
    expect(screen.getByText(/Charge 1 PERIO-1 Periodontal maintenance: \$50\.00/)).toBeInTheDocument();

    await userEvent.click(await screen.findByRole("button", { name: "View statement 1" }));
    expect(await screen.findByText(/can no longer be shown exactly as it was issued, so it is not shown/)).toBeInTheDocument();
    expect(screen.queryByText(/Shown exactly as issued/)).not.toBeInTheDocument();
  });

  it("says there is nothing to put on a statement before anything is billed and issues nothing", async () => {
    const api = stubApi({ preview: () => json({ error: "nothing_owed", message: "x", fieldErrors: {} }, 409) });
    await openPanel(true);
    await userEvent.click(screen.getByRole("button", { name: "Preview a statement" }));
    expect(await screen.findByText(/Nothing has been billed yet/)).toBeInTheDocument();
    expect(posts(api, "/billing/documents")).toHaveLength(0);
  });
});

describe("the timeline and aging", () => {
  it("shows what is open by age and the timeline when asked for, naming the procedure behind a charge", async () => {
    stubApi();
    await openPanel(false);
    const table = await screen.findByRole("table", { name: "Open charges by age" });
    expect(within(table).getByText("$80.00")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Show the timeline" }));
    const list = await screen.findByRole("list", { name: "Account timeline" });
    expect(within(list).getByText(/Charge for PERIO-1 Periodontal maintenance/)).toBeInTheDocument();
    expect(within(list).getByText(/Completed procedure performed 2030-01-01/)).toBeInTheDocument();
    expect(within(list).getAllByRole("listitem")).toHaveLength(2);
    const links = within(list).getAllByRole("link", { name: "Clinical history" });
    expect(links).toHaveLength(1);                                                                    // only the charge that bills a completed procedure links back to its source
    expect(links[0]).toHaveAttribute("href", `/patients/${P}/history`);
  });

  it("says the timeline could not be loaded rather than showing an empty history, and leaves aging out when it cannot be read", async () => {
    stubApi({ timeline: () => json({ error: "server_error", message: "boom" }, 500), aging: () => json({ error: "server_error", message: "boom" }, 500) });
    await openPanel(false);
    expect(screen.queryByRole("table", { name: "Open charges by age" })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Show the timeline" }));
    expect(await screen.findByText("Could not load the timeline.")).toBeInTheDocument();
    expect(screen.queryByRole("list", { name: "Account timeline" })).not.toBeInTheDocument();
  });
});

describe("accessibility", () => {
  it("has no violations with the refund form and a previewed statement open", async () => {
    stubApi();
    const { container } = render(<BillingPanel patient={patient} canWrite />);
    await screen.findByRole("heading", { name: "Account history" });
    await userEvent.click(screen.getByRole("button", { name: "Refund payment 3" }));
    await userEvent.click(screen.getByRole("button", { name: "Preview a statement" }));
    await screen.findByText("Preview: not issued yet.");
    expect(await axe(container)).toHaveNoViolations();
  });
});
