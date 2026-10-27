import { useState } from "react";
import { Button } from "../../components/Button";
import { downloadReadableRecord, exportErrorMessage } from "../../services/patientExportsApi";
import { saveFile } from "../../services/saveFile";

/**
 * ALV-018-C01: "Download readable record" on a patient's page. It downloads the patient's whole record as one page that opens in any browser and prints cleanly: no documents (they are listed by name; their files are
 * in the full package). The server logs the export (who and when) before it sends the page, and sends nothing if it cannot log it. Shown only to people who hold ExportPatientReadableRecord (the administrator by
 * default). It says what happened in words and keeps a failure on the screen.
 */
export function DownloadReadableRecordButton({ patientId }: { patientId: string }) {
  const [busy, setBusy] = useState(false);
  const [saved, setSaved] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const run = async () => {
    setBusy(true); setSaved(null); setError(null);
    try {
      const file = await downloadReadableRecord(patientId);
      saveFile(file.fileName, file.blob);
      setSaved(file.fileName);
    } catch (e) {
      setError(exportErrorMessage(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="alv-fu-actions">
      <Button onClick={run} disabled={busy}>{busy ? "Preparing…" : "Download readable record"}</Button>
      <p className="alv-clinical__status" role="status" aria-live="polite">{saved ? `Saved ${saved}. The export was logged.` : ""}</p>
      {error && <p role="alert" className="alv-fu-error">{error}</p>}
    </div>
  );
}
