import { useEffect, useId, useRef, useState } from "react";
import { ApiError, isConcurrencyConflict } from "../../services/authApi";
import { isNetworkFailure, newKey } from "../../services/clinicalApi";
import { completeProcedures, getCompletionReadiness, refusedItemsOf } from "../../services/completionApi";
import type { CompletionReadiness, CompletionResult } from "../../services/completionApi";
import { planFieldErrorsOf } from "../../services/treatmentPlansApi";
import type { TreatmentPlan } from "../../services/treatmentPlansApi";
import type { ChangeProps } from "./TreatmentPlanActions";
import { CompletionResultPanel } from "./CompletionResultPanel";
import { CompletionReviewTable } from "./CompletionReviewTable";
import { formGaps, localInputValue, requestSignature, summarize, summaryWords, toUtcIso } from "./completionReviewRules";
import { when } from "./treatmentPlanRules";

interface Props extends ChangeProps {
  plan: TreatmentPlan;
  onClose: () => void;
}

/** The server's per-procedure messages (keyed `items.<number>`) by item number. */
const itemNotesOf = (err: unknown): Record<number, string> =>
  Object.fromEntries(Object.entries(planFieldErrorsOf(err)).flatMap(([k, v]) => { const m = /^items\.(\d+)$/.exec(k); return m ? [[Number(m[1]), v]] : []; }));

const FIELDS = ["itemIds", "providerId", "encounterId", "performedAtUtc", "idempotencyKey"];

/**
 * ALV-008-C01: the completion review screen, the product way to complete procedures. It reads what completing would do from the server (the readiness) and shows it for every procedure: whether it can be
 * completed, what happens to the chart and what is charged. The person picks the procedures, the provider, the encounter and when it was performed, sees the total, and confirms. The request carries the
 * plan's version as the readiness read it and a save key that a retry reuses, so a double click or a dropped connection never completes anything twice. Nothing is shown as completed unless the server
 * confirmed the whole set; any failure says nothing was completed and keeps what was entered.
 */
export function TreatmentPlanReviewComplete({ plan, onChanged, onConflict, onClose }: Props) {
  const [readiness, setReadiness] = useState<CompletionReadiness | null>(null);
  const [loadFailed, setLoadFailed] = useState(false);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [providerId, setProviderId] = useState("");
  const [encounterId, setEncounterId] = useState("");
  const [performed, setPerformed] = useState(() => localInputValue(new Date()));
  const [note, setNote] = useState<string | null>(null);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [itemNotes, setItemNotes] = useState<Record<number, string>>({});
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<CompletionResult | null>(null);
  const attempt = useRef<{ key: string; signature: string } | null>(null);
  const first = useRef(true);
  const uid = useId();

  const [reloads, setReloads] = useState(0);
  const load = () => setReloads((n) => n + 1);

  useEffect(() => {
    const controller = new AbortController();
    getCompletionReadiness(plan.id, undefined, controller.signal)
      .then((r) => {
        const initial = first.current;                                  // read now: the updater below runs later, after this is cleared
        first.current = false;
        setReadiness(r);
        setLoadFailed(false);
        setSelected((was) => new Set(r.items.filter((i) => i.ready && (initial || was.has(i.itemId))).map((i) => i.itemId)));
      })
      .catch(() => { if (!controller.signal.aborted) setLoadFailed(true); });
    return () => controller.abort();
  }, [plan.id, reloads]);

  if (result) return <CompletionResultPanel result={result} patientId={plan.patientId} onDone={onClose} />;
  if (loadFailed && readiness === null) {
    return (
      <div className="alv-dx__form">
        <p className="alv-dx__message" role="alert">Could not load what completing would do, so nothing can be completed now. Check your connection and try again.</p>
        <div className="alv-clinical__row-actions">
          <button type="button" className="btn btn-outline-primary" onClick={load}>Try again</button>
          <button type="button" className="btn btn-outline-secondary" onClick={onClose}>Cancel</button>
        </div>
      </div>
    );
  }
  if (readiness === null) return <p className="alv-clinical__meta">Loading what each procedure would do…</p>;

  const summary = summarize(readiness.items, selected);
  const toggle = (id: string) => setSelected((was) => { const next = new Set(was); if (next.has(id)) next.delete(id); else next.add(id); return next; });

  async function submit() {
    if (readiness === null) return;
    const utc = toUtcIso(performed);
    const gaps = formGaps(providerId, encounterId, utc, selected.size);
    setNote(null);
    setItemNotes({});
    setErrors(gaps);
    if (Object.keys(gaps).length > 0) return;

    const itemIds = readiness.items.filter((i) => selected.has(i.itemId)).map((i) => i.itemId);
    const signature = requestSignature(readiness.rowVersion, itemIds, encounterId, providerId, utc!);
    if (attempt.current?.signature !== signature) attempt.current = { key: newKey(), signature };
    setBusy(true);
    try {
      const done = await completeProcedures(plan.id, { idempotencyKey: attempt.current.key, rowVersion: readiness.rowVersion, encounterId, providerId, performedAtUtc: utc!, itemIds });
      setResult(done);
      onChanged(done.plan, `${done.procedures.length} procedure${done.procedures.length === 1 ? "" : "s"} completed${done.replayed ? " earlier; nothing new was recorded" : ""} and added to the clinical history.`);
    } catch (err) {
      setBusy(false);
      const fields = planFieldErrorsOf(err);
      if (isConcurrencyConflict(err)) { setNote("Nothing was completed: this plan changed since you opened this screen. Reload the review and look again."); onConflict(); }
      else if (err instanceof ApiError && refusedItemsOf(err).length > 0) { setItemNotes(itemNotesOf(err)); setNote(`Nothing was completed: ${err.message}`); load(); }
      else if (Object.keys(fields).some((f) => FIELDS.includes(f))) { setErrors(Object.fromEntries(Object.entries(fields).filter(([f]) => FIELDS.includes(f)))); setNote("Nothing was completed: check the fields marked below."); }
      else if (isNetworkFailure(err)) setNote("Nothing was completed: the connection dropped. What you entered is still here; try again (the same request is safe to repeat).");
      else setNote(`Nothing was completed: ${err instanceof ApiError ? err.message : "something went wrong."}`);
    }
  }

  const fieldError = (name: string) => errors[name] && <span id={`${uid}-${name}`} className="alv-tp__error">{errors[name]}</span>;
  return (
    <form className="alv-dx__form" aria-label="Review and complete procedures" onSubmit={(e) => { e.preventDefault(); void submit(); }}>
      <h4>Review and complete</h4>
      <p className="alv-clinical__meta">
        Choose the procedures that were done, who did them, when and for which visit. Each completion is final: it is recorded with the plan revision, the provider and the encounter, moves the chart where a finding is linked, and charges the plan&apos;s fee.
      </p>
      {note && <p className="alv-dx__message" role="alert">{note}</p>}
      {fieldError("itemIds")}
      <CompletionReviewTable items={readiness.items} selected={selected} onToggle={toggle} notes={itemNotes} />
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
      <p className="alv-clinical__meta" aria-live="polite">{summaryWords(summary)}</p>
      <div className="alv-clinical__row-actions">
        <button type="submit" className="btn btn-primary" disabled={busy || summary.count === 0}>Confirm completion of {summary.count} procedure{summary.count === 1 ? "" : "s"}</button>
        <button type="button" className="btn btn-outline-secondary" onClick={onClose}>Cancel</button>
        {note && <button type="button" className="btn btn-outline-primary" onClick={() => { setNote(null); load(); }}>Reload the review</button>}
      </div>
    </form>
  );
}
