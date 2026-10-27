import { useEffect, useRef, useState } from "react";
import { Button } from "../../components/Button";
import { PageHeader } from "../../components/PageHeader";
import { PatientPicker } from "../../components/PatientPicker";
import { ErrorState, LoadingState } from "../../components/StatePatterns";
import { useAuth } from "../../contexts/AuthContext";
import {
  downloadAllPatientsPackage, downloadPatientPackage, downloadReadableRecord, exportErrorMessage, fetchExportOverview, wasCancelled,
} from "../../services/patientExportsApi";
import type { DownloadedExport, ExportOverview, FolderNaming, PackageOptions } from "../../services/patientExportsApi";
import type { PatientSummary } from "../../services/patientsApi";
import { saveFile } from "../../services/saveFile";
import { formatSize } from "./exportFormat";
import { ExportHistoryTable } from "./ExportHistoryTable";
import { ExportOverviewPanel } from "./ExportOverviewPanel";
import { useExportHistory } from "./useExportHistory";
import "../followups/followups.css";
import "./exports.css";

type Scope = "one" | "all";
type Format = "package" | "record";

/**
 * ALV-018-C01: "Export patient data". Takes one patient's records or every patient's out of Alveara as a package (one ZIP: a manifest, the data, a readable page per patient and, when chosen, the document files) or
 * one patient's readable record alone (one HTML page). Each choice is offered only to people who hold its permission. Before anything is created the screen says what a package holds and leaves out, whether the
 * current data fits the limits (the button is disabled with the reason when it does not), how an all-patients export reads the database, and, if folder names will carry patient names, that they will show in file
 * listings. While it runs the person can cancel; afterwards it says what was saved, that the export was logged, and, for an incomplete package, that it is incomplete and where to look. Every export, failed or not,
 * appears in the history with its result in words.
 */
