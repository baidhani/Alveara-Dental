import { useState } from "react";
import { Button } from "../../components/Button";
import { downloadErrorReport, importErrorMessage } from "../../services/bulkImportApi";
import { saveFile } from "../../services/saveFile";

/** Saves the row-level error report (a CSV of every rejected, skipped and unresolved row with its fixed message, never a value from the file). */
export function ErrorReportButton({ batchId }: { batchId: string }) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const download = async () => {
    setBusy(true); setError(null);
    try {
      const file = await downloadErrorReport(batchId);
      saveFile(file.fileName, file.blob);
    } catch (e) {
      setError(importErrorMessage(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <>
      <Button onClick={download} disabled={busy}>{busy ? "Preparing the report…" : "Download the error report (CSV)"}</Button>
      {error && <p role="alert" className="alv-fu-error">{error}</p>}
    </>
  );
}
