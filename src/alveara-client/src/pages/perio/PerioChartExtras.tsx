import { useEffect, useId, useState } from "react";
import { ApiError } from "../../services/authApi";
import { isNetworkFailure } from "../../services/clinicalApi";
import { PERIO_LINK_LABELS, PERIO_LINK_TYPES, linkPerioChart, perioProblemsOf } from "../../services/perioApi";
import { listDiagnoses } from "../../services/diagnosisApi";
import type { Diagnosis } from "../../services/diagnosisApi";
import type { PerioChart } from "../../services/perioApi";
import { displayTooth, toothName } from "../odontogram/toothNumbering";
import type { NumberingSystem } from "../odontogram/toothNumbering";
import { showInstant } from "../../time/practiceTime";

/** What a saved chart records about whole teeth: mobility, furcation, and teeth not charted. Blank means not assessed, in words. */
export function PerioToothRecords({ chart, numbering }: { chart: PerioChart; numbering: NumberingSystem }) {
  const teeth = chart.teeth ?? [];
  if (teeth.length === 0) return null;
  return (
    <div className="alv-perio__arch">
      <table className="alv-perio__table">
        <caption className="alv-perio__sr">Whole-tooth records in this chart</caption>
        <thead><tr><th scope="col">Tooth</th><th scope="col">Mobility (grade)</th><th scope="col">Furcation (grade)</th><th scope="col">Charted</th></tr></thead>
        <tbody>
          {teeth.map((t) => (
            <tr key={t.toothKey}>
              <th scope="row" title={toothName(t.toothKey)}>{displayTooth(t.toothKey, numbering)}</th>
              <td>{t.mobility ?? "not assessed"}</td><td>{t.furcation ?? "not assessed"}</td><td>{t.excluded ? "Not charted" : "Yes"}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

interface LinksProps {
  chart: PerioChart;
  canWrite: boolean;
  /** Called with the chart as the server now holds it, so the list shows the new link at once. */
  onChanged: (chart: PerioChart) => void;
}

const when = (iso: string) => showInstant(iso);

/**
 * The records a chart is linked to (a diagnosis, treatment plan, encounter or history entry) by reference. A link is added, never edited or removed; adding the same one again changes nothing. The
 * records themselves may not exist yet, so the reference is plain text. A refused or failed link says so and keeps what was typed.
 */
export function PerioChartLinks({ chart, canWrite, onChanged }: LinksProps) {
  const [type, setType] = useState<string>(PERIO_LINK_TYPES[0]);
  const [reference, setReference] = useState("");
  const [diagnosisId, setDiagnosisId] = useState("");
  const diagnosisFieldId = useId();   // an explicit label, so the select is named "Diagnosis" alone (a wrapping label adds the chosen option to its name)
  const [diagnoses, setDiagnoses] = useState<Diagnosis[] | null>(null);
  const [note, setNote] = useState<{ kind: "saved" | "failed"; text: string } | null>(null);
  const [busy, setBusy] = useState(false);
  const links = chart.links ?? [];
  const isDiagnosis = type === "Diagnosis";
  // ALV-N022 A3 (F-098, D-1 B): a diagnosis link names one of THIS patient's diagnoses (withdrawn ones are not offered); the server and the database refuse any other
  useEffect(() => {
    if (!canWrite || !isDiagnosis || diagnoses !== null) return;
    const controller = new AbortController();
    listDiagnoses(chart.patientId, {}, controller.signal).then((r) => setDiagnoses(r.diagnoses.filter((d) => d.status !== "Withdrawn"))).catch(() => { if (!controller.signal.aborted) setDiagnoses([]); });
    return () => controller.abort();
  }, [canWrite, isDiagnosis, diagnoses, chart.patientId]);

  async function add() {
    setBusy(true);
    try {
      onChanged(await linkPerioChart(chart.id, type, reference, isDiagnosis ? diagnosisId : undefined));
      setReference("");
      setDiagnosisId("");
      setNote({ kind: "saved", text: "Linked." });
    } catch (err) {
      const problems = perioProblemsOf(err);
      setNote({ kind: "failed", text: isNetworkFailure(err) ? "Not linked: the connection dropped. What you typed is still here; try again." : problems.length > 0 ? `Not linked: ${problems.map((p) => p.message).join(" ")}` : `Not linked: ${err instanceof ApiError ? err.message : "something went wrong."}` });
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="alv-perio__links">
      <h4>Linked records</h4>
      {links.length === 0
        ? <p className="alv-clinical__meta">This chart is not linked to a diagnosis, treatment plan, encounter or history entry.</p>
        : <ul aria-label="Linked records">{links.map((l) => <li key={`${l.linkType}:${l.reference}`}>{PERIO_LINK_LABELS[l.linkType] ?? l.linkType}: <strong>{l.diagnosisId ? (l.diagnosisLabel ?? "a diagnosis of this patient") : l.reference}</strong>{l.linkType === "Diagnosis" && !l.diagnosisId ? <span className="alv-clinical__meta"> (free-text reference, legacy)</span> : null} <span className="alv-clinical__meta">(linked by {l.linkedByName ?? "a staff member"}, {when(l.linkedAtUtc)})</span></li>)}</ul>}
      {canWrite && (
        <form className="alv-perio__linkform" aria-label="Link this chart" onSubmit={(e) => { e.preventDefault(); void add(); }}>
          <label>Link to
            <select value={type} onChange={(e) => setType(e.target.value)}>{PERIO_LINK_TYPES.map((t) => <option key={t} value={t}>{PERIO_LINK_LABELS[t]}</option>)}</select>
          </label>
          {isDiagnosis ? (
            <>
              <label htmlFor={diagnosisFieldId}>Diagnosis</label>
              <select id={diagnosisFieldId} value={diagnosisId} onChange={(e) => setDiagnosisId(e.target.value)}>
                <option value="">{diagnoses === null ? "Loading this patient's diagnoses…" : diagnoses.length === 0 ? "This patient has no diagnosis to link" : "Choose a diagnosis"}</option>
                {(diagnoses ?? []).map((d) => <option key={d.id} value={d.id}>{d.label}</option>)}
              </select>
            </>
          ) : (
            <label>Reference
              <input type="text" maxLength={100} value={reference} onChange={(e) => setReference(e.target.value)} />
            </label>
          )}
          <button type="submit" className="btn btn-outline-secondary" disabled={busy || (isDiagnosis ? diagnosisId === "" : reference.trim() === "")}>Add link</button>
        </form>
      )}
      {note && <p className={note.kind === "failed" ? "alv-perio__fielderror" : "alv-clinical__meta"} role={note.kind === "failed" ? "alert" : "status"}>{note.text}</p>}
    </div>
  );
}
