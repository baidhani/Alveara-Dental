import { useCallback, useEffect, useState } from "react";
import { ConcurrencyConflictBanner } from "../../components/ConcurrencyConflictBanner";
import { ErrorState, LoadingState } from "../../components/StatePatterns";
import { formatMoney, getBillingAccount, listBillableProcedures } from "../../services/billingApi";
import type { BillableProcedure, BillingAccount } from "../../services/billingApi";
import type { PatientDetail } from "../../services/patientsApi";
import { BillableProcedures } from "./BillableProcedures";
import { BillingAdjustmentForm } from "./BillingAdjustmentForm";
import { BillingLedger } from "./BillingLedger";
import { BillingPaymentForm } from "./BillingPaymentForm";
import "../diagnosis/Diagnosis.css";
import "./Billing.css";

interface Props {
  patient: PatientDetail;
  /** Whether the signed-in person may bill, take payments and adjust. It only decides which controls are drawn (the server checks every call). */
  canWrite?: boolean;
}

/**
 * STORY-007: a patient's billing account. It shows the balance owed (always the server's figure) and the history of every charge, payment and adjustment, oldest first. People who may change it bill a
 * completed procedure at the fee its treatment plan copied, record payments, and correct mistakes with an adjustment that says why; everyone who can see billing reads the account exactly as recorded.
 * Nothing recorded is ever edited or deleted. A failed load says it could not load rather than showing a zero balance.
 */
export function BillingPanel({ patient, canWrite = false }: Props) {
  const [account, setAccount] = useState<BillingAccount | null>(null);
  const [ready, setReady] = useState<BillableProcedure[] | null>(null);
  const [failed, setFailed] = useState(false);
  const [readyFailed, setReadyFailed] = useState(false);
  const [status, setStatus] = useState<string | null>(null);
  const [conflict, setConflict] = useState(false);

  const load = useCallback((signal?: AbortSignal) => {
    getBillingAccount(patient.id, signal).then((a) => { setAccount(a); setFailed(false); }).catch(() => { if (!signal?.aborted) setFailed(true); });
    listBillableProcedures(patient.id, signal).then((r) => { setReady(r); setReadyFailed(false); }).catch(() => { if (!signal?.aborted) { setReadyFailed(true); setReady((cur) => cur ?? []); } });
  }, [patient.id]);

  useEffect(() => {
    const controller = new AbortController();
    load(controller.signal);
    return () => controller.abort();
  }, [load]);

  function changed(updated: BillingAccount, text: string) {
    setAccount(updated);
    setConflict(false);
    setStatus(text);
    listBillableProcedures(patient.id).then((r) => { setReady(r); setReadyFailed(false); }).catch(() => setReadyFailed(true));
  }

  if (failed && account === null) return <ErrorState title="Could not load the billing account" description="Do not assume nothing is owed. Check your connection and reload the page." />;
  if (account === null || ready === null) return <LoadingState label="Loading the billing account…" />;

  return (
    <section className="alv-clinical alv-dx alv-bill" aria-labelledby="alv-bill-title">
      <h2 id="alv-bill-title" className="alv-workspace__section-title">Billing</h2>
      <p className="alv-clinical__meta">
        Charges, payments and adjustments for this patient. Nothing recorded is ever edited or deleted: a mistake is corrected with an adjustment that says why. The balance owed is calculated from the history every time.
      </p>
      <p className="alv-clinical__status" role="status" aria-live="polite">{status ?? (canWrite ? "Nothing has been recorded in this visit." : "You can read this but your role cannot change it.")}</p>
      {conflict && <ConcurrencyConflictBanner problem={{ error: "concurrency_conflict", entityType: "billing account", entityId: patient.id }} onReload={() => { setConflict(false); setStatus("Reloaded."); load(); }} />}
      {failed && <p className="alv-clinical__meta" role="alert">Could not refresh the account. What is shown may be out of date.</p>}
      {readyFailed && <p className="alv-clinical__meta" role="alert">Could not load the procedures ready to bill. The list shown may be out of date.</p>}

      <p className="alv-bill__balance"><strong>Balance owed: {formatMoney(account.balanceOwed)}</strong></p>

      <BillableProcedures patientId={patient.id} procedures={ready} canWrite={canWrite} onBilled={changed} />

      <h3>Account history</h3>
      {account.entries.length === 0
        ? <p className="alv-clinical__meta">Nothing has been billed to this patient yet. That means nothing is recorded here, not that nothing is owed elsewhere.</p>
        : <BillingLedger entries={account.entries} />}

      {canWrite && account.hasAccount && account.balanceOwed > 0 && <BillingPaymentForm account={account} onDone={changed} onConflict={() => setConflict(true)} />}
      {canWrite && account.hasAccount && <BillingAdjustmentForm account={account} onDone={changed} onConflict={() => setConflict(true)} />}
    </section>
  );
}
