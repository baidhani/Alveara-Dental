import { useEffect, useId, useRef, useState } from "react";
import { ApiError, isConcurrencyConflict } from "../../services/authApi";
import { isNetworkFailure, newKey } from "../../services/clinicalApi";
import { adoptCompletion, getCompletionReadiness } from "../../services/completionApi";
import type { CompletionReadiness, ItemReadiness } from "../../services/completionApi";
import { planFieldErrorsOf } from "../../services/treatmentPlansApi";
import type { TreatmentPlan } from "../../services/treatmentPlansApi";
import type { ChangeProps } from "./TreatmentPlanActions";
import { localInputValue, money, toUtcIso } from "./completionReviewRules";
import { when } from "./treatmentPlanRules";

interface Props extends ChangeProps {
  plan: TreatmentPlan;
  onClose: () => void;
}

const FIELDS = ["providerId", "encounterId", "performedAtUtc", "reason", "idempotencyKey"];
const REASON_MAX = 500;

const label = (i: ItemReadiness) => [i.procedureCode, i.procedureDescription, i.toothKey ? `tooth ${i.toothKey}` : null, i.surface ? `surface ${i.surface}` : null].filter(Boolean).join(" · ");

/** What adopting this procedure will do, in words, from what the server said (nothing is worked out here). */
export function adoptionEffectWords(i: ItemReadiness): string {
  const chart = i.chartEffect === "Updated" ? "the linked chart finding will be marked Completed"
    : i.chartEffect === "AlreadyCompleted" ? "the linked chart finding is already Completed and is left alone"
      : i.chartEffect === "Ambiguous" ? "more than one chart finding could match, so it cannot be adopted until the chart is clarified" : "no chart finding changes";
  const charge = !i.billable ? "it has no fee, so nothing is charged" : i.chargeExists ? "its existing charge is kept" : `a charge of ${money(i.fee)} will be made`;
  return `${chart}; ${charge}.`;
}

/**
 * ALV-008-C02: adopt a procedure that was completed the old, state-only way. That completion recorded only that the procedure was done; adopting it supplies who performed it, for which encounter, when, and why it is
 * being recorded now, and then gives it its chart effect and its one charge, exactly as completing does. The earlier completion is not changed. What adopting will do is read from the server (the readiness); the request
 * carries the plan's version as read and a save key that a retry reuses; any failure says nothing was recorded and keeps what was entered.
 */
