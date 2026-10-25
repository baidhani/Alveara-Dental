import { useCallback, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { Button } from "../../components/Button";
import { ErrorState, LoadingState } from "../../components/StatePatterns";
import { briefName, dismissPair, getMergeLog, listDismissals, mergeErrorMessage, REASON_MAX, restoreDismissal, scanDuplicates } from "../../services/patientMergesApi";
import type { Dismissal, DuplicatePair, DuplicateScan, MergeLogEntry, MergeResult, PatientBrief, UnmergePreview, UnmergeResult } from "../../services/patientMergesApi";
import { MergeReview } from "./MergeReview";
import { UnmergeReview } from "./UnmergeReview";
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
  const [dismissals, setDismissals] = useState<Dismissal[] | null>(null);
  const [dismissing, setDismissing] = useState<string | null>(null);
  const [dismissReason, setDismissReason] = useState("");
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState(false);
  const [pair, setPair] = useState<{ sourceId: string; survivorId: string } | null>(null);
  const [undoing, setUndoing] = useState<string | null>(null);
  const [done, setDone] = useState<Done | null>(null);

  const load = useCallback((signal?: AbortSignal) => {
    Promise.all([scanDuplicates(signal), getMergeLog(signal), listDismissals(signal)]).then(([s, l, d]) => { setScan(s); setLog(l); setDismissals(d); setFailed(false); }).catch(() => { if (!signal?.aborted) setFailed(true); });
  }, []);
  useEffect(() => { const controller = new AbortController(); load(controller.signal); return () => controller.abort(); }, [load]);

  const review = (p: DuplicatePair) => {
    const survivorId = p.suggestedSurvivorId;
    setDone(null);
    setPair({ sourceId: survivorId === p.first.id ? p.second.id : p.first.id, survivorId });
  };

  if (failed && scan === null) return <ErrorState title="Could not load the duplicate review" description="Check your connection and reload the page." />;
  const pairKey = (p: DuplicatePair) => `${p.first.id}-${p.second.id}`;

  async function dismiss(p: DuplicatePair) {
    const reason = dismissReason.trim();
    if (reason === "" || reason.length > REASON_MAX) { setError(`Write why these are different people (up to ${REASON_MAX} characters).`); return; }
    setBusy(true); setError(null); setNotice(null);
    try { await dismissPair(p.first.id, p.second.id, reason); setDismissing(null); setDismissReason(""); setNotice(`${briefName(p.first)} and ${briefName(p.second)} are no longer suggested.`); load(); }
    catch (err) { setError(mergeErrorMessage(err)); } finally { setBusy(false); }
  }

  async function restore(d: Dismissal) {
    setBusy(true); setError(null); setNotice(null);
    try { await restoreDismissal(d.id); setNotice(`${briefName(d.first)} and ${briefName(d.second)} are suggested again.`); load(); }
    catch (err) { setError(mergeErrorMessage(err)); } finally { setBusy(false); }
  }

  if (scan === null || log === null || dismissals === null) return <LoadingState label="Looking for possible duplicates…" />;

  return (
    <section className="alv-clinical alv-fu" aria-labelledby="alv-merge-page-title">
      <h1 id="alv-merge-page-title" className="alv-workspace__section-title">Duplicate patient records</h1>
      <p className="alv-clinical__meta">Pairs of patients who may be the same person: they share a date of birth and a name, phone number or email. Review a pair to merge the two records. A merged record is never deleted.</p>

      <p className="alv-clinical__status" role="status" aria-live="polite">{notice ?? ""}</p>
      {error && <p role="alert" className="alv-fu-error">{error}</p>}

      {done && (
        <div role="status" className="alv-clinical__status">
          Merged {briefName(done.source)} into {briefName(done.survivor)}. {done.result.moves.reduce((n, m) => n + m.rows, 0)} rows moved{done.result.resolutions.some((r) => r.kind === "Record") ? ", and some records stayed with the retired record" : ""}.{" "}
          <Link to={`/patients/${done.survivor.id}`}>Open the record that carries on</Link>
        </div>
      )}

      {undoing ? (
        <UnmergeReview
          mergeId={undoing}
          onCancel={() => setUndoing(null)}
          onDone={(result: UnmergeResult, preview: UnmergePreview) => { setUndoing(null); setNotice(`Undid the merge of ${briefName(preview.source)} into ${briefName(preview.survivor)}: ${result.rowsMovedBack} rows moved back, and ${briefName(preview.source)} is active again.`); load(); }}
        />
      ) : pair ? (
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
                  <tr key={pairKey(p)}>
                    <td><strong>{briefName(p.first)}</strong> and <strong>{briefName(p.second)}</strong>{p.sameHousehold && <div className="alv-clinical__meta">Same household: could be twins or relatives.</div>}</td>
                    <td>{p.first.dateOfBirth}</td>
                    <td>{p.reasons.join("; ")}</td>
                    <td>{p.score}%</td>
                    <td className="alv-fu-actions">
                      <Button type="button" onClick={() => review(p)} aria-label={`Review ${briefName(p.first)} and ${briefName(p.second)}`}>Review</Button>
                      {dismissing === pairKey(p) ? (
                        <>
                          <label className="alv-fu-sr" htmlFor={`why-${pairKey(p)}`}>Why {briefName(p.first)} and {briefName(p.second)} are different people</label>
                          <input id={`why-${pairKey(p)}`} className="alv-form-field__input" value={dismissReason} maxLength={REASON_MAX} onChange={(e) => setDismissReason(e.target.value)} autoComplete="off" />
                          <Button type="button" variant="primary" disabled={busy} onClick={() => dismiss(p)} aria-label={`Confirm: ${briefName(p.first)} and ${briefName(p.second)} are not duplicates`}>Confirm</Button>
                          <Button type="button" onClick={() => { setDismissing(null); setDismissReason(""); setError(null); }}>Cancel</Button>
                        </>
                      ) : (
                        <Button type="button" onClick={() => { setDismissing(pairKey(p)); setDismissReason(""); setError(null); setNotice(null); }} aria-label={`${briefName(p.first)} and ${briefName(p.second)} are not duplicates`}>Not a duplicate</Button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}

          <h2>Dismissed as not a duplicate ({dismissals.length})</h2>
          {dismissals.length === 0 ? <p>No pairs have been dismissed.</p> : (
            <table className="alv-fu-table">
              <caption className="alv-fu-sr">Pairs dismissed as not a duplicate</caption>
              <thead><tr><th scope="col">Patients</th><th scope="col">Why</th><th scope="col">By</th><th scope="col">When</th><th scope="col">Actions</th></tr></thead>
              <tbody>
                {dismissals.map((d) => (
                  <tr key={d.id}>
                    <td><strong>{briefName(d.first)}</strong> and <strong>{briefName(d.second)}</strong></td>
                    <td>{d.reason}</td><td>{d.dismissedByName || d.dismissedByUserId}</td><td>{new Date(d.dismissedAtUtc).toLocaleString()}</td>
                    <td className="alv-fu-actions"><Button type="button" disabled={busy} onClick={() => restore(d)} aria-label={`Suggest ${briefName(d.first)} and ${briefName(d.second)} again`}>Suggest again</Button></td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}

          <h2>Merge log</h2>
          {log.length === 0 ? <p>No records have been merged yet.</p> : (
            <table className="alv-fu-table">
              <caption className="alv-fu-sr">Merges made</caption>
              <thead><tr><th scope="col">When</th><th scope="col">Merged away</th><th scope="col">Into</th><th scope="col">By</th><th scope="col">Reason</th><th scope="col">Rows moved</th><th scope="col">Stayed behind</th><th scope="col">Status</th><th scope="col">Actions</th></tr></thead>
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
                    <td>{m.undoneAtUtc ? `Undone ${new Date(m.undoneAtUtc).toLocaleDateString()} by ${m.undoneByName || "an administrator"}` : "In force"}</td>
                    <td className="alv-fu-actions">{m.canUndo && <Button type="button" onClick={() => { setUndoing(m.mergeId); setNotice(null); setError(null); }} aria-label={`Undo the merge of ${briefName(m.source)} into ${briefName(m.survivor)}`}>Undo</Button>}</td>
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
