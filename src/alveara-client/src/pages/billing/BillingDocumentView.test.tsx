import { describe, it, expect, afterEach } from "vitest";
import { render, cleanup } from "@testing-library/react";
import { ReceiptView, StatementView } from "./BillingDocumentView";
import type { ReceiptContent, StatementContent } from "../../services/billingStatementsApi";
import { setRenderZone } from "../../time/practiceTime";

// ALV-N021 A3-R02 (review CR-2): the statement and receipt views show their instants in the PRACTICE zone, whatever the browser's zone, and nothing else in the
// document changes with the zone. The instants below fall on different calendar days in Chicago and in UTC/Tokyo:
//   2030-01-15T03:30:00Z = 1/14/2030 9:30 PM in Chicago, 1/15/2030 in UTC and Tokyo.

const AS_OF = "2030-01-15T03:30:00Z";
const LINE_AT = "2030-01-15T04:00:00Z";       // 1/14 in Chicago
const SERVICE_AT = "2030-01-15T05:00:00Z";    // 1/14 in Chicago
const RECEIVED_AT = "2030-01-15T02:00:00Z";   // 1/14 in Chicago

const party = (name: string) => ({ patientId: "p1", name, isSelf: true });
const statement: StatementContent = {
  documentType: "Statement", templateVersion: 2, asOfUtc: AS_OF, currency: "USD", patient: party("Ann Lee"), addressee: party("Ann Lee"),
  lines: [{ number: 7, kind: "Charge", amount: "120.00", balanceAfter: "120.00", procedureCode: "D1110", procedureDescription: "Prophylaxis", method: null, reason: null, recordedAtUtc: LINE_AT, reversesNumber: null, refundsNumber: null, replacesNumber: null }],
  openCharges: [{ number: 7, procedureCode: "D1110", procedureDescription: "Prophylaxis", amount: "120.00", paid: "0.00", open: "120.00", serviceDateUtc: SERVICE_AT, ageDays: 0, bucket: "0-30" }],
  unapplied: [], aging: { current: "120.00", days31To60: "0.00", days61To90: "0.00", over90: "0.00" }, balance: "120.00", openChargeTotal: "120.00", unappliedTotal: "0.00",
  practice: { name: "DentalKeep Practice", phone: "555-0100", addressLine: "1 Main St" },
};
const receipt: ReceiptContent = {
  documentType: "Receipt", templateVersion: 1, asOfUtc: AS_OF, currency: "USD", patient: party("Ann Lee"), receivedFrom: party("Ann Lee"), paymentNumber: 9, amount: "50.00", method: "Card",
  receivedAtUtc: RECEIVED_AT, applications: [{ chargeNumber: 7, procedureCode: "D1110", procedureDescription: "Prophylaxis", amount: "50.00" }], unapplied: "0.00", refundedToDate: "0.00", balanceOwed: "70.00",
  practice: { name: "DentalKeep Practice", phone: "555-0100", addressLine: "1 Main St" },
};

const plain = (s: string | null | undefined) => (s ?? "").replace(/ /g, " ");
const savedTz = process.env.TZ;

function renderIn(practiceZone: string, browserZone: string, view: "statement" | "receipt") {
  process.env.TZ = browserZone;   // the "browser" zone (Node honours a runtime TZ change); it must not decide anything
  setRenderZone(practiceZone);    // the session's practice zone, as the AuthProvider sets it
  const { container } = render(view === "statement" ? <StatementView statement={statement} /> : <ReceiptView receipt={receipt} />);
  const text = plain(container.textContent);
  cleanup();
  return text;
}

/** The document text with every rendered date/time removed, to show the zone changes nothing else. */
const withoutDates = (text: string) => text.replace(/\d{1,2}\/\d{1,2}\/\d{4}(, \d{1,2}:\d{2}:\d{2} [AP]M)?/g, "<time>");

afterEach(() => {
  if (savedTz === undefined) delete process.env.TZ; else process.env.TZ = savedTz;
  setRenderZone("America/Chicago");
});

describe("statement view (ALV-N021 A3-R02, CR-2)", () => {
  it("shows the cut-off, the line date and the service date in the practice zone while the browser is in Tokyo", () => {
    const text = renderIn("America/Chicago", "Asia/Tokyo", "statement");
    expect(text).toContain("Up to: 1/14/2030, 9:30:00 PM");
    expect(text).toMatch(/7\s*1\/14\/2030\s*Charge/);                 // the statement line's date
    expect(text).toContain("7 D1110 Prophylaxis1/14/2030");           // the open charge's service date
    expect(text).not.toContain("1/15/2030");                          // nothing in the browser's (or UTC) day
  });

  it("follows the practice zone, not the browser, and changes nothing but the times", () => {
    const chicago = renderIn("America/Chicago", "Asia/Tokyo", "statement");
    const tokyo = renderIn("Asia/Tokyo", "America/Chicago", "statement");
    expect(tokyo).toContain("Up to: 1/15/2030, 12:30:00 PM");
    expect(chicago).not.toEqual(tokyo);
    expect(withoutDates(chicago)).toEqual(withoutDates(tokyo));       // names, numbers, amounts, wording identical
  });
});

describe("receipt view (ALV-N021 A3-R02, CR-2)", () => {
  it("shows the received date and the cut-off in the practice zone while the browser is in Tokyo, and changes nothing but the times", () => {
    const chicago = renderIn("America/Chicago", "Asia/Tokyo", "receipt");
    expect(chicago).toContain("Received: 1/14/2030 by Card");
    expect(chicago).toContain("Up to: 1/14/2030, 9:30:00 PM");
    const tokyo = renderIn("Asia/Tokyo", "America/Chicago", "receipt");
    expect(tokyo).toContain("Received: 1/15/2030 by Card");
    expect(withoutDates(chicago)).toEqual(withoutDates(tokyo));
  });
});
