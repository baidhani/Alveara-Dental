import { useState } from "react";
import { Button } from "../../components/Button";
import { ApiError } from "../../services/authApi";
import { newKey } from "../../services/clinicalApi";
import { openClinicalRecordCopy } from "../../services/patientExportsApi";

/**
 * ALV-N026 S8 (D4): "Clinical record copy (PDF)" on a patient's page, beside the readable-record download and shown to the same people (ExportPatientReadableRecord). Each click issues a new copy
 * "generated as of" now on the server and opens it as a PDF; an earlier copy is never re-made. The server records the issue and the print; a refusal is shown and nothing is printed.
 */
export function ClinicalRecordCopyButton({ patientId }: { patientId: string }) {
  const [busy, setBusy] = useState(false);
  const [status, setStatus] = useState<string | null>(null);

  const run = async () => {
    setBusy(true); setStatus(null);
    try {
      await openClinicalRecordCopy(patientId, newKey());
      setStatus("A clinical record copy dated now was issued and opened as a PDF.");
    } catch (err) {
      setStatus(`Nothing was printed. ${err instanceof ApiError ? err.message : "The connection dropped. Try again."}`);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="alv-fu-actions">
      <Button onClick={run} disabled={busy}>{busy ? "Preparing…" : "Clinical record copy (PDF)"}</Button>
      <p className="alv-clinical__status" role="status" aria-live="polite">{status ?? ""}</p>
    </div>
  );
}
