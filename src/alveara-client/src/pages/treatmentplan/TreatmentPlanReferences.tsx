import { useId, useState } from "react";
import { ApiError } from "../../services/authApi";
import { isNetworkFailure } from "../../services/clinicalApi";
import { planFieldErrorsOf, reconcileReferences, resolvePlanReference } from "../../services/treatmentPlansApi";
import type { ReconciliationItem, ReconciliationResult, TreatmentPlan } from "../../services/treatmentPlansApi";
import { outcomeWords } from "./treatmentPlanRules";

function ChoosePlan({ item, plans, onResolved }: { item: ReconciliationItem; plans: TreatmentPlan[]; onResolved: (i: ReconciliationItem) => void }) {
  const [planId, setPlanId] = useState("");
  const [note, setNote] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const id = useId();

  async function tie() {
    if (planId === "") return setNote("Not tied: choose the plan this reference belongs to.");
    setBusy(true);
    setNote(null);
    try {
      onResolved(await resolvePlanReference(item.diagnosisId, planId));
    } catch (err) {
      const fields = Object.values(planFieldErrorsOf(err));
      if (fields.length > 0) setNote(`Not tied: ${fields.join(" ")}`);
      else if (isNetworkFailure(err)) setNote("Not tied: the connection dropped. Try again.");
      else setNote(`Not tied: ${err instanceof ApiError ? err.message : "something went wrong."}`);
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="alv-dx__form" aria-label={`Choose the plan for ${item.diagnosisLabel}`} onSubmit={(e) => { e.preventDefault(); void tie(); }}>
      {note && <p className="alv-dx__message" role="alert">{note}</p>}
      <label htmlFor={id}>Plan for &ldquo;{item.reference}&rdquo;
        <select id={id} value={planId} onChange={(e) => { setPlanId(e.target.value); setNote(null); }}>
          <option value="">Choose a plan</option>
          {plans.map((p) => <option key={p.id} value={p.id}>{p.title}{p.referenceCode ? ` (${p.referenceCode})` : ""}{p.status === "Withdrawn" ? " - withdrawn" : ""}</option>)}
        </select>
      </label>
      <div className="alv-clinical__row-actions"><button type="submit" className="btn btn-outline-primary btn-sm" disabled={busy}>Tie to this plan</button></div>
    </form>
  );
}

/**
 * ALV-015-C01: the old free-text treatment-plan references on this patient's diagnoses, from before plans existed. Reviewing is read-only: it shows which references match one of this patient's plans (by the plan's reference
 * code or id) and which cannot be tied safely, and why. Tying them records the link beside the diagnosis and keeps its original text and history; it is final, and a reference with no safe match stays unresolved until a person
 * chooses one of this patient's plans for it. Nothing is guessed, and another patient's plan is never offered.
 */
export function TreatmentPlanReferences({ patientId, plans }: { patientId: string; plans: TreatmentPlan[] }) {
  const [result, setResult] = useState<ReconciliationResult | null>(null);
  const [open, setOpen] = useState(false);
  const [status, setStatus] = useState<string | null>(null);
  const [note, setNote] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function run(apply: boolean) {
    setBusy(true);
    setNote(null);
    try {
      const r = await reconcileReferences(patientId, apply);
      setResult(r);
      setStatus(apply ? (r.matched === 0 ? "Nothing needed tying." : `${r.matched} ${r.matched === 1 ? "reference" : "references"} tied to plans. Each diagnosis keeps its original text.`) : null);
    } catch (err) {
      setNote(isNetworkFailure(err) ? "Could not check the references: the connection dropped. Try again." : `${apply ? "Nothing was tied" : "Could not check the references"}: ${err instanceof ApiError ? err.message : "something went wrong."}`);
    } finally {
      setBusy(false);
    }
  }

  const replace = (item: ReconciliationItem) => setResult((r) => (r ? { ...r, items: r.items.map((x) => (x.diagnosisId === item.diagnosisId ? item : x)) } : r));
  const needsChoice = (i: ReconciliationItem) => i.outcome === "NoMatch" || i.outcome === "Ambiguous";

  return (
    <section className="alv-tp__references" aria-label="Old diagnosis references">
      <h3>Old diagnosis references</h3>
      <p className="alv-clinical__meta">A diagnosis recorded before treatment plans existed may carry a free-text reference such as &quot;plan-2026-07&quot;. Reviewing shows which of them match one of this patient&apos;s plans. Nothing is changed until you tie them.</p>
      {!open && <button type="button" className="btn btn-outline-secondary btn-sm" disabled={busy} onClick={() => { setOpen(true); void run(false); }}>Review old diagnosis references</button>}
      {note && <p className="alv-dx__message" role="alert">{note}</p>}
      <p className="alv-clinical__meta" role="status" aria-live="polite">{status}</p>
      {open && result && (
        <>
          {result.items.length === 0
            ? <p className="alv-clinical__meta">No diagnosis of this patient has an old treatment-plan reference.</p>
            : <ul className="alv-dx__list" aria-label="Old diagnosis references">
              {result.items.map((i) => (
                <li key={i.diagnosisId} className="alv-dx__item">
                  <p className="alv-dx__label">{i.diagnosisLabel}</p>
                  <p className="alv-clinical__meta">Reference: <strong>{i.reference}</strong></p>
                  <p>{outcomeWords(i)}</p>
                  {needsChoice(i) && plans.length > 0 && <ChoosePlan item={i} plans={plans} onResolved={replace} />}
                </li>
              ))}
            </ul>}
          {!result.applied && result.matched > 0 && (
            <div className="alv-clinical__row-actions">
              <button type="button" className="btn btn-primary" disabled={busy} onClick={() => void run(true)}>Tie {result.matched} matching {result.matched === 1 ? "reference" : "references"} to {result.matched === 1 ? "its plan" : "their plans"}</button>
            </div>
          )}
        </>
      )}
    </section>
  );
}
