import { useCallback, useEffect, useState } from "react";
import { ErrorState, LoadingState } from "../../../components/StatePatterns";
import { ApiError } from "../../../services/authApi";
import { newKey } from "../../../services/clinicalApi";
import type { PatientDetail } from "../../../services/patientsApi";
import {
  amendPrescription, cancelPrescription, discontinuePrescription, getWorkflowOptions, listDrafts, listRecords,
} from "../../../services/prescriptionWorkflowApi";
import type { DraftView, PrescriptionRecord, WorkflowOptions } from "../../../services/prescriptionWorkflowApi";
import { getSafety } from "../../../services/safetyApi";
import type { SafetyContext } from "../../../services/safetyApi";
import { when } from "../../treatmentplan/treatmentPlanRules";
import "../Prescription.css";
import "./PrescriptionWorkflow.css";
import { DraftComposer } from "./DraftComposer";
import { PrescriptionRecordList } from "./PrescriptionRecordList";
import { PrintPreview } from "./PrintPreview";

interface Props {
  patient: PatientDetail;
  /** Whether the signed-in person may prescribe. It only decides which controls are drawn (the server checks every call). */
  canPrescribe?: boolean;
}

/** What the composer is showing: a brand-new draft, or an existing one. `session` changes only when a different draft is opened, so the composer keeps its typed text while a draft is saved. */
interface Composing { session: number; draft: DraftView | null }

/**
 * ALV-016-C01: the prescription workflow for one patient. People who may prescribe get their open drafts, the composer beside the persistent patient-safety panel, and the finalized prescriptions with their
 * actions; everyone who may read gets the finalized prescriptions (and can print them). A banner always says that no drug-knowledge source is configured: nothing on this screen checks interactions or whether a dose is
 * clinically right. A failed load says it could not load rather than showing an empty list.
 */
export function PrescriptionWorkflowPanel({ patient, canPrescribe = false }: Props) {
  const [options, setOptions] = useState<WorkflowOptions | null>(null);
  const [drafts, setDrafts] = useState<DraftView[] | null>(canPrescribe ? null : []);          // a role that cannot prescribe has no drafts to load
  const [records, setRecords] = useState<PrescriptionRecord[] | null>(null);
  const [safety, setSafety] = useState<SafetyContext | null | "failed">(null);
  const [failed, setFailed] = useState(false);
  const [status, setStatus] = useState<string | null>(null);
  const [composing, setComposing] = useState<Composing | null>(null);
  const [printing, setPrinting] = useState<string | null>(null);

  const loadDrafts = useCallback((signal?: AbortSignal) => {
    if (!canPrescribe) return;
    listDrafts(patient.id, signal).then((r) => { setDrafts(r.drafts); setFailed(false); }).catch(() => { if (!signal?.aborted) setFailed(true); });
  }, [patient.id, canPrescribe]);
  const loadRecords = useCallback((signal?: AbortSignal) => {
    listRecords(patient.id, signal).then((r) => { setRecords(r.prescriptions); setFailed(false); }).catch(() => { if (!signal?.aborted) setFailed(true); });
  }, [patient.id]);

  useEffect(() => {
    const controller = new AbortController();
    getWorkflowOptions(controller.signal).then(setOptions).catch(() => { if (!controller.signal.aborted) setFailed(true); });
    loadDrafts(controller.signal);
    loadRecords(controller.signal);
    getSafety(patient.id, controller.signal).then(setSafety).catch(() => { if (!controller.signal.aborted) setSafety("failed"); });
    return () => controller.abort();
  }, [patient.id, loadDrafts, loadRecords]);

  if (failed && (options === null || drafts === null || records === null)) return <ErrorState title="Could not load the prescriptions" description="Do not assume none are recorded. Check your connection and reload the page." />;
  if (options === null || drafts === null || records === null) return <LoadingState label="Loading the prescriptions…" />;

  const open = (draft: DraftView | null) => { setPrinting(null); setComposing((c) => ({ session: (c?.session ?? 0) + 1, draft })); };
  const finished = (text: string) => { setStatus(text); setComposing(null); loadDrafts(); loadRecords(); };

  async function amend(id: string) {
    try {
      const started = await amendPrescription(id, newKey());
      setStatus(started.replayed ? "An amendment of this prescription is already open as a draft." : "An amendment was started as a draft. The original stays active until you finalize it.");
      loadDrafts();
      open(started.draft);
    } catch (err) {
      setStatus(`The amendment could not be started: ${err instanceof ApiError ? err.message : "the connection dropped. Try again."}`);
    }
  }

  async function end(kind: "cancel" | "discontinue", id: string, reason: string): Promise<string | null> {
    try {
      const done = await (kind === "cancel" ? cancelPrescription : discontinuePrescription)(id, reason);
      setStatus(done.replayed ? `This prescription was already ${done.record.status.toLowerCase()}; nothing new was recorded.` : `${done.record.medication} ${done.record.status.toLowerCase()} and logged.`);
      loadRecords();
      return null;
    } catch (err) {
      const listed = err instanceof ApiError && Array.isArray(err.body.problems) ? (err.body.problems as { message: string }[]).map((p) => p.message).join(" ") : "";
      return `Nothing was recorded. ${listed || (err instanceof ApiError ? err.message : "The connection dropped. Try again.")}`;
    }
  }

  return (
    <section className="alv-clinical alv-rx" aria-labelledby="alv-rxw-title">
      <h2 id="alv-rxw-title" className="alv-workspace__section-title">Prescription composer</h2>
      <p className="alv-rx__banner" role="note"><strong>Not configured:</strong> {options.decisionSupportMessage}</p>
      <p className="alv-clinical__status" role="status" aria-live="polite">{status ?? (canPrescribe ? "Nothing is final until you finalize a draft." : "You can read and print these but your role cannot prescribe.")}</p>
      {failed && <p className="alv-clinical__meta" role="alert">Could not refresh the prescriptions. What is shown may be out of date.</p>}

      {canPrescribe && (
        <>
          <h3>Drafts</h3>
          {drafts.length === 0 ? <p className="alv-clinical__meta">No draft is open for this patient.</p> : (
            <ul className="alv-rx__list" aria-label="Open drafts">
              {drafts.map((d) => (
                <li key={d.id} className="alv-rx__item">
                  <h4 className="alv-rx__medication">{d.medication}{d.supersedesPrescriptionId ? " (amendment)" : ""}</h4>
                  <p className="alv-clinical__meta">Last changed by {d.updatedByName ?? "an unnamed user"} on {when(d.updatedAtUtc)}.</p>
                  <button type="button" className="btn btn-outline-primary btn-sm" onClick={() => open(d)}>Open draft {d.medication}</button>
                </li>
              ))}
            </ul>
          )}
          {!composing && <button type="button" className="btn btn-primary" onClick={() => open(null)}>New prescription draft</button>}
          {composing && (
            <DraftComposer key={composing.session} patientId={patient.id} options={options} draft={composing.draft} safety={safety}
              onChanged={() => loadDrafts()} onClosed={() => { setComposing(null); loadDrafts(); }} onFinalized={(_r, text) => finished(text)} onDiscarded={finished} />
          )}
        </>
      )}

      {printing && <PrintPreview key={printing} prescriptionId={printing} onClose={() => setPrinting(null)} />}

      <h3>Finalized prescriptions</h3>
      <PrescriptionRecordList records={records} canPrescribe={canPrescribe} onPrint={(id) => { setComposing(null); setPrinting(id); }} onAmend={(id) => void amend(id)} onEnd={end} />
    </section>
  );
}
