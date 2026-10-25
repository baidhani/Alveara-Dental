import { useCallback, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { Button } from "../../components/Button";
import { EmptyState, ErrorState, LoadingState } from "../../components/StatePatterns";
import { getPatientCaseTimeline, listCases, routeOf } from "../../services/externalCasesApi";
import type { CaseKind, ExternalCase, PatientCaseTimelineEntry } from "../../services/externalCasesApi";
import { describeEvent, formatDay, kindLabel, statusLabel } from "./externalCaseFormat";
import { NewCaseForm } from "./NewCaseForm";
import "../followups/followups.css";

interface Props { patientId: string; canManage: boolean }

/**
 * ALV-009-C02: a patient's referrals and lab cases: every case with its status and due date in words (OVERDUE as text), what happened to them newest first with who did it, and for people who may manage cases a way to start a
 * referral or a lab case. Each case links to its full page. A failed load says it could not load rather than showing an empty list.
 */
export function PatientExternalCasesPanel({ patientId, canManage }: Props) {
  const [cases, setCases] = useState<ExternalCase[] | null>(null);
  const [timeline, setTimeline] = useState<PatientCaseTimelineEntry[] | null>(null);
  const [failed, setFailed] = useState(false);
  const [creating, setCreating] = useState<CaseKind | null>(null);
  const [status, setStatus] = useState<string | null>(null);

  const load = useCallback((signal?: AbortSignal) => {
    Promise.all([listCases({ patientId, status: "all", pageSize: 100 }, signal), getPatientCaseTimeline(patientId, signal)])
      .then(([page, entries]) => { setCases(page.items.map((i) => i.case)); setTimeline(entries); setFailed(false); }).catch(() => { if (!signal?.aborted) setFailed(true); });
  }, [patientId]);
  useEffect(() => { const controller = new AbortController(); load(controller.signal); return () => controller.abort(); }, [load]);

  if (failed && cases === null) return <ErrorState title="Could not load the referrals and lab cases" description="Do not assume there are none. Check your connection and reload the page." />;
  if (cases === null || timeline === null) return <LoadingState label="Loading referrals and lab cases…" />;

  return (
    <section className="alv-clinical alv-fu" aria-labelledby="alv-pec-title">
      <h2 id="alv-pec-title" className="alv-workspace__section-title">Referrals and lab cases</h2>
      <p className="alv-clinical__status" role="status" aria-live="polite">{status ?? ""}</p>
      {failed && <p role="alert" className="alv-fu-error">Could not refresh. What is shown may be out of date.</p>}
      {canManage && creating === null && (
        <div className="alv-fu-actions">
          <Button type="button" variant="primary" onClick={() => { setCreating("Referral"); setStatus(null); }}>New referral</Button>
          <Button type="button" onClick={() => { setCreating("Lab"); setStatus(null); }}>New lab case</Button>
        </div>
      )}
      {!canManage && <p className="alv-clinical__meta">Your role can read referrals and lab cases but cannot start or change them.</p>}
      {creating && <NewCaseForm patientId={patientId} kind={creating} onCancel={() => setCreating(null)} onCreated={(made) => { setCreating(null); setStatus(`Saved: ${made.summary}.`); load(); }} />}

      {cases.length === 0 ? <EmptyState title="No referrals or lab cases" description="Nothing has been sent out for this patient." /> : (
        <ul className="alv-fu-list" aria-label="Cases of this patient">
          {cases.map((c) => (
            <li key={c.id}>
              <Link to={`/external-cases/${routeOf(c.kind)}/${c.id}`}><strong>{c.summary}</strong></Link>
              <span className="alv-fu-item__meta"> {kindLabel(c.kind)} · {c.partyName} · {statusLabel(c.status)} · follow up by {formatDay(c.dueOn)}</span>
              {c.overdue && <span className="alv-fu-badge alv-fu-badge--overdue">Overdue</span>}
            </li>
          ))}
        </ul>
      )}

      <h3>What happened</h3>
      {timeline.length === 0 ? <p>Nothing has happened yet.</p> : (
        <ol className="alv-fu-history" aria-label="Referral and lab timeline">
          {timeline.map((t, i) => (
            <li key={`${t.caseId}-${i}-${t.occurredAtUtc}`}>
              <strong>{describeEvent({ kind: t.eventKind, fromStatus: t.fromStatus, toStatus: t.toStatus })}</strong>: {t.caseSummary} ({kindLabel(t.caseKind)}) by {t.actorName ?? "a former colleague"} · {new Date(t.occurredAtUtc).toLocaleString()}
              {t.note && <span className="alv-fu-item__meta"> — {t.note}</span>}
            </li>
          ))}
        </ol>
      )}
    </section>
  );
}
