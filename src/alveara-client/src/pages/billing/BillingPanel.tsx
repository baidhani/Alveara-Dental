import { useCallback, useEffect, useState } from "react";
import { ConcurrencyConflictBanner } from "../../components/ConcurrencyConflictBanner";
import { ErrorState, LoadingState } from "../../components/StatePatterns";
import {
  formatMoney, getBillingAccount, getGuarantorSummary, getResponsibility, isGuarantorSummary, isPaymentMethodList, isResponsibility, listBillableProcedures, listPaymentMethods,
} from "../../services/billingApi";
import type { BillableProcedure, BillingAccount, BillingEntry, GuarantorSummary as Summary, PaymentMethod, Responsibility } from "../../services/billingApi";
import type { PatientDetail } from "../../services/patientsApi";
import { BillableProcedures } from "./BillableProcedures";
import { BillingAdjustmentForm } from "./BillingAdjustmentForm";
import { BillingApplyForm } from "./BillingApplyForm";
import { BillingLedger } from "./BillingLedger";
import { BillingPaymentForm } from "./BillingPaymentForm";
import { BillingResponsibilityPanel } from "./BillingResponsibilityPanel";
import { BillingReverseForm } from "./BillingReverseForm";
import { GuarantorSummary } from "./GuarantorSummary";
import { PaymentMethodsSettings } from "./PaymentMethodsSettings";
import "../diagnosis/Diagnosis.css";
import "./Billing.css";

interface Props {
  patient: PatientDetail;
  /** Whether the signed-in person may bill, take payments, adjust, reverse and configure. It only decides which controls are drawn (the server checks every call). */
  canWrite?: boolean;
}

/**
 * STORY-007 and ALV-007-C01: a patient's billing account. It shows the balance owed (always the server's figure) and the history of every charge, payment, adjustment and reversal, oldest first, with what each now
 * stands for (reversed, paid, open, applied, unapplied), who is responsible for the account and, for a guarantor, what they answer for. People who may change it bill a completed procedure at the fee its treatment
 * plan copied, record payments (applied to the oldest charges or to the ones chosen) under the practice's payment methods, apply what a payment still holds, correct mistakes with an adjustment or undo an entry with a
 * reversal that says why; everyone who can see billing reads the account exactly as recorded. Nothing recorded is ever edited or deleted. A failed load says it could not load rather than showing a zero balance; the
 * secondary panels (responsibility, guarantor summary, payment methods) are left out when they cannot be read, never drawn from a wrong answer.
 */
