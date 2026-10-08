import { useEffect, useId, useState } from "react";
import type { FormEvent } from "react";
import { Button } from "../../components/Button";
import { useUnsavedChangesWarning } from "../../hooks/useUnsavedChangesWarning";
import { getProcedure, getProcedureHistory, getProcedureUsage, inactivateProcedure, reactivateProcedure, reviseProcedure } from "../../services/proceduresApi";
import type { ProcedureDetail, ProcedureEvent, ProcedureSummary, ProcedureUsage, ProcedureVersion } from "../../services/proceduresApi";
import { ReasonForm } from "../safety/SafetyForms";
import type { SaveResult } from "../safety/SafetyForms";
import { ProcedureForm } from "./ProcedureForm";
import { CATEGORY_TEXT, CODE_SYSTEM_TEXT, appliesText, dateText, formatFee, inputOf, when } from "./procedureText";

export type RunProcedure = (change: () => Promise<ProcedureDetail>, savedText: string) => Promise<SaveResult>;

function VersionLine({ v }: { v: ProcedureVersion }) {
  return (
    <li>
      <span className="alv-clinical__history-what">Version {v.versionNumber}: {formatFee(v.fee)}</span>
      <span> · from {dateText(v.effectiveFrom)}{v.validThrough ? ` to ${dateText(v.validThrough)}` : ""} · {v.description}</span>
      {v.sourceVersion && <span> · edition {v.sourceVersion}</span>}
      {v.reason && <span> · Reason: {v.reason}</span>}
      <span className="alv-clinical__meta"> {when(v.createdAtUtc)} by {v.createdByName ?? "Staff member"}</span>
    </li>
  );
}

function ProcedureHistory({ p }: { p: ProcedureSummary }) {
  const [open, setOpen] = useState(false);
  const [data, setData] = useState<{ versions: ProcedureVersion[]; events: ProcedureEvent[] } | null>(null);
  const [failed, setFailed] = useState(false);
  useEffect(() => {
    if (!open) return;
    const controller = new AbortController();
    Promise.all([getProcedure(p.id, controller.signal), getProcedureHistory(p.id, controller.signal)])
      .then(([detail, events]) => { setFailed(false); setData({ versions: detail.versions, events }); })
      .catch(() => { if (!controller.signal.aborted) setFailed(true); });
    return () => controller.abort();
  }, [open, p.id, p.rowVersion]);
  return (
    <details className="alv-clinical__history" onToggle={(e) => setOpen((e.currentTarget as HTMLDetailsElement).open)}>
      <summary aria-label={`Fee and change history of ${p.code}`}>Fee and change history</summary>
      {open && failed && <p className="alv-form-field__error" role="alert">Could not load the history. Close it and open it again.</p>}
      {open && !failed && data === null && <p className="alv-clinical__meta">Loading the history…</p>}
      {data && (
        <>
          <ol className="alv-clinical__history-list" aria-label={`Versions of ${p.code}`}>{data.versions.map((v) => <VersionLine key={v.versionNumber} v={v} />)}</ol>
          <ol className="alv-clinical__history-list" aria-label={`Changes to ${p.code}`}>
            {data.events.map((e) => (
              <li key={e.eventNumber}><span className="alv-clinical__history-what">{e.changeType}</span>{e.reason && <span> · Reason: {e.reason}</span>}<span className="alv-clinical__meta"> {when(e.occurredAtUtc)} by {e.actorName ?? "Staff member"}</span></li>
            ))}
          </ol>
        </>
      )}
    </details>
  );
}

