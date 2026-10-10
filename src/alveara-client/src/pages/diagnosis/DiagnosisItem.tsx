import { useState } from "react";
import { Link } from "react-router-dom";
import type { EncounterSummary } from "../../services/clinicalApi";
import { reactivateDiagnosis, resolveDiagnosis, withdrawDiagnosis } from "../../services/diagnosisApi";
import type { Diagnosis } from "../../services/diagnosisApi";
import type { NumberingSystem } from "../odontogram/toothNumbering";
import { DiagnosisAmendForm } from "./DiagnosisAmendForm";
import { DiagnosisChips } from "./DiagnosisChips";
import { DiagnosisForm } from "./DiagnosisForm";
import { DiagnosisHistory } from "./DiagnosisHistory";
import { DiagnosisLinkForm } from "./DiagnosisLinkForm";
import { DiagnosisReasonForm } from "./DiagnosisReasonForm";

const when = (iso: string) => new Date(iso).toLocaleString();

/** How a reference was tied to its plan, in words. */
const methodWords = (method: string | null | undefined) =>
  method === "ReferenceCode" ? "matched to the plan's reference code" : method === "PlanId" ? "matched to the plan's id" : method === "Chosen" ? "chosen by a person" : "tied to the plan";

/**
 * The treatment-plan reference as it is shown anywhere. Until it is tied to a real plan it is labelled unresolved, with the plain statement that it proves nothing. Once a person or a reconciliation has tied it
 * to a plan of the same patient (ALV-015-C01) it says which plan, how and by whom; the original text is shown unchanged because it is never replaced.
 */
export function PlanReference({ value, resolved }: { value: string | null; resolved?: { patientId: string; title: string | null; method: string | null; byName: string | null; atUtc: string | null } }) {
  if (value === null) return <span className="alv-clinical__meta">No treatment plan reference.</span>;
  if (resolved) {
    return (
      <span className="alv-dx__reference">
        Treatment plan reference (tied to a plan): <strong>{value}</strong> is the treatment plan <Link to={`/patients/${resolved.patientId}/treatment-plan`} className="alv-workspace__link">{resolved.title ?? "this patient's plan"}</Link>.
        <span className="alv-clinical__meta"> {methodWords(resolved.method)[0].toUpperCase() + methodWords(resolved.method).slice(1)}{resolved.byName ? ` by ${resolved.byName}` : ""}{resolved.atUtc ? `, ${new Date(resolved.atUtc).toLocaleString()}` : ""}. The original text is kept as it was written.</span>
      </span>
    );
  }
  return (
    <span className="alv-dx__reference">
      Treatment plan reference (unresolved): <strong>{value}</strong>
      <span className="alv-clinical__meta"> This is a note to link a plan later; it does not mean a treatment plan with this reference exists.</span>
    </span>
  );
}

type Mode = "none" | "correct" | "amend" | "resolve" | "reactivate" | "link" | "withdraw" | "history";

interface Props {
  diagnosis: Diagnosis;
  encounters: EncounterSummary[];
  numbering: NumberingSystem;
  canWrite: boolean;
  /** The diagnosis as the server now holds it, after a change. */
  onChanged: (diagnosis: Diagnosis, text: string) => void;
  onConflict: () => void;
}

/**
 * One diagnosis. It shows what was recorded, for which encounter, by whom and when, its context as chips (tooth or region, coding, source, linked findings and charts) and the treatment-plan reference as
 * an unresolved note. People who can write can correct it (label, tooth, notes), amend its structure, resolve or reactivate it, link it, or withdraw it; every one of those but linking asks why, and
 * nothing is ever deleted. Anyone who can read can open its history, which lists every step with the whole diagnosis as it stood. A resolved diagnosis stays on the list marked Resolved; a withdrawn one stays
 * visible, marked Withdrawn with its reason, and can no longer be changed.
 */
