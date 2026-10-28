import { useState } from "react";
import { Button } from "../../components/Button";
import { useAuth } from "../../contexts/AuthContext";
import { commitBatch, importErrorMessage, retryCommit } from "../../services/bulkImportApi";
import type { ImportBatch, ImportPolicy } from "../../services/bulkImportApi";
import { kindLabel, money, validRows } from "./importFormat";

interface Props { batch: ImportBatch; pollExpired: boolean; onChanged: (batch: ImportBatch) => void; onReload: () => void; onRefresh: () => void }

/**
 * Step 4: the commit. Before anything is written it says exactly what will happen: how many rows, the total for balances, and what the chosen policy does to the rows that are not valid. All-or-nothing is offered only
 * when it can work (patients, with every row valid or skipped); appointments and balances are recorded one at a time, each in its own transaction, so only "valid rows" is offered for them and the page says why.
 * The person ticks a confirmation and the page sends the number of valid rows it showed: if the results changed in the meantime the server refuses and the page reloads, so nothing is committed on a stale screen.
 * While the commit runs the page follows it; if it stops (the server restarted, a job failed) the page says so and offers Retry, which starts the work again without repeating any row already done.
 */
export function CommitStep({ batch, pollExpired, onChanged, onReload, onRefresh }: Props) {
  const { hasPermission } = useAuth();
  const valid = validRows(batch);
  const balances = batch.sourceKind === "OpeningBalances";
  const needsBilling = balances && !hasPermission("ManageBilling");
  const allOrNothingBlock = batch.sourceKind !== "Patients" ? `${kindLabel(batch.sourceKind)} are recorded one at a time, each in its own transaction, so all-or-nothing is not available for them.`
    : batch.rejectedRows + batch.unresolvedRows > 0 ? "Every row must be valid or skipped. Fix the file or decide the waiting rows, or commit the valid rows only." : null;
  const [choice, setChoice] = useState<ImportPolicy | null>(null);
  const policy: ImportPolicy = choice ?? (allOrNothingBlock ? "CommitValidRows" : "AllOrNothing");
  const [confirmed, setConfirmed] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const run = async (action: () => Promise<ImportBatch>, staleOnError: boolean) => {
    setBusy(true); setError(null);
    try {
      onChanged(await action());
    } catch (e) {
      setError(importErrorMessage(e));
      if (staleOnError) onReload();
    } finally {
      setConfirmed(false);                                                              // every attempt, even a refused one, needs a fresh confirmation of what the page then shows
      setBusy(false);
    }
  };

  if (batch.status === "Committing") {
    return (
      <section aria-labelledby="import-commit" className="alv-fu-form alv-import__step">
        <h2 id="import-commit">Step 4: commit</h2>
        {batch.canRetry ? (
          <>
            <p role="alert" className="alv-import__warning"><strong>The commit stopped before it finished.</strong> Retrying starts the remaining work again. Rows that were already done are not repeated.</p>
            {needsBilling && <p>Retrying a balance commit also needs the Manage billing permission, which your role does not hold.</p>}
            <div className="alv-fu-actions"><Button variant="primary" onClick={() => run(() => retryCommit(batch.id), true)} disabled={busy || needsBilling}>{busy ? "Retrying…" : "Retry the commit"}</Button></div>
          </>
        ) : (
          <p role="status" aria-live="polite">The commit is running. This page checks every few seconds and will show the result here.</p>
        )}
        {pollExpired && <div className="alv-fu-actions"><p>It is still running after ten minutes.</p><Button onClick={onRefresh}>Check again</Button></div>}
        {error && <p role="alert" className="alv-fu-error">{error}</p>}
      </section>
    );
  }

  const noun = valid === 1 ? "row" : "rows";
  return (
    <section aria-labelledby="import-commit" className="alv-fu-form alv-import__step">
      <h2 id="import-commit">Step 4: commit</h2>
      {batch.commitOutcome === "Interrupted" && <p role="alert" className="alv-import__warning">The last commit was interrupted. What it already did is kept; confirming again does only what is left.</p>}
      <fieldset className="alv-import__group">
        <legend>What happens to rows that are not valid</legend>
        <label><input type="radio" name="import-policy" checked={policy === "CommitValidRows"} onChange={() => setChoice("CommitValidRows")} disabled={busy} /> Commit the valid rows only. The others stay in the lists above with their reasons.</label>
        <label><input type="radio" name="import-policy" checked={policy === "AllOrNothing"} onChange={() => setChoice("AllOrNothing")} disabled={busy || !!allOrNothingBlock} /> All or nothing. If any row stops being valid, nothing is written.</label>
        {allOrNothingBlock && <p className="alv-import__hint">{allOrNothingBlock}</p>}
      </fieldset>
      <p className="alv-import__summary">
        This will write <strong>{valid} {noun}</strong> to {balances ? "patients' billing ledgers" : batch.sourceKind === "Appointments" ? "the schedule" : "the patient records"}
        {balances && <>, a total of <strong>{money(batch.validAmount)}</strong></>}. It cannot be undone as a batch.
      </p>
      {needsBilling && <p className="alv-import__warning">Loading opening balances also needs the Manage billing permission, which your role does not hold.</p>}
      <label><input type="checkbox" checked={confirmed} onChange={(e) => setConfirmed(e.target.checked)} disabled={busy || needsBilling} /> I have checked the results{balances ? " and the total" : ""} and want to write these {valid} {noun}.</label>
      <div className="alv-fu-actions">
        <Button variant="primary" onClick={() => run(() => commitBatch(batch.id, policy, valid), true)} disabled={busy || valid === 0 || !confirmed || needsBilling}>
          {busy ? "Starting the commit…" : `Commit ${valid} ${noun}`}
        </Button>
      </div>
      {valid === 0 && <p>No row is valid, so there is nothing to commit.</p>}
      {error && <p role="alert" className="alv-fu-error">{error}</p>}
    </section>
  );
}
