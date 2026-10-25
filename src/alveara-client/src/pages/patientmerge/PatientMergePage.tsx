import { useCallback, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { Button } from "../../components/Button";
import { ErrorState, LoadingState } from "../../components/StatePatterns";
import { briefName, getMergeLog, scanDuplicates } from "../../services/patientMergesApi";
import type { DuplicatePair, DuplicateScan, MergeLogEntry, MergeResult, PatientBrief } from "../../services/patientMergesApi";
import { MergeReview } from "./MergeReview";
import "../followups/followups.css";

interface Done { result: MergeResult; source: PatientBrief; survivor: PatientBrief }

/**
 * STORY-017: the administrator's duplicate review. The scan lists pairs of patients who may be the same person, strongest first, each with the reasons in words and a suggested record to keep; choosing one opens the
 * side-by-side review (the administrator decides which record carries on and can swap them). After a merge the page says what happened and links to the record that carries on; the merge log underneath shows every
 * merge with who and when. Nothing here changes a record until the review is confirmed.
 */
export function PatientMergePage() {
  const [scan, setScan] = useState<DuplicateScan | null>(null);
  const [log, setLog] = useState<MergeLogEntry[] | null>(null);
  const [failed, setFailed] = useState(false);
  const [pair, setPair] = useState<{ sourceId: string; survivorId: string } | null>(null);
  const [done, setDone] = useState<Done | null>(null);

  const load = useCallback((signal?: AbortSignal) => {
    Promise.all([scanDuplicates(signal), getMergeLog(signal)]).then(([s, l]) => { setScan(s); setLog(l); setFailed(false); }).catch(() => { if (!signal?.aborted) setFailed(true); });
  }, []);
  useEffect(() => { const controller = new AbortController(); load(controller.signal); return () => controller.abort(); }, [load]);

  const review = (p: DuplicatePair) => {
    const survivorId = p.suggestedSurvivorId;
    setDone(null);
    setPair({ sourceId: survivorId === p.first.id ? p.second.id : p.first.id, survivorId });
  };

  if (failed && scan === null) return <ErrorState title="Could not load the duplicate review" description="Check your connection and reload the page." />;
  if (scan === null || log === null) return <LoadingState label="Looking for possible duplicates…" />;

  return (
    <section className="alv-clinical alv-fu" aria-labelledby="alv-merge-page-title">
      <h1 id="alv-merge-page-title" className="alv-workspace__section-title">Duplicate patient records</h1>
      <p className="alv-clinical__meta">Pairs of patients who may be the same person: they share a date of birth and a name, phone number or email. Review a pair to merge the two records. A merged record is never deleted.</p>

      {done && (
        <div role="status" className="alv-clinical__status">
          Merged {briefName(done.source)} into {briefName(done.survivor)}. {done.result.moves.reduce((n, m) => n + m.rows, 0)} rows moved{done.result.resolutions.some((r) => r.kind === "Record") ? ", and some records stayed with the retired record" : ""}.{" "}
          <Link to={`/patients/${done.survivor.id}`}>Open the record that carries on</Link>
        </div>
      )}

      {pair ? (
        <MergeReview
          sourceId={pair.sourceId}
          survivorId={pair.survivorId}
          onSwap={() => setPair({ sourceId: pair.survivorId, survivorId: pair.sourceId })}
          onCancel={() => setPair(null)}
          onMerged={(result, source, survivor) => { setPair(null); setDone({ result, source, survivor }); load(); }}
        />
      ) : (
        <>
          <h2>Possible duplicates{scan.truncated ? " (strongest 200 shown)" : ""}</h2>
          {scan.pairs.length === 0 ? <p>No possible duplicates found among {scan.patientsConsidered} patients who share a birth date.</p> : (
            <table className="alv-fu-table">
              <caption className="alv-fu-sr">Possible duplicate patient records</caption>
              <thead><tr><th scope="col">Patients</th><th scope="col">Date of birth</th><th scope="col">Why</th><th scope="col">Match</th><th scope="col">Actions</th></tr></thead>
              <tbody>
                {scan.pairs.map((p) => (
                  <tr key={`${p.first.id}-${p.second.id}`}>
                    <td><strong>{briefName(p.first)}</strong> and <strong>{briefName(p.second)}</strong>{p.sameHousehold && <div className="alv-clinical__meta">Same household: could be twins or relatives.</div>}</td>
                    <td>{p.first.dateOfBirth}</td>
                    <td>{p.reasons.join("; ")}</td>
                    <td>{p.score}%</td>
                    <td className="alv-fu-actions"><Button type="button" onClick={() => review(p)} aria-label={`Review ${briefName(p.first)} and ${briefName(p.second)}`}>Review</Button></td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}

          <h2>Merge log</h2>
          {log.length === 0 ? <p>No records have been merged yet.</p> : (
            <table className="alv-fu-table">
              <caption className="alv-fu-sr">Merges made</caption>
              <thead><tr><th scope="col">When</th><th scope="col">Merged away</th><th scope="col">Into</th><th scope="col">By</th><th scope="col">Reason</th><th scope="col">Rows moved</th><th scope="col">Stayed behind</th></tr></thead>
              <tbody>
                {log.map((m) => (
                  <tr key={m.mergeId}>
                    <td>{new Date(m.mergedAtUtc).toLocaleString()}</td>
                    <td>{briefName(m.source)}</td>
                    <td><Link to={`/patients/${m.survivor.id}`}>{briefName(m.survivor)}</Link></td>
                    <td>{m.mergedByName || m.mergedByUserId}</td>
                    <td>{m.reason}</td>
                    <td>{m.rowsMoved}</td>
                    <td>{m.recordsLeftBehind}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </>
      )}
    </section>
  );
}
