import { useEffect, useState } from "react";
import { LoadingState } from "../../components/StatePatterns";
import { getDiagnosisHistory } from "../../services/diagnosisApi";
import type { DiagnosisVersion } from "../../services/diagnosisApi";
import { REGION_LABELS, sourceText } from "./diagnosisStructureRules";

const when = (iso: string) => new Date(iso).toLocaleString();
const CHANGE_LABELS: Record<string, string> = {
  Recorded: "Recorded", Corrected: "Corrected", Withdrawn: "Withdrawn", Amended: "Amended (structure)", Resolved: "Marked resolved", Reactivated: "Made active again",
};

/** What the diagnosis's structure was at one step, in words: its region, coding and source (or "none"); its tooth is shown with the diagnosis. */
function structureText(v: DiagnosisVersion): string {
  const parts = [
    v.regionKey ? `region ${REGION_LABELS[v.regionKey] ?? v.regionKey}` : null,
    v.codingSystem && v.code ? `${v.codingSystem} ${v.code}` : null,
    sourceText(v.source, v.sourceNote),
  ].filter((p): p is string => p !== null);
  return parts.length === 0 ? "none" : parts.join("; ");
}

/**
 * The history of one diagnosis, oldest first: every step with who, when and why, and the whole diagnosis as it stood then (its place, coding and source, and the treatment-plan reference, always marked
 * unresolved). Amendments, resolving and reactivating appear as steps of their own, so what an amendment replaced is never lost. A failed load says so rather than showing an empty history.
 */
export function DiagnosisHistory({ id }: { id: string }) {
  const [versions, setVersions] = useState<DiagnosisVersion[] | null>(null);
  const [failed, setFailed] = useState(false);
  useEffect(() => {
    const controller = new AbortController();
    getDiagnosisHistory(id, controller.signal).then((h) => setVersions(h.versions)).catch(() => { if (!controller.signal.aborted) setFailed(true); });
    return () => controller.abort();
  }, [id]);
  if (failed) return <p className="alv-clinical__meta" role="alert">Could not load the history. Do not assume there is none. Close and open it again.</p>;
  if (versions === null) return <LoadingState label="Loading the history…" />;
  return (
    <div className="alv-dx__history">
      <table className="alv-dx__table">
        <caption className="alv-dx__sr">History of this diagnosis, oldest first</caption>
        <thead><tr><th scope="col">When</th><th scope="col">By</th><th scope="col">What happened</th><th scope="col">Diagnosis then</th><th scope="col">Treatment plan reference then</th><th scope="col">Why</th><th scope="col">Region, coding and source then</th></tr></thead>
        <tbody>
          {versions.map((v) => (
            <tr key={v.versionNumber}>
              <td>{when(v.occurredAtUtc)}</td><td>{v.actorName ?? "a staff member"}</td><td>{CHANGE_LABELS[v.changeType] ?? v.changeType}</td>
              <td>{v.label}{v.toothKey ? ` (tooth ${v.toothKey} FDI)` : ""}</td>
              <td>{v.treatmentPlanReference === null ? "none" : `${v.treatmentPlanReference} (unresolved)`}</td>
              <td>{v.reason ?? "-"}</td>
              <td>{structureText(v)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