export function ExportCenterPage() {
  const { state, hasPermission } = useAuth();
  const canPackage = hasPermission("ExportPatientData");
  const canAll = hasPermission("ExportAllPatientData");
  const canRecord = hasPermission("ExportPatientReadableRecord");
  const scopes: Scope[] = [...(canPackage || canRecord ? ["one" as const] : []), ...(canAll ? ["all" as const] : [])];

  const [overview, setOverview] = useState<ExportOverview | null>(null);
  const [overviewFailed, setOverviewFailed] = useState(false);
  const [scopeChoice, setScopeChoice] = useState<Scope>("one");
  const [formatChoice, setFormatChoice] = useState<Format>("package");
  const [patient, setPatient] = useState<PatientSummary | null>(null);
  const [includeDocuments, setIncludeDocuments] = useState(true);
  const [naming, setNaming] = useState<FolderNaming | null>(null);
  const [busy, setBusy] = useState(false);
  const [outcome, setOutcome] = useState<DownloadedExport | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const running = useRef<AbortController | null>(null);
  const history = useExportHistory();

  useEffect(() => {
    const controller = new AbortController();
    fetchExportOverview(controller.signal).then(setOverview).catch((e) => { if (!wasCancelled(e)) setOverviewFailed(true); });
    return () => controller.abort();
  }, []);

  const scope: Scope = scopes.includes(scopeChoice) ? scopeChoice : (scopes[0] ?? "one");
  const format: Format = scope === "all" ? "package" : formatChoice === "record" && canRecord ? "record" : canPackage ? "package" : "record";
  const isPackage = format === "package";
  const effectiveNaming = naming ?? overview?.defaultFolderNaming ?? "IdOnly";
  const patientsOver = scope === "all" && !!overview && overview.patientCount > overview.maxPatients;
  const documentsOver = scope === "all" && isPackage && includeDocuments && !!overview && overview.documentBytes > overview.maxPackageBytes;
  const cannotStart = busy || (scope === "one" && !patient) || patientsOver || documentsOver || scopes.length === 0;
  const refreshHistory = () => history.refresh().catch(() => undefined);

  const start = async () => {
    const controller = new AbortController();
    running.current = controller;
    setBusy(true); setOutcome(null); setNotice(null); setError(null);
    const options: PackageOptions = { includeDocumentFiles: includeDocuments, folderNaming: naming ?? undefined };
    try {
      const file = scope === "all" ? await downloadAllPatientsPackage(options, controller.signal)
        : format === "record" ? await downloadReadableRecord(patient!.id, controller.signal)
        : await downloadPatientPackage(patient!.id, options, controller.signal);
      saveFile(file.fileName, file.blob);
      setOutcome(file);
    } catch (e) {
      if (wasCancelled(e)) setNotice("Cancelled. Nothing was saved. The history shows how the server recorded it.");
      else setError(exportErrorMessage(e));
    } finally {
      running.current = null;
      setBusy(false);
      refreshHistory();                                                                  // a failed export leaves a Failed line too; a failed refresh shows its own retry
    }
  };

  if (state.kind === "loading") return <LoadingState label="Checking your permissions…" />;

  const label = scope === "all" ? "Create package for all patients" : format === "record" ? "Download readable record" : "Create package";

  return (
    <div className="alv-export">
      <PageHeader title="Export patient data" description="Take patient records out of Alveara as a file you can keep or move. Every export is logged with who made it and when." />

      {overviewFailed && <ErrorState title="Could not load the export details" description="Reload the page to try again." />}
      {!overview && !overviewFailed && <LoadingState label="Loading the export details…" />}
      {scopes.length === 0 && <p>You do not hold any export permission.</p>}

      {overview && scopes.length > 0 && (
        <section aria-labelledby="export-start" className="alv-fu-form alv-export__form" aria-busy={busy}>
          <h2 id="export-start">Start an export</h2>

          {scopes.length > 1 && (
            <fieldset className="alv-export__group">
              <legend>Whose records</legend>
              <label><input type="radio" name="export-scope" checked={scope === "one"} onChange={() => setScopeChoice("one")} disabled={busy} /> One patient</label>
              <label><input type="radio" name="export-scope" checked={scope === "all"} onChange={() => setScopeChoice("all")} disabled={busy} /> All patients</label>
            </fieldset>
          )}

          {scope === "one" && (
            <>
              <PatientPicker label="Patient" selected={patient} onSelect={setPatient} disabled={busy} />
              {canPackage && canRecord && (
                <fieldset className="alv-export__group">
                  <legend>What to download</legend>
                  <label><input type="radio" name="export-format" checked={format === "package"} onChange={() => setFormatChoice("package")} disabled={busy} /> Complete package (one ZIP file)</label>
                  <label><input type="radio" name="export-format" checked={format === "record"} onChange={() => setFormatChoice("record")} disabled={busy} /> Readable record only (one page, no documents)</label>
                </fieldset>
              )}
            </>
          )}

          {scope === "all" && (
            <div className="alv-export__facts">
              <p>{overview.patientCount} patients, with {formatSize(overview.documentBytes)} of document files. One export may carry up to {overview.maxPatients} patients and {formatSize(overview.maxPackageBytes)}.</p>
              <p>{overview.allPatientsReadMode === "Snapshot"
                ? "The export reads the whole database as it was at one moment."
                : "The export reads the database one table after another, so a change made while it runs may be only partly included. Run it at a quiet time."}</p>
              {patientsOver && <p role="alert" className="alv-fu-error">There are more patients than one export may carry, so this export cannot be created. Export patients one at a time, or ask an administrator to raise the limit.</p>}
              {documentsOver && <p role="alert" className="alv-fu-error">The document files come to more than one package may carry. Turn off “Include document files” to continue.</p>}
            </div>
          )}

          {isPackage && (
            <div className="alv-export__options">
              <label><input type="checkbox" checked={includeDocuments} onChange={(e) => setIncludeDocuments(e.target.checked)} disabled={busy} /> Include document files (scans, images, letters)</label>
              <label htmlFor="export-naming">Patient folder names</label>
              <select id="export-naming" value={effectiveNaming} onChange={(e) => setNaming(e.target.value as FolderNaming)} disabled={busy}>
                <option value="IdOnly">Patient id only (recommended)</option>
                <option value="NameAndId">Patient name and id</option>
              </select>
              {effectiveNaming === "NameAndId" && <p className="alv-export__warning">Folder names will contain patient names. Anyone who can see the file listing, a backup log or an email preview will see them.</p>}
            </div>
          )}

          <ExportOverviewPanel overview={overview} />

          <div className="alv-fu-actions">
            <Button variant="primary" onClick={start} disabled={cannotStart}>{busy ? "Creating…" : label}</Button>
            {busy && <Button onClick={() => running.current?.abort()}>Cancel</Button>}
          </div>
        </section>
      )}

      <p className="alv-clinical__status" role="status" aria-live="polite">
        {outcome ? `Saved ${outcome.fileName}. The export was logged (export ${outcome.exportId}).` : notice ?? ""}
      </p>
      {outcome?.result === "Partial" && (
        <div role="alert" className="alv-export__partial">
          <p><strong>This package is incomplete.</strong> {outcome.problems} {outcome.problems === 1 ? "thing" : "things"} could not be included. Open <code>manifest.json</code> inside the package and read its “problems” list before relying on it.</p>
        </div>
      )}
      {error && <p role="alert" className="alv-fu-error">{error}</p>}

      <section aria-labelledby="export-history" className="alv-export__history">
        <div className="alv-fu-toolbar">
          <h2 id="export-history">Export history</h2>
          <Button onClick={refreshHistory}>Refresh</Button>
        </div>
        <p>{canAll ? "You see everyone's exports." : "You see the exports you made."}{overview ? ` Times are in the practice time zone (${overview.practiceTimeZone}).` : ""}</p>
        {history.failed && <ErrorState title="Could not load the export history" action={<Button onClick={refreshHistory}>Retry</Button>} />}
        {!history.items && !history.failed && <LoadingState label="Loading the export history…" />}
        {history.items?.length === 0 && <p>No exports yet.</p>}
        {!!history.items?.length && <ExportHistoryTable items={history.items} timeZone={overview?.practiceTimeZone ?? null} canOpenPatients={hasPermission("ViewPatientRecords")} />}
        {history.hasMore && <div className="alv-fu-actions"><Button onClick={history.loadOlder} disabled={history.loadingMore}>{history.loadingMore ? "Loading…" : "Load older exports"}</Button></div>}
      </section>
    </div>
  );
}