export function TreatmentPlanAdoptCompletion({ plan, onChanged, onConflict, onClose }: Props) {
  const [readiness, setReadiness] = useState<CompletionReadiness | null>(null);
  const [loadFailed, setLoadFailed] = useState(false);
  const [itemId, setItemId] = useState("");
  const [providerId, setProviderId] = useState("");
  const [encounterId, setEncounterId] = useState("");
  const [performed, setPerformed] = useState(() => localInputValue(new Date()));
  const [reason, setReason] = useState("");
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [note, setNote] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [reloads, setReloads] = useState(0);
  const attempt = useRef<{ key: string; signature: string } | null>(null);
  const uid = useId();

  useEffect(() => {
    const controller = new AbortController();
    getCompletionReadiness(plan.id, undefined, controller.signal)
      .then((r) => {
        setReadiness(r);
        setLoadFailed(false);
        const legacy = r.items.filter((i) => i.legacyCompleted);
        setItemId((was) => (legacy.some((i) => i.itemId === was) ? was : legacy[0]?.itemId ?? ""));
      })
      .catch(() => { if (!controller.signal.aborted) setLoadFailed(true); });
    return () => controller.abort();
  }, [plan.id, reloads]);

  if (loadFailed && readiness === null) {
    return (
      <div className="alv-dx__form">
        <p className="alv-dx__message" role="alert">Could not load what adopting would do, so nothing can be recorded now. Check your connection and try again.</p>
        <div className="alv-clinical__row-actions">
          <button type="button" className="btn btn-outline-primary" onClick={() => setReloads((n) => n + 1)}>Try again</button>
          <button type="button" className="btn btn-outline-secondary" onClick={onClose}>Cancel</button>
        </div>
      </div>
    );
  }
  if (readiness === null) return <p className="alv-clinical__meta">Loading the earlier completions…</p>;

  const legacy = readiness.items.filter((i) => i.legacyCompleted);
  if (legacy.length === 0) {
    return (
      <div className="alv-dx__form" role="status">
        <p className="alv-clinical__meta">No procedure on this plan was completed the old way, so there is nothing to adopt.</p>
        <button type="button" className="btn btn-outline-secondary" onClick={onClose}>Close</button>
      </div>
    );
  }
  const chosen = legacy.find((i) => i.itemId === itemId) ?? legacy[0];

  async function submit() {
    if (readiness === null) return;
    const utc = toUtcIso(performed);
    const gaps: Record<string, string> = {};
    if (providerId === "") gaps.providerId = "Choose the provider who performed the care.";
    if (encounterId === "") gaps.encounterId = "Choose the encounter this care belongs to.";
    if (utc === null) gaps.performedAtUtc = "Say when the procedure was performed.";
    if (reason.trim() === "") gaps.reason = "Say why this is being recorded now.";
    setNote(null);
    setErrors(gaps);
    if (Object.keys(gaps).length > 0) return;

    const signature = JSON.stringify([readiness.rowVersion, chosen.itemId, encounterId, providerId, utc, reason.trim()]);
    if (attempt.current?.signature !== signature) attempt.current = { key: newKey(), signature };
    setBusy(true);
    try {
      const done = await adoptCompletion(plan.id, chosen.itemId, { idempotencyKey: attempt.current.key, rowVersion: readiness.rowVersion, encounterId, providerId, performedAtUtc: utc!, reason: reason.trim() });
      onChanged(done.plan, `${chosen.procedureCode} adopted${done.replayed ? " earlier; nothing new was recorded" : ""}: its provider, encounter and time are recorded, and it is on the chart and the ledger like any completion.`);
      onClose();
    } catch (err) {
      setBusy(false);
      const fields = planFieldErrorsOf(err);
      if (isConcurrencyConflict(err)) { setNote("Nothing was recorded: this plan changed since you opened this screen. Reload and look again."); onConflict(); }
      else if (Object.keys(fields).some((f) => FIELDS.includes(f))) { setErrors(Object.fromEntries(Object.entries(fields).filter(([f]) => FIELDS.includes(f)))); setNote("Nothing was recorded: check the fields marked below."); }
      else if (isNetworkFailure(err)) setNote("Nothing was recorded: the connection dropped. What you entered is still here; try again (the same request is safe to repeat).");
      else setNote(`Nothing was recorded: ${err instanceof ApiError ? err.message : "something went wrong."}`);
    }
  }

  const fieldError = (name: string) => errors[name] && <span id={`${uid}-${name}`} className="alv-tp__error">{errors[name]}</span>;
  return (
    <form className="alv-dx__form" aria-label="Adopt an earlier completion" onSubmit={(e) => { e.preventDefault(); void submit(); }}>
      <h4>Adopt an earlier completion</h4>
      <p className="alv-clinical__meta">
        These procedures were marked completed before provider, encounter, chart and charge records existed. Adopting one records who performed it, for which visit, when, and why it is being recorded now. The earlier completion is not changed.
      </p>
      {note && <p className="alv-dx__message" role="alert">{note}</p>}
      <label htmlFor={`${uid}-item`}>Procedure</label>
      <select id={`${uid}-item`} value={chosen.itemId} onChange={(e) => setItemId(e.target.value)}>
        {legacy.map((i) => <option key={i.itemId} value={i.itemId}>{label(i)}</option>)}
      </select>
      <p className="alv-clinical__meta" aria-live="polite">Adopting {chosen.procedureCode}: {adoptionEffectWords(chosen)}</p>
      <label htmlFor={`${uid}-provider`}>Provider</label>
      <select id={`${uid}-provider`} value={providerId} onChange={(e) => setProviderId(e.target.value)} aria-describedby={errors.providerId ? `${uid}-providerId` : undefined}>
        <option value="">Choose a provider</option>
        {readiness.providers.map((p) => <option key={p.id} value={p.id}>{p.name} ({p.specialty})</option>)}
      </select>
      {fieldError("providerId")}
      <label htmlFor={`${uid}-encounter`}>Encounter</label>
      <select id={`${uid}-encounter`} value={encounterId} onChange={(e) => setEncounterId(e.target.value)} aria-describedby={errors.encounterId ? `${uid}-encounterId` : undefined}>
        <option value="">Choose an encounter</option>
        {readiness.encounters.map((e) => <option key={e.id} value={e.id}>{when(e.atUtc)} ({e.status})</option>)}
      </select>
      {fieldError("encounterId")}
      <label htmlFor={`${uid}-performed`}>Performed on</label>
      <input id={`${uid}-performed`} type="datetime-local" step="1" value={performed} onChange={(e) => setPerformed(e.target.value)} aria-describedby={errors.performedAtUtc ? `${uid}-performedAtUtc` : undefined} />
      {fieldError("performedAtUtc")}
      <label htmlFor={`${uid}-reason`}>Why is this being recorded now?</label>
      <textarea id={`${uid}-reason`} rows={2} maxLength={REASON_MAX} value={reason} onChange={(e) => setReason(e.target.value)} aria-describedby={errors.reason ? `${uid}-reason-error` : undefined} />
      {errors.reason && <span id={`${uid}-reason-error`} className="alv-tp__error">{errors.reason}</span>}
      <div className="alv-clinical__row-actions">
        <button type="submit" className="btn btn-primary" disabled={busy || chosen.chartEffect === "Ambiguous"}>Adopt {chosen.procedureCode}</button>
        <button type="button" className="btn btn-outline-secondary" onClick={onClose}>Cancel</button>
        {note && <button type="button" className="btn btn-outline-primary" onClick={() => { setNote(null); setReloads((n) => n + 1); }}>Reload</button>}
      </div>
    </form>
  );
}
