import { Button } from "./Button";
import type { ConcurrencyConflictProblem } from "../services/authApi";
import "./ConcurrencyConflictBanner.css";

/**
 * ALV-002-C01 R02 (review finding ALV-002-C01-R01-02): the shared reusable stale-edit/conflict
 * presentation pattern every future mutable-record edit flow renders when its save attempt returns
 * the backend's shared `ConcurrencyConflictProblem` shape (see `ConcurrencyConflictException.ToProblem()`,
 * detected client-side via `authApi.isConcurrencyConflict`).
 *
 * Deliberately does not attempt to merge or silently discard the caller's in-progress edit: it
 * names what happened in plain language and hands control to `onReload`, which an adopting edit
 * flow wires to re-fetch the current server version - never to re-submit the stale one. No domain
 * record currently has a UI edit flow to wire this into (see R01.md's "Review-relevant
 * limitations"); this component and its tests are the reusable pattern the first such flow adopts.
 */
export function ConcurrencyConflictBanner({
  problem,
  onReload,
}: {
  problem: ConcurrencyConflictProblem;
  onReload: () => void;
}) {
  return (
    <div className="alv-concurrency-conflict" role="alert">
      <p className="alv-concurrency-conflict__title">Someone else changed this while you were editing</p>
      <p className="alv-concurrency-conflict__description">
        This {problem.entityType.toLowerCase()} record was updated by someone else since you opened it. Your changes
        were not saved, so nothing was overwritten - reload to see the current version before trying again.
      </p>
      {/* type="button": this banner renders inside edit forms; a default (submit) button would re-submit the stale edit on every reload click. */}
      <Button type="button" variant="primary" onClick={onReload}>
        Reload current version
      </Button>
    </div>
  );
}
