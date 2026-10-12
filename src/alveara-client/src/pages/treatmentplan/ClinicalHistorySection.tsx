import { useEffect, useState } from "react";
import { ErrorState } from "../../components/StatePatterns";
import { getClinicalHistory } from "../../services/treatmentPlansApi";
import type { ClinicalHistoryEntry } from "../../services/treatmentPlansApi";
import { when } from "./treatmentPlanRules";

/**
 * STORY-008: the care this patient has had, newest first, read from the completion records of their treatment plans (there is no separate record to drift). It reloads when `refreshKey` changes, so a completion
 * shows here straight away. An empty list says only that nothing has been completed; a failed load says it could not load rather than showing an empty list.
 */
export function ClinicalHistorySection({ patientId, refreshKey }: { patientId: string; refreshKey: number }) {
  const [entries, setEntries] = useState<ClinicalHistoryEntry[] | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    getClinicalHistory(patientId, controller.signal).then((r) => { setEntries(r.entries); setFailed(false); }).catch(() => { if (!controller.signal.aborted) setFailed(true); });
    return () => controller.abort();
  }, [patientId, refreshKey]);

  if (failed && entries === null) return <ErrorState title="Could not load the clinical history" description="Do not assume nothing has been completed. Check your connection and reload the page." />;
  if (entries === null) return <section aria-labelledby="alv-ch-title"><h3 id="alv-ch-title">Clinical history</h3><p className="alv-clinical__meta">Loading the clinical history…</p></section>;

  return (
    <section aria-labelledby="alv-ch-title">
      <h3 id="alv-ch-title">Clinical history</h3>
      {failed && <p className="alv-clinical__meta" role="alert">Could not refresh the clinical history. What is shown may be out of date.</p>}
      {entries.length === 0
        ? <p className="alv-clinical__meta">No procedure has been completed for this patient yet. That means none has been recorded, not that no care was given elsewhere.</p>
        : (
          <ul className="alv-dx__list" aria-label="Completed procedures">
            {entries.map((e) => (
              <li key={e.itemId} className="alv-dx__item">
                <strong>{e.procedureCode}</strong> {e.procedureDescription}{e.toothKey ? ` · tooth ${e.toothKey}` : ""}{e.surface ? ` · surface ${e.surface}` : ""}
                <p className="alv-clinical__meta">Completed by {e.completedByName} on {when(e.completedAtUtc)} · from {e.planTitle}, revision {e.planRevisionAtCompletion}</p>
                {e.completedProcedureId && (
                  <p className="alv-clinical__meta">
                    Performed by {e.providerName} on {when(e.performedAtUtc!)}
                    {e.chargeEntryNumber ? ` · charged $${(e.chargeAmount ?? 0).toFixed(2)} (account entry ${e.chargeEntryNumber})` : " · no charge"}
                    {e.chartOutcome === "Updated" ? " · chart finding completed" : ""}
                  </p>
                )}
              </li>
            ))}
          </ul>
        )}
    </section>
  );
}
