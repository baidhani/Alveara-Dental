import { formatMoney } from "../../services/billingApi";
import type { GuarantorSummary as Summary } from "../../services/billingApi";

/**
 * ALV-007-C01: when this patient is financially responsible for other people's accounts, what is owed on each and in total. Every figure is that account's own ledger balance from the server. Shown only when there is
 * something beyond the patient's own account to show.
 */
export function GuarantorSummary({ summary }: { summary: Summary }) {
  if (!summary.accounts.some((a) => !a.isGuarantor)) return null;
  return (
    <section aria-labelledby="alv-bill-guarantor-title">
      <h3 id="alv-bill-guarantor-title">Accounts this patient is responsible for</h3>
      <table className="alv-bill__table" aria-label="Accounts this patient is responsible for">
        <thead><tr><th scope="col">Patient</th><th scope="col" className="alv-bill__num">Balance owed</th></tr></thead>
        <tbody>
          {summary.accounts.map((a) => <tr key={a.patientId}><td>{a.isGuarantor ? `${a.patientName} (this patient)` : a.patientName}</td><td className="alv-bill__num">{formatMoney(a.balanceOwed)}</td></tr>)}
        </tbody>
        <tfoot><tr><th scope="row">Total owed</th><td className="alv-bill__num">{formatMoney(summary.totalOwed)}</td></tr></tfoot>
      </table>
    </section>
  );
}
