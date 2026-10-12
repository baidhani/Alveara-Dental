import type { DocumentPractice, ReceiptContent, StatementContent } from "../../services/billingStatementsApi";

const day = (iso: string) => new Date(iso).toLocaleDateString();
const money = (text: string) => (text.startsWith("-") ? `-$${text.slice(1)}` : `$${text}`);

/** The practice as issued (name, address, phone), the first thing on a printed document. */
function PracticeHeader({ practice }: { practice?: DocumentPractice | null }) {
  if (!practice) return null;
  return <p className="alv-bill__practice"><strong>{practice.name}</strong>{practice.addressLine ? <><br />{practice.addressLine}</> : null}{practice.phone ? <><br />{practice.phone}</> : null}</p>;
}

/**
 * ALV-007-C02: a statement as issued. Every figure is the text the server built (two places, in dollars); nothing is added up here, so what prints is what the server's content says and what its hash covers.
 * The layout is plain and prints on its own (see Billing.css): the addressee, the cut-off instant, the ledger lines with their running balance, what is open by age and what is unapplied.
 */
export function StatementView({ statement }: { statement: StatementContent }) {
  return (
    <article className="alv-bill__document" aria-label="Statement">
      <PracticeHeader practice={statement.practice} />
      <h4>Statement of account</h4>
      <p>For: {statement.patient.name}<br />Addressed to: {statement.addressee.name}{statement.addressee.isSelf ? "" : " (responsible for this account)"}<br />Up to: {new Date(statement.asOfUtc).toLocaleString()}</p>
      <table className="alv-bill__table" aria-label="Statement lines">
        <thead><tr><th scope="col">#</th><th scope="col">Date</th><th scope="col">Entry</th><th scope="col">Details</th><th scope="col" className="alv-bill__num">Amount</th><th scope="col" className="alv-bill__num">Balance owed after</th></tr></thead>
        <tbody>
          {statement.lines.map((l) => (
            <tr key={l.number}>
              <td>{l.number}</td><td>{day(l.recordedAtUtc)}</td><td>{l.kind}</td>
              <td>{[l.procedureCode, l.procedureDescription, l.method, l.reason].filter(Boolean).join(" - ")}</td>
              <td className="alv-bill__num">{money(l.amount)}</td><td className="alv-bill__num">{money(l.balanceAfter)}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <p><strong>Balance owed: {money(statement.balance)}</strong></p>
      {statement.openCharges.length > 0 && (
        <table className="alv-bill__table" aria-label="Open charges by age">
          <thead><tr><th scope="col">Charge</th><th scope="col">Performed</th><th scope="col">Age (days)</th><th scope="col">Group</th><th scope="col" className="alv-bill__num">Open</th></tr></thead>
          <tbody>
            {statement.openCharges.map((c) => (
              <tr key={c.number}><td>{c.number} {c.procedureCode} {c.procedureDescription}</td><td>{day(c.serviceDateUtc)}</td><td>{c.ageDays}</td><td>{c.bucket}</td><td className="alv-bill__num">{money(c.open)}</td></tr>
            ))}
          </tbody>
        </table>
      )}
      <p>By age: 0-30 days {money(statement.aging.current)}; 31-60 days {money(statement.aging.days31To60)}; 61-90 days {money(statement.aging.days61To90)}; over 90 days {money(statement.aging.over90)}.</p>
      {statement.unapplied.length > 0 && <p>Payments not yet applied to a charge: {statement.unapplied.map((u) => `payment ${u.paymentNumber} ${money(u.amount)}`).join("; ")}.</p>}
    </article>
  );
}

/** ALV-007-C02: a receipt as issued: what was received, how, from whom, what it paid and what was left unapplied, with the balance owed at that moment. */
export function ReceiptView({ receipt }: { receipt: ReceiptContent }) {
  return (
    <article className="alv-bill__document" aria-label="Receipt">
      <PracticeHeader practice={receipt.practice} />
      <h4>Receipt for payment {receipt.paymentNumber}</h4>
      <p>For: {receipt.patient.name}<br />Received from: {receipt.receivedFrom.name}<br />Received: {day(receipt.receivedAtUtc)}{receipt.method ? ` by ${receipt.method}` : ""}<br />Up to: {new Date(receipt.asOfUtc).toLocaleString()}</p>
      <p><strong>Amount received: {money(receipt.amount)}</strong></p>
      {receipt.applications.length > 0 && (
        <ul aria-label="What this payment paid">
          {receipt.applications.map((a) => <li key={a.chargeNumber}>Charge {a.chargeNumber} {a.procedureCode} {a.procedureDescription}: {money(a.amount)}</li>)}
        </ul>
      )}
      <p>Not applied to a charge: {money(receipt.unapplied)}. Refunded so far: {money(receipt.refundedToDate)}. Balance owed: {money(receipt.balanceOwed)}.</p>
    </article>
  );
}
