import { useId, useRef, useState } from "react";
import { ApiError, isConcurrencyConflict } from "../../services/authApi";
import { isNetworkFailure } from "../../services/clinicalApi";
import { planFieldErrorsOf, revisePlan } from "../../services/treatmentPlansApi";
import type { TreatmentPlan } from "../../services/treatmentPlansApi";
import { TreatmentPlanPhaseEditor } from "./TreatmentPlanPhaseEditor";
import { TEXT_MAX, draftFromPlan, liveItems, removePhase, reviseGaps, toReviseInput } from "./treatmentPlanLayout";
import type { ReviseDraft } from "./treatmentPlanLayout";

interface Props {
  plan: TreatmentPlan;
  /** Called with the plan as the server now holds it, and the words to tell the person. */
  onDone: (plan: TreatmentPlan, text: string) => void;
  /** A stale revision: the panel shows the conflict banner and reloads. */
  onConflict: () => void;
  onCancel: () => void;
}

/**
 * ALV-015-C01: revise a plan's content: its clinical rationale, the explanation a patient will read, its phases and where each procedure sits. Every revision says why and is saved as the next version; the
 * earlier ones stay readable. A gap is named beside its field before anything is sent; the server's refusal is shown the same way and everything typed is kept. Saving what the plan already says changes nothing.
 */
export function TreatmentPlanReviseForm({ plan, onDone, onConflict, onCancel }: Props) {
  const uid = useId();
  const [draft, setDraft] = useState<ReviseDraft>(() => draftFromPlan(plan));
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [message, setMessage] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const summary = useRef<HTMLDivElement>(null);
  const live = liveItems(plan);
  const set = (patch: Partial<ReviseDraft>) => { setDraft((d) => ({ ...d, ...patch })); setMessage(null); };

  function refuse(found: Record<string, string>, text: string) {
    setErrors(found);
    setMessage(text);
    setTimeout(() => summary.current?.focus(), 0);
  }

  async function save() {
    setErrors({});
    setMessage(null);
    const gaps = reviseGaps(draft);
    if (Object.keys(gaps).length > 0) return refuse(gaps, "Not saved: some entries need correcting. Nothing was sent.");
    setBusy(true);
    try {
      const saved = await revisePlan(plan, toReviseInput(draft, live));
      onDone(saved, saved.currentRevision === plan.currentRevision ? "Nothing changed, so no new revision was saved." : `Plan revised: this is now revision ${saved.currentRevision}. The earlier version is kept.`);
    } catch (err) {
      const server = planFieldErrorsOf(err);
      if (isConcurrencyConflict(err)) { setMessage("Not saved: someone else changed this treatment plan. What you entered is still here."); onConflict(); }
      else if (err instanceof ApiError && Object.keys(server).length > 0) refuse(server, "Not saved: some entries need correcting. Everything you entered is still here.");
      else if (isNetworkFailure(err)) setMessage("Not saved: the connection dropped. What you entered is still here; save again to retry.");
      else setMessage(`Not saved: ${err instanceof ApiError ? err.message : "something went wrong."} What you entered is still here.`);
    } finally {
      setBusy(false);
    }
  }

  const area = (key: "rationale" | "explanation", field: "clinicalRationale" | "patientExplanation", label: string, hint: string) => (
    <label htmlFor={`${uid}-${key}`}>{label}
      <textarea id={`${uid}-${key}`} rows={3} value={draft[key]} onChange={(e) => set({ [key]: e.target.value })} aria-invalid={errors[field] ? true : undefined} aria-describedby={errors[field] ? `${uid}-${key}-problem` : undefined} />
      <span className="alv-clinical__meta">{hint} {draft[key].length} of {TEXT_MAX} characters</span>
      {errors[field] && <span id={`${uid}-${key}-problem`} className="alv-tp__error">{errors[field]}</span>}
    </label>
  );

  return (
    <form className="alv-dx__form" aria-label="Revise this treatment plan" onSubmit={(e) => { e.preventDefault(); void save(); }}>
      {message && <p className="alv-dx__message" role="status">{message}</p>}
      {Object.keys(errors).length > 0 && (
        <div className="alv-dx__problems" role="alert" tabIndex={-1} ref={summary}>
          <h3>Some entries need correcting</h3>
          <ul>{Object.values(errors).map((m, i) => <li key={`${i}-${m}`}>{m}</li>)}</ul>
        </div>
      )}
      <p className="alv-clinical__meta">Saving creates revision {plan.currentRevision + 1}. Revision {plan.currentRevision} and every earlier version stay on record.</p>
      <label htmlFor={`${uid}-reason`}>Why is this plan being revised?
        <input id={`${uid}-reason`} type="text" maxLength={500} value={draft.reason} onChange={(e) => set({ reason: e.target.value })} aria-invalid={errors.reason ? true : undefined} aria-describedby={errors.reason ? `${uid}-reason-problem` : undefined} />
        {errors.reason && <span id={`${uid}-reason-problem`} className="alv-tp__error">{errors.reason}</span>}
      </label>
      {area("rationale", "clinicalRationale", "Clinical rationale (for staff)", "Why this care is proposed.")}
      {area("explanation", "patientExplanation", "Explanation for the patient", "Shown on the patient copy, in plain words.")}

      <fieldset className="alv-dx__plan">
        <legend>Phases</legend>
        <TreatmentPlanPhaseEditor phases={draft.phases} errors={errors} idBase={`${uid}-phase`} onChange={(phases) => set({ phases })} onRemove={(n) => { setDraft((d) => removePhase(d, n)); setMessage(null); }} />
      </fieldset>

      {draft.phases.length > 0 && live.length > 0 && (
        <fieldset className="alv-dx__plan">
          <legend>Where each procedure goes</legend>
          {live.map((item, i) => (
            <label key={item.id} htmlFor={`${uid}-place-${item.id}`}>{item.procedureCode} {item.procedureDescription}
              <select id={`${uid}-place-${item.id}`} value={draft.placement[item.id] ?? ""} aria-invalid={errors[`placements[${i}].phaseNumber`] ? true : undefined}
                onChange={(e) => set({ placement: { ...draft.placement, [item.id]: e.target.value === "" ? null : Number(e.target.value) } })}>
                <option value="">Not yet placed</option>
                {draft.phases.map((p) => <option key={p.number} value={p.number}>Phase {p.number}{p.name.trim() ? `: ${p.name.trim()}` : ""}</option>)}
              </select>
              {errors[`placements[${i}].phaseNumber`] && <span className="alv-tp__error">{errors[`placements[${i}].phaseNumber`]}</span>}
            </label>
          ))}
        </fieldset>
      )}

      <div className="alv-clinical__row-actions">
        <button type="submit" className="btn btn-primary" disabled={busy}>Save revision</button>
        <button type="button" className="btn btn-outline-secondary" onClick={onCancel}>Cancel</button>
      </div>
    </form>
  );
}
