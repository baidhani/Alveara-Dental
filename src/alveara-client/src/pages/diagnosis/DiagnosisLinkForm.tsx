import { useEffect, useId, useState } from "react";
import { LoadingState } from "../../components/StatePatterns";
import { ApiError } from "../../services/authApi";
import { isNetworkFailure } from "../../services/clinicalApi";
import { diagnosisProblemsOf, linkDiagnosis } from "../../services/diagnosisApi";
import type { Diagnosis, DiagnosisLink } from "../../services/diagnosisApi";
import { getChart } from "../../services/odontogramApi";
import type { Finding } from "../../services/odontogramApi";
import { getPerioCharts } from "../../services/perioApi";
import type { PerioChart } from "../../services/perioApi";

interface Props {
  diagnosis: Diagnosis;
  onLinked: (diagnosis: Diagnosis, text: string) => void;
  onCancel: () => void;
}

const when = (iso: string) => new Date(iso).toLocaleDateString();
const findingText = (f: Finding) => `${f.conditionLabel} on tooth ${f.toothKey}${f.surface ? `, surface ${f.surface}` : ""}`;

/**
 * ALV-013-C01: link a diagnosis to a finding on the patient's odontogram or to one of the patient's periodontal charts, so the diagnosis can be traced back to what it was based on. Only this patient's own
 * records are offered (the server refuses anything else, in the same words, whether or not it exists for someone else). Linking is permanent and adds to the diagnosis; a link already made is not
 * offered again. If a list cannot be loaded the form says so rather than showing it as empty.
 */
export function DiagnosisLinkForm({ diagnosis: d, onLinked, onCancel }: Props) {
  const id = useId();
  const [findings, setFindings] = useState<Finding[] | null>(null);
  const [charts, setCharts] = useState<PerioChart[] | null>(null);
  const [failed, setFailed] = useState(false);
  const [choice, setChoice] = useState("");
  const [note, setNote] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    getChart(d.patientId, controller.signal).then((c) => setFindings(c.findings)).catch(() => { if (!controller.signal.aborted) setFailed(true); });
    getPerioCharts(d.patientId, controller.signal).then((h) => setCharts(h.exams)).catch(() => { if (!controller.signal.aborted) setFailed(true); });
    return () => controller.abort();
  }, [d.patientId]);

  if (failed) return <p className="alv-clinical__meta" role="alert">Could not load this patient's findings and charts, so nothing can be linked now. Do not assume there are none. Close this and try again.</p>;
  if (findings === null || charts === null) return <LoadingState label="Loading the findings and charts…" />;

  const linked = (type: DiagnosisLink["linkType"], target: string) => (d.links ?? []).some((l) => l.linkType === type && l.targetId === target);
  const options = [
    ...findings.filter((f) => !linked("Finding", f.id)).map((f) => ({ value: `Finding:${f.id}`, text: `Finding: ${findingText(f)}` })),
    ...charts.filter((c) => !linked("PerioExam", c.id)).map((c) => ({ value: `PerioExam:${c.id}`, text: `Periodontal chart of ${when(c.recordedAtUtc)}` })),
  ];

  async function submit() {
    const [type, target] = choice.split(":");
    setBusy(true);
    setNote(null);
    try {
      onLinked(await linkDiagnosis(d.id, type as DiagnosisLink["linkType"], target), "Diagnosis linked.");
    } catch (err) {
      const problems = diagnosisProblemsOf(err);
      if (problems.length > 0) setNote(`Not linked: ${problems.map((p) => p.message).join(" ")}`);
      else if (isNetworkFailure(err)) setNote("Not linked: the connection dropped. Try again.");
      else setNote(`Not linked: ${err instanceof ApiError ? err.message : "something went wrong."}`);
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="alv-dx__form" aria-label="Link this diagnosis" onSubmit={(e) => { e.preventDefault(); void submit(); }}>
      <p className="alv-clinical__meta">Link this diagnosis to a finding or a periodontal chart of this patient. A link is kept permanently and records who made it and when.</p>
      {note && <p className="alv-dx__message" role="alert">{note}</p>}
      {options.length === 0
        ? <p className="alv-clinical__meta" role="note">There is nothing left to link: this patient has no findings or periodontal charts that are not already linked.</p>
        : (
          <label htmlFor={id}>Link to
            <select id={id} value={choice} onChange={(e) => setChoice(e.target.value)}>
              <option value="">Choose a finding or chart</option>
              {options.map((o) => <option key={o.value} value={o.value}>{o.text}</option>)}
            </select>
          </label>
        )}
      <div className="alv-clinical__row-actions">
        <button type="submit" className="btn btn-primary" disabled={busy || choice === ""}>Link diagnosis</button>
        <button type="button" className="btn btn-outline-secondary" onClick={onCancel}>Cancel</button>
      </div>
    </form>
  );
}
