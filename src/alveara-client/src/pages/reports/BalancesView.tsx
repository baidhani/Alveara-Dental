import { useState } from "react";
import { getBalances } from "../../services/reportsApi";
import type { BalanceSnapshot, ReportQuery } from "../../services/reportsApi";
import { BalanceAccountRecords } from "./MoreRecords";
import { NumberButton } from "./NumberButton";
import { ReportFrame } from "./ReportFrame";
import { money, queryKey } from "./reportFormat";
import { useReport } from "./useReport";

type Spec = { title: string; side: string };

/**
 * The balances report: what every account owed at the START of the first day and at the END of the last day, each balance being the plain sum of the account's ledger entries up to then (never a stored figure). The
 * change between the two is checked against an independent sum of the entries recorded in the period. The accounts at the end open as a list, largest first. The ledger refuses an entry that would leave an account
 * below zero, so no account is in credit; the credit row is shown anyway so that a broken rule could not hide.
 */
export function BalancesView({ query }: { query: ReportQuery }) {
  const { outcome, again } = useReport((signal) => getBalances(query, signal), queryKey(query));
  const [spec, setSpec] = useState<Spec | null>(null);

  return (
    <ReportFrame outcome={outcome} onRetry={again} emptyText="No account holds any ledger entry yet.">
      {(report) => {
        const d = report.data;
        const row = (label: string, pick: (s: BalanceSnapshot) => React.ReactNode, key: string) => <tr key={key}><th scope="row">{label}</th><td>{pick(d.atStart)}</td><td>{pick(d.atEnd)}</td></tr>;
        return (
          <>
            <ul className="alv-rep__cards" aria-label="Summary">
              <li><span>Owed at the end</span><strong>{money(d.atEnd.totalOwed)}</strong><small>{d.atEnd.accountsOwing.toLocaleString()} {d.atEnd.accountsOwing === 1 ? "account" : "accounts"}</small></li>
              <li><span>Owed at the start</span><strong>{money(d.atStart.totalOwed)}</strong><small>{d.atStart.accountsOwing.toLocaleString()} {d.atStart.accountsOwing === 1 ? "account" : "accounts"}</small></li>
              <li><span>Change over the period</span><strong>{money(d.change)}</strong></li>
            </ul>

            <table className="alv-rep__table">
              <caption className="alv-rep__sr">Balances at the start and at the end of the period</caption>
              <thead><tr><th scope="col">What patients owed</th><th scope="col">At the start</th><th scope="col">At the end</th></tr></thead>
              <tbody>
                {row("Accounts with any entry", (s) => s.accounts.toLocaleString(), "accounts")}
                {row("Accounts that owe", (s) => (s === d.atEnd
                  ? <NumberButton value={s.accountsOwing} label="accounts that owe at the end" onOpen={() => setSpec({ title: "Accounts that owe at the end", side: "Owing" })} />
                  : s.accountsOwing.toLocaleString()), "owing")}
                {row("Total owed", (s) => money(s.totalOwed), "owed")}
                {row("Accounts in credit", (s) => (s === d.atEnd
                  ? <NumberButton value={s.accountsInCredit} label="accounts in credit at the end" onOpen={() => setSpec({ title: "Accounts in credit at the end", side: "Credit" })} />
                  : s.accountsInCredit.toLocaleString()), "credit")}
                {row("Total in credit", (s) => money(s.totalCredit), "credit-total")}
                {row("Net (owed less credit)", (s) => money(s.netBalance), "net")}
              </tbody>
            </table>

            {d.reconciled
              ? <p className="alv-rep__hint">The change ({money(d.change)}) equals an independent sum of the {d.ledgerEntries.toLocaleString()} {d.ledgerEntries === 1 ? "entry" : "entries"} recorded in the period ({money(d.ledgerChange)}).</p>
              : <p role="alert" className="alv-rep__problem">The change ({money(d.change)}) does not equal the entries recorded in the period ({money(d.ledgerChange)}). Do not rely on these numbers; tell your administrator.</p>}
            <p className="alv-rep__hint">No account can be in credit while the ledger refuses a negative balance; the rows are shown so that a broken rule could not hide.</p>
            {spec && <BalanceAccountRecords key={spec.side} query={query} side={spec.side} title={spec.title} onClose={() => setSpec(null)} />}
          </>
        );
      }}
    </ReportFrame>
  );
}
