import { useState } from "react";
import { Button } from "../../components/Button";
import { exportPatientFile, portabilityErrorMessage } from "../../services/patientPortabilityApi";

/** Saves a file the browser has downloaded into memory, through a short-lived link, and tidies up after itself. */
function saveFile(fileName: string, blob: Blob) {
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

/**
 * STORY-018: "Export patient file" on a patient's page. It downloads the patient's registration data as a portable JSON file; the server logs the export (who and when) before it sends the file. Shown only to people who
 * hold the export permission (the administrator by default). It says what happened in words and keeps a failure on the screen instead of leaving the person guessing.
 */
export function ExportPatientButton({ patientId }: { patientId: string }) {
  const [busy, setBusy] = useState(false);
  const [saved, setSaved] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const run = async () => {
    setBusy(true); setSaved(null); setError(null);
    try {
      const file = await exportPatientFile(patientId);
      saveFile(file.fileName, file.blob);
      setSaved(file.fileName);
    } catch (e) {
      setError(portabilityErrorMessage(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="alv-fu-actions">
      <Button onClick={run} disabled={busy}>{busy ? "Exporting…" : "Export patient file"}</Button>
      <p className="alv-clinical__status" role="status" aria-live="polite">{saved ? `Saved ${saved}. The export was logged.` : ""}</p>
      {error && <p role="alert" className="alv-fu-error">{error}</p>}
    </div>
  );
}
