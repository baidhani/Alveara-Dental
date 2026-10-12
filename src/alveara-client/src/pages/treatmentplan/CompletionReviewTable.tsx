import { chartEffectWords, notReadyWords } from "../../services/completionApi";
import type { ItemReadiness } from "../../services/completionApi";
import { money } from "./completionReviewRules";

interface Props {
  items: ItemReadiness[];
  selected: ReadonlySet<string>;
  onToggle: (itemId: string) => void;
  /** A message the server gave about one procedure (by item number), shown beside it. */
  notes: Record<number, string>;
}

const chargeWords = (i: ItemReadiness): string =>
  !i.billable ? "No charge (no fee)" : i.chargeExists ? `Charge already exists (${money(i.fee)})` : `Charge ${money(i.fee)}`;

/**
 * ALV-008-C01: every procedure of the plan that has not been withdrawn, with what completing it would do. A procedure that cannot be completed says why and cannot be picked; what each one will do to the chart
 * and the ledger is the server's answer (the readiness), shown in words. Nothing here decides who can be completed.
 */
export function CompletionReviewTable({ items, selected, onToggle, notes }: Props) {
  return (
    <table className="alv-tp__review" aria-label="Procedures to complete">
      <thead>
        <tr><th scope="col">Complete</th><th scope="col">Procedure</th><th scope="col">Tooth and surface</th><th scope="col">Chart</th><th scope="col">Charge</th></tr>
      </thead>
      <tbody>
        {items.map((i) => (
          <tr key={i.itemId}>
            <td>
              <input type="checkbox" aria-label={`Complete procedure ${i.itemNumber} ${i.procedureCode}`} checked={selected.has(i.itemId)} disabled={!i.ready} onChange={() => onToggle(i.itemId)} />
            </td>
            <th scope="row">
              <strong>{i.procedureCode}</strong> {i.procedureDescription}
              {!i.ready && <span className="alv-tp__error"> {notReadyWords(i.notReadyReason)}</span>}
              {notes[i.itemNumber] && <span className="alv-tp__error"> {notes[i.itemNumber]}</span>}
            </th>
            <td>{i.toothKey ? `Tooth ${i.toothKey}${i.surface ? `, ${i.surface}` : ""}` : "Whole mouth or arch"}</td>
            <td>{i.ready || i.chartEffect === "Ambiguous" ? chartEffectWords(i.chartEffect, i.chartMatches) : "-"}</td>
            <td>{i.ready ? chargeWords(i) : "-"}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