/** Asks why, and - when other records refer to the procedure - says how many and asks for confirmation before it is inactivated. */
function InactivateForm({ p, busy, run, onDone }: { p: ProcedureSummary; busy: boolean; run: RunProcedure; onDone: () => void }) {
  const [usage, setUsage] = useState<ProcedureUsage | null>(null);
  const [usageFailed, setUsageFailed] = useState(false);
  const [reason, setReason] = useState("");
  const [confirmed, setConfirmed] = useState(false);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const ids = { reason: useId(), confirm: useId() };
  useUnsavedChangesWarning(reason.trim() !== "");
  useEffect(() => {
    const controller = new AbortController();
    getProcedureUsage(p.id, controller.signal).then(setUsage).catch(() => { if (!controller.signal.aborted) setUsageFailed(true); });
    return () => controller.abort();
  }, [p.id]);

  const referenced = (usage?.count ?? 0) > 0;
  async function submit(event: FormEvent) {
    event.preventDefault();
    if (busy || usage === null) return;
    const found: Record<string, string> = {};
    if (reason.trim() === "") found.reason = "Say why.";
    if (referenced && !confirmed) found.confirm = "Confirm that you understand before inactivating.";
    setErrors(found);
    if (Object.keys(found).length > 0) return;
    const refused = await run(() => inactivateProcedure(p.id, reason.trim(), referenced && confirmed, p.rowVersion), `${p.code} inactivated.`);
    if (refused) setErrors(refused); else onDone();
  }

  return (
    <form className="alv-safety__form" onSubmit={submit} noValidate aria-label={`Inactivate procedure ${p.code}`}>
      {usage === null && !usageFailed && <p className="alv-clinical__meta">Checking where {p.code} is used…</p>}
      {usageFailed && <p className="alv-form-field__error" role="alert">Could not check where this procedure is used, so it cannot be inactivated now. Cancel and try again.</p>}
      {referenced && (
        <div role="alert" className="alv-procedures__warning">
          <p><strong>{p.code} is referred to by {usage!.count} {usage!.count === 1 ? "record" : "records"}</strong> ({usage!.bySource.filter((b) => b.count > 0).map((b) => `${b.source}: ${b.count}`).join(", ")}).</p>
          <p>Inactivating it does not change or remove those records. It only stops the procedure being offered for new work.</p>
          <div className="alv-form-field">
            <label htmlFor={ids.confirm}><input id={ids.confirm} type="checkbox" checked={confirmed} onChange={(e) => setConfirmed(e.target.checked)} /> I understand and want to inactivate it</label>
            {errors.confirm && <p className="alv-form-field__error" role="alert">{errors.confirm}</p>}
          </div>
        </div>
      )}
      <div className="alv-form-field">
        <label htmlFor={ids.reason} className="alv-form-field__label">{`Why is ${p.code} being inactivated?`}</label>
        <textarea id={ids.reason} className="alv-form-field__input alv-clinical__details" rows={2} maxLength={500} value={reason} aria-invalid={errors.reason ? true : undefined} onChange={(e) => setReason(e.target.value)} />
        {errors.reason && <p className="alv-form-field__error" role="alert">{errors.reason}</p>}
      </div>
      <div className="alv-clinical__row-actions">
        <Button type="submit" variant="primary" disabled={busy || usage === null}>Inactivate procedure</Button>
        <Button type="button" onClick={onDone} disabled={busy}>Cancel</Button>
      </div>
    </form>
  );
}

export function ProcedureRow({ p, canManage, busy, run }: { p: ProcedureSummary; canManage: boolean; busy: boolean; run: RunProcedure }) {
  const [mode, setMode] = useState<"view" | "change" | "inactivate" | "reactivate">("view");
  const v = p.version;
  const done = () => setMode("view");
  return (
    <li className="alv-odonto__condition">
      <p className="alv-odonto__finding-title">
        <strong>{p.code}</strong> {v.description}
        <span className="alv-odonto__chip">{p.status}</span>
      </p>
      <p className="alv-odonto__finding-meta">
        <strong>{formatFee(v.fee)}</strong> · {CODE_SYSTEM_TEXT[p.codeSystem]}{v.sourceName ? ` (${v.sourceName}${v.sourceVersion ? `, ${v.sourceVersion}` : ""})` : ""} · {CATEGORY_TEXT[v.category]} · {appliesText(v)}
        {" "}· fee in effect from {dateText(v.effectiveFrom)}{v.validThrough ? `, valid through ${dateText(v.validThrough)}` : ""}{p.status === "Scheduled" ? " (not started yet)" : ""}.
      </p>
      {canManage && mode === "view" && (
        <div className="alv-clinical__row-actions">
          {p.isActive && <Button type="button" onClick={() => setMode("change")} disabled={busy} aria-label={`Change procedure ${p.code}`}>Change</Button>}
          {p.isActive
            ? <Button type="button" onClick={() => setMode("inactivate")} disabled={busy} aria-label={`Inactivate procedure ${p.code}`}>Inactivate</Button>
            : <Button type="button" onClick={() => setMode("reactivate")} disabled={busy} aria-label={`Reactivate procedure ${p.code}`}>Reactivate</Button>}
        </div>
      )}
      {mode === "change" && (
        <ProcedureForm initial={inputOf(p.codeSystem, p.code, v)} revising={{ codeSystemText: CODE_SYSTEM_TEXT[p.codeSystem], code: p.code }} busy={busy} submitLabel="Save change" onCancel={done}
          onSubmit={async (input, reason) => { const refused = await run(() => reviseProcedure(p.id, input, reason, p.rowVersion), `${p.code} changed.`); if (!refused) done(); return refused; }} />
      )}
      {mode === "inactivate" && <InactivateForm p={p} busy={busy} run={run} onDone={done} />}
      {mode === "reactivate" && (
        <ReasonForm label={`Why is ${p.code} being reactivated? (optional)`} required={false} submitLabel="Reactivate procedure" ariaLabel={`Reactivate procedure ${p.code}`} busy={busy} onCancel={done}
          onSubmit={async (reason) => { const refused = await run(() => reactivateProcedure(p.id, reason, p.rowVersion), `${p.code} reactivated.`); if (!refused) done(); return refused; }} />
      )}
      <ProcedureHistory p={p} />
    </li>
  );
}
