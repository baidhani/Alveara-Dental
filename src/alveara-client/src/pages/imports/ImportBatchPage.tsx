import { useParams } from "react-router-dom";
import { Button } from "../../components/Button";
import { PageHeader } from "../../components/PageHeader";
import { SafeLink } from "../../components/SafeLink";
import { ErrorState, LoadingState } from "../../components/StatePatterns";
import { formatMoment, formatSize } from "../exports/exportFormat";
import { CommitStep } from "./CommitStep";
import { DecisionQueue } from "./DecisionQueue";
import { DryRunStep } from "./DryRunStep";
import { kindLabel, statusOf } from "./importFormat";
import { MappingStep } from "./MappingStep";
import { ReconciliationPanel } from "./ReconciliationPanel";
import { useImportBatch } from "./useImportBatch";
import "../followups/followups.css";
import "./imports.css";

/**
 * ALV-018-C02: one batch as a short list of steps. The page follows the batch's state: a batch that is still open shows the mapping, the dry-run, the likely-duplicate queue (patients) and the commit; one that is
 * committing shows its progress (and a retry if it stopped); one that has committed shows what happened. The address names the batch, so reloading the page, or opening it from the batch list, resumes where it was.
 */
export function ImportBatchPage() {
  const { batchId = "" } = useParams();
  const { batch, setBatch, failed, reload, refresh, pollExpired } = useImportBatch(batchId);

  if (failed && !batch) return <ErrorState title="Could not load this batch" description="It may not exist, or you may not be allowed to see it." action={<Button onClick={reload}>Retry</Button>} />;
  if (!batch) return <LoadingState label="Loading the batch…" />;

  const state = statusOf(batch);
  const open = batch.status === "Uploaded" || batch.status === "DryRunComplete";
  const dryRun = batch.dryRunAtUtc !== null;
  const settled = batch.status === "Committed" || (batch.status === "DryRunComplete" && batch.commitOutcome !== null);

  return (
    <div className="alv-import">
      <PageHeader title={`${kindLabel(batch.sourceKind)}: ${batch.sourceLabel}`} description="Work through the steps in order. Nothing is written until you confirm the commit." />
      <p><SafeLink to="/admin/data-import">All batches</SafeLink></p>

      <section aria-labelledby="import-file" className="alv-fu-form alv-import__step">
        <h2 id="import-file">Step 1: the file</h2>
        <p>
          <span className={`alv-import__badge alv-import__badge--${state.tone}`}>{state.label}</span>{" "}
          Sent {formatMoment(batch.createdAtUtc, null)} by {batch.createdBy}. {batch.rowCount} rows, {batch.columnNames.length} columns, {formatSize(batch.fileSizeBytes)}.
        </p>
        <p>Columns: {batch.columnNames.join(", ")}. <span className="alv-import__note">Fingerprint <code title={batch.fileSha256}>{batch.fileSha256.slice(0, 12)}</code></span></p>
      </section>

      {open && <MappingStep batch={batch} onSaved={reload} />}
      {open && <DryRunStep batch={batch} onChanged={setBatch} />}
      {open && dryRun && batch.sourceKind === "Patients" && batch.status === "DryRunComplete" && <DecisionQueue batch={batch} onChanged={setBatch} />}
      {((batch.status === "DryRunComplete" && dryRun) || batch.status === "Committing") && (
        <CommitStep batch={batch} pollExpired={pollExpired} onChanged={setBatch} onReload={reload} onRefresh={refresh} />
      )}
      {settled && <ReconciliationPanel key={`${batch.status}|${batch.commitOutcome}|${batch.committedAtUtc}|${batch.commitRound}`} batch={batch} />}
    </div>
  );
}