export function BillingPanel({ patient, canWrite = false }: Props) {
  const [account, setAccount] = useState<BillingAccount | null>(null);
  const [ready, setReady] = useState<BillableProcedure[] | null>(null);
  const [responsibility, setResponsibility] = useState<Responsibility | null>(null);
  const [summary, setSummary] = useState<Summary | null>(null);
  const [methods, setMethods] = useState<PaymentMethod[] | null>(null);
  const [failed, setFailed] = useState(false);
  const [readyFailed, setReadyFailed] = useState(false);
  const [status, setStatus] = useState<string | null>(null);
  const [conflict, setConflict] = useState(false);
  const [reversing, setReversing] = useState<number | null>(null);
  const [applying, setApplying] = useState<number | null>(null);

  const loadSecondary = useCallback((signal?: AbortSignal) => {
    getResponsibility(patient.id, signal).then((r) => setResponsibility(isResponsibility(r) ? r : null)).catch(() => undefined);
    getGuarantorSummary(patient.id, signal).then((s) => setSummary(isGuarantorSummary(s) ? s : null)).catch(() => undefined);
    if (canWrite) listPaymentMethods(true, signal).then((m) => setMethods(isPaymentMethodList(m) ? m : null)).catch(() => undefined);
  }, [patient.id, canWrite]);

  const load = useCallback((signal?: AbortSignal) => {
    getBillingAccount(patient.id, signal).then((a) => { setAccount(a); setFailed(false); }).catch(() => { if (!signal?.aborted) setFailed(true); });
    listBillableProcedures(patient.id, signal).then((r) => { setReady(r); setReadyFailed(false); }).catch(() => { if (!signal?.aborted) { setReadyFailed(true); setReady((cur) => cur ?? []); } });
    loadSecondary(signal);
  }, [patient.id, loadSecondary]);

  useEffect(() => {
    const controller = new AbortController();
    load(controller.signal);
    return () => controller.abort();
  }, [load]);

  function changed(updated: BillingAccount, text: string) {
    setAccount(updated);
    setConflict(false);
    setStatus(text);
    setReversing(null);
    setApplying(null);
    listBillableProcedures(patient.id).then((r) => { setReady(r); setReadyFailed(false); }).catch(() => setReadyFailed(true));
    loadSecondary();
  }

  function responsibilityChanged(updated: Responsibility, text: string) {
    setResponsibility(updated);
    setConflict(false);
    setStatus(text);
    getBillingAccount(patient.id).then(setAccount).catch(() => setFailed(true));        // recording who is responsible is a change to the account, so its version moved
    loadSecondary();
  }

  if (failed && account === null) return <ErrorState title="Could not load the billing account" description="Do not assume nothing is owed. Check your connection and reload the page." />;
  if (account === null || ready === null) return <LoadingState label="Loading the billing account…" />;

  const reversingEntry: BillingEntry | undefined = account.entries.find((e) => e.entryNumber === reversing);
  const activeMethods = methods?.filter((m) => m.isActive).map((m) => m.name);
  const payers = responsibility && !responsibility.isSelf ? [{ id: responsibility.responsiblePatientId, name: responsibility.responsibleName }] : [];

  return (
    <section className="alv-clinical alv-dx alv-bill" aria-labelledby="alv-bill-title">
      <h2 id="alv-bill-title" className="alv-workspace__section-title">Billing</h2>
      <p className="alv-clinical__meta">
        Charges, payments, adjustments and reversals for this patient. Nothing recorded is ever edited or deleted: a mistake is corrected with an adjustment or a reversal that says why. The balance owed is calculated from the history every time.
      </p>
      <p className="alv-clinical__status" role="status" aria-live="polite">{status ?? (canWrite ? "Nothing has been recorded in this visit." : "You can read this but your role cannot change it.")}</p>
      {conflict && <ConcurrencyConflictBanner problem={{ error: "concurrency_conflict", entityType: "billing account", entityId: patient.id }} onReload={() => { setConflict(false); setStatus("Reloaded."); load(); }} />}
      {failed && <p className="alv-clinical__meta" role="alert">Could not refresh the account. What is shown may be out of date.</p>}
      {readyFailed && <p className="alv-clinical__meta" role="alert">Could not load the procedures ready to bill. The list shown may be out of date.</p>}

      <p className="alv-bill__balance"><strong>Balance owed: {formatMoney(account.balanceOwed)}</strong></p>
      {account.openChargeTotal !== undefined && account.entries.length > 0 && (
        <p className="alv-clinical__meta">Still to pay on charges: {formatMoney(account.openChargeTotal)}. Payments not yet applied to a charge: {formatMoney(account.unappliedTotal ?? 0)}.</p>
      )}

      {responsibility && <BillingResponsibilityPanel patient={patient} responsibility={responsibility} accountVersion={account.rowVersion} canWrite={canWrite} accountExists={account.hasAccount} onChanged={responsibilityChanged} onConflict={() => setConflict(true)} />}
      {summary && <GuarantorSummary summary={summary} />}

      <BillableProcedures patientId={patient.id} procedures={ready} canWrite={canWrite} onBilled={changed} />

      <h3>Account history</h3>
      {account.entries.length === 0
        ? <p className="alv-clinical__meta">Nothing has been billed to this patient yet. That means nothing is recorded here, not that nothing is owed elsewhere.</p>
        : <BillingLedger account={account} onReverse={canWrite ? (e) => { setApplying(null); setReversing(e.entryNumber); } : undefined} onApply={canWrite ? (e) => { setReversing(null); setApplying(e.entryNumber); } : undefined} />}

      {canWrite && reversingEntry && <BillingReverseForm account={account} entry={reversingEntry} onDone={changed} onConflict={() => setConflict(true)} onCancel={() => setReversing(null)} />}
      {canWrite && applying !== null && <BillingApplyForm account={account} paymentEntryNumber={applying} onDone={changed} onConflict={() => setConflict(true)} onCancel={() => setApplying(null)} />}
      {canWrite && account.hasAccount && account.balanceOwed > 0 && <BillingPaymentForm account={account} methods={activeMethods} payers={payers} onDone={changed} onConflict={() => setConflict(true)} />}
      {canWrite && account.hasAccount && <BillingAdjustmentForm account={account} onDone={changed} onConflict={() => setConflict(true)} />}
      {canWrite && methods && <PaymentMethodsSettings methods={methods} onChanged={(m, text) => { setMethods(m); setStatus(text); }} />}
    </section>
  );
}
