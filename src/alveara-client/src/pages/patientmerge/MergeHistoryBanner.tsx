import { useEffect, useState } from "react";
import { SafeLink } from "../../components/SafeLink";
import { getPatientMergeHistory } from "../../services/patientMergesApi";
import type { MergeLink, PatientMergeHistory } from "../../services/patientMergesApi";

const when = (iso: string) => new Date(iso).toLocaleDateString();

function Line({ link }: { link: MergeLink }) {
  return <>{link.patientName} on {when(link.mergedAtUtc)} by {link.mergedByName || "an administrator"} ({link.reason})</>;
}

/**
 * ALV-017-C01: the merge lineage on a patient's own page. A record that was merged into another says so in words and points to the record that carries on, so nobody keeps working in a retired chart; a record that carries
 * on lists the records merged into it, so the lineage stays visible after the merge. Nothing is shown for a patient with no merge history. If the history cannot be read the page says so instead of staying silent, because a
 * retired record that looks like any other is the one thing this banner exists to prevent.
 */
export function MergeHistoryBanner({ patientId }: { patientId: string }) {
  const [history, setHistory] = useState<PatientMergeHistory | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    setHistory(null); setFailed(false);
    getPatientMergeHistory(patientId, controller.signal).then((h) => { if (h && typeof h === "object" && Array.isArray(h.absorbed) && "mergedInto" in h) setHistory(h); else setFailed(true); }).catch(() => { if (!controller.signal.aborted) setFailed(true); });
    return () => controller.abort();
  }, [patientId]);

  if (failed) return <p className="alv-clinical__meta" role="status">Could not check whether this record was merged into another. Reload the page.</p>;
  if (!history || (!history.mergedInto && history.absorbed.length === 0)) return null;
  return (
    <section className="alv-merge-banner" aria-label="Merge history">
      {history.mergedInto && (
        <p role="alert" className="alv-fu-error">
          <strong>This record was merged into another and takes no new records.</strong> It was merged into{" "}
          <SafeLink to={`/patients/${history.mergedInto.patientId}`}>{history.mergedInto.patientName}</SafeLink> on {when(history.mergedInto.mergedAtUtc)} by {history.mergedInto.mergedByName || "an administrator"}: {history.mergedInto.reason}. Open that record to continue.
        </p>
      )}
      {history.absorbed.length > 0 && (
        <div className="alv-clinical__meta">
          <strong>Records merged into this one:</strong>
          <ul>{history.absorbed.map((a) => <li key={a.mergeId}><SafeLink to={`/patients/${a.patientId}`}><Line link={a} /></SafeLink></li>)}</ul>
        </div>
      )}
    </section>
  );
}
