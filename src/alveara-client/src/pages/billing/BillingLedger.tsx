import { formatMoney } from "../../services/billingApi";
import type { BillingEntry } from "../../services/billingApi";

/** What an entry is about, in words a billing person would use. */
function describe(e: BillingEntry): string {
  if (e.kind === "Charge") return `${e.procedureCode ?? ""} ${e.procedureDescription ?? ""}`.trim();
  if (e.kind === "Payment") return [e.paymentMethod, e.reason].filter(Boolean).join(" - ");
  return e.reason ?? "";
}

/**
 * STORY-007: the account's history, oldest first, with the balance owed after each entry. Every number is the server's (the amount as stored, the balance after it); nothing is added up here. Entries are
 * only ever added: there is no edit or delete, so a correction shows as a new adjustment that says why.
 */
export function BillingLedger({ entries }: { entries: BillingEntry[] }) {
  return (
    <table className="alv-bill__table" aria-label="Account history">
      <thead>
        <tr><th scope="col">#</th><th scope="col">Date</th><th scope="col">Entry</th><th scope="col">Details</th><th scope="col" className="alv-bill__num">Amount</th><th scope="col" className="alv-bill__num">Balance owed after</th><th scope="col">Recorded by</th></tr>
      </thead>
      <tbody>
        {entries.map((e) => (
          <tr key={e.entryNumber}>
            <td>{e.entryNumber}</td>
            <td>{new Date(e.recordedAtUtc).toLocaleDateString()}</td>
            <td>{e.kind}</td>
            <td>{describe(e)}</td>
            <td className="alv-bill__num">{formatMoney(e.amount)}</td>
            <td className="alv-bill__num">{formatMoney(e.balanceAfter)}</td>
            <td>{e.recordedByName ?? "Unknown"}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