export function DiagnosisItem({ diagnosis: d, encounters, numbering, canWrite, onChanged, onConflict }: Props) {
  const [mode, setMode] = useState<Mode>("none");
  const toggle = (next: Mode) => setMode(mode === next ? "none" : next);
  const done = (saved: Diagnosis, text: string) => { setMode("none"); onChanged(saved, text); };

  const encounter = encounters.find((e) => e.id === d.encounterId);
  const open = d.status !== "Withdrawn";
  return (
    <li className={`alv-dx__item${d.status === "Withdrawn" ? " alv-dx__item--withdrawn" : ""}`} aria-label={`Diagnosis: ${d.label}`}>
      <p className="alv-dx__label">
        <strong>{d.label}</strong>
        {d.status === "Withdrawn" && <span className="alv-dx__badge"> Withdrawn</span>}
        {d.status === "Resolved" && <span className="alv-dx__badge"> Resolved</span>}
      </p>
      <DiagnosisChips diagnosis={d} numbering={numbering} />
      <p className="alv-clinical__meta">
        Encounter of {when(d.encounterAtUtc)}{encounter ? ` (${encounter.status.toLowerCase()})` : ""} · recorded by {d.recordedByName ?? "a staff member"}, {when(d.recordedAtUtc)}
        {d.updatedAtUtc ? ` · last changed by ${d.updatedByName ?? "a staff member"}, ${when(d.updatedAtUtc)}` : ""}
      </p>
      {d.notes && <p className="alv-dx__notes">{d.notes}</p>}
      <p><PlanReference value={d.treatmentPlanReference} resolved={d.treatmentPlanReferenceState === "Resolved" ? { patientId: d.patientId, title: d.resolvedPlanTitle ?? null, method: d.resolvedMethod ?? null, byName: d.resolvedByName ?? null, atUtc: d.resolvedAtUtc ?? null } : undefined} /></p>
      {d.status === "Withdrawn" && <p className="alv-clinical__meta">Withdrawn by {d.withdrawnByName ?? "a staff member"}{d.withdrawnAtUtc ? `, ${when(d.withdrawnAtUtc)}` : ""}: {d.withdrawnReason}</p>}

      <div className="alv-clinical__row-actions">
        <button type="button" className="btn btn-outline-secondary" aria-expanded={mode === "history"} onClick={() => toggle("history")}>{mode === "history" ? "Hide history" : "History"}</button>
        {canWrite && open && (
          <>
            <button type="button" className="btn btn-outline-secondary" onClick={() => toggle("correct")}>Correct</button>
            <button type="button" className="btn btn-outline-secondary" onClick={() => toggle("amend")}>Amend</button>
            {d.status === "Active"
              ? <button type="button" className="btn btn-outline-secondary" onClick={() => toggle("resolve")}>Mark resolved</button>
              : <button type="button" className="btn btn-outline-secondary" onClick={() => toggle("reactivate")}>Make active again</button>}
            <button type="button" className="btn btn-outline-secondary" onClick={() => toggle("link")}>Link</button>
            <button type="button" className="btn btn-outline-secondary" onClick={() => toggle("withdraw")}>Withdraw</button>
          </>
        )}
      </div>

      {mode === "correct" && <DiagnosisForm patientId={d.patientId} numbering={numbering} encounters={encounters} correcting={d} onSaved={done} onConflict={onConflict} onCancel={() => setMode("none")} />}
      {mode === "amend" && <DiagnosisAmendForm diagnosis={d} numbering={numbering} onSaved={done} onConflict={onConflict} onCancel={() => setMode("none")} />}
      {mode === "link" && <DiagnosisLinkForm diagnosis={d} onLinked={done} onCancel={() => setMode("none")} />}
      {mode === "withdraw" && (
        <DiagnosisReasonForm title="Withdraw this diagnosis" hint="Withdrawing marks a wrong diagnosis as withdrawn. It stays in the record and its history, treatment plan reference included; nothing is deleted."
          question="Why is this diagnosis being withdrawn?" button="Withdraw diagnosis" past="withdrawn" act={(r) => withdrawDiagnosis(d, r)} onDone={(s) => done(s, "Diagnosis withdrawn.")} onConflict={onConflict} onCancel={() => setMode("none")} />
      )}
      {mode === "resolve" && (
        <DiagnosisReasonForm title="Mark this diagnosis resolved" hint="Resolved means the condition is no longer present. The diagnosis stays on the list and in the history, and it can be made active again."
          question="Why is this diagnosis resolved?" button="Mark resolved" past="resolved" act={(r) => resolveDiagnosis(d, r)} onDone={(s) => done(s, "Diagnosis marked resolved.")} onConflict={onConflict} onCancel={() => setMode("none")} />
      )}
      {mode === "reactivate" && (
        <DiagnosisReasonForm title="Make this diagnosis active again" hint="This diagnosis was marked resolved. Making it active again records that the condition is present."
          question="Why is this diagnosis active again?" button="Make active again" past="reactivated" act={(r) => reactivateDiagnosis(d, r)} onDone={(s) => done(s, "Diagnosis made active again.")} onConflict={onConflict} onCancel={() => setMode("none")} />
      )}
      {mode === "history" && <DiagnosisHistory id={d.id} />}
    </li>
  );
}
