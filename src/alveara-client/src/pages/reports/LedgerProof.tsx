import type { LedgerProof as Proof } from "../../services/reportsApi";
import { NumberButton } from "./NumberButton";
import { LINE_LABELS, money } from "./reportFormat";

/**
 * How the ledger adds up. Every ledger entry of the period is in exactly one line, and the lines are shown with what each does to what patients owe (a charge adds to it, a payment takes from it). The lines are then
 * checked against an independent sum of the same entries, in amount and in number; the sentence says whether they agree, and a disagreement is shown as a warning. A loaded opening balance is its own line: it is neither
 * production nor collections. The lines this report covers open their entries; the others are shown so that nothing is hidden.
 */
export function LedgerProof({ proof, opens, onOpen }: { proof: Proof; opens: string[]; onOpen: (line: string) => void }) {
  const lines = proof.lines.filter((l) => l.line !== "Unclassified" || l.entries > 0);
  return (
    <section aria-label="How the ledger adds up">
      <h2>How the ledger adds up</h2>
      {proof.scope === "ProviderProduction" && <p className="alv-rep__hint">Only the production of the providers chosen is covered here, so only the charge lines are shown.</p>}
      <table className="alv-rep__table">
        <caption className="alv-rep__sr">Ledger entries of the period by line, with their effect on what patients owe</caption>
        <thead><tr><th scope="col">Line</th><th scope="col">Entries</th><th scope="col">Effect on what is owed</th></tr></thead>
        <tbody>
          {lines.map((l) => (
            <tr key={l.line}>
              <th scope="row">{LINE_LABELS[l.line] ?? l.line}</th>
              <td>{opens.includes(l.line) ? <NumberButton value={l.entries} label={LINE_LABELS[l.line] ?? l.line} onOpen={() => onOpen(l.line)} /> : l.entries.toLocaleString()}</td>
              <td>{money(l.amount)}</td>
            </tr>
          ))}
          <tr><th scope="row">All lines</th><td>{proof.linesEntries.toLocaleString()}</td><td>{money(proof.linesTotal)}</td></tr>
        </tbody>
      </table>
      {proof.reconciled
        ? <p className="alv-rep__hint">The lines add up to the ledger: {money(proof.ledgerTotal)} across {proof.ledgerEntries.toLocaleString()} {proof.ledgerEntries === 1 ? "entry" : "entries"}, checked against an independent sum of the same entries.</p>
        : <p role="alert" className="alv-rep__problem">The lines do not agree with the ledger ({money(proof.linesTotal)} in {proof.linesEntries} entries against {money(proof.ledgerTotal)} in {proof.ledgerEntries}). Do not rely on these numbers; tell your administrator.</p>}
    </section>
  );
}
