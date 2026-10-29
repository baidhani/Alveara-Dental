import { useState } from "react";
import { Button } from "../../components/Button";
import { exportCsv, reportErrorMessage } from "../../services/reportsApi";
import type { ReportQuery } from "../../services/reportsApi";
import { saveFile } from "../../services/saveFile";

interface Props {
  /** The report's export route under /api/reports, without "/export" (for example "production" or "measurement/comparison"). */
  path: string;
  query: ReportQuery;
  extra?: Record<string, string | null | undefined>;
  /** What the download button says; the file is named by the server. */
  csvLabel?: string;
  /** The browser's own print dialog; only the main report prints (see reports.css). */
  showPrint?: boolean;
}

/**
 * Takes the report away as a CSV file or on paper. The file comes from the server, from the same report the screen shows, with the filter the screen was asked with (so its numbers are the screen's), and the server
 * checks the permission again and records the export; if it refuses, the reason is said in words and no file is made. Pressing the button twice makes two identical files and two records (an export is a read). Printing
 * uses the browser's print dialog on the report as it is on screen; the menu, the filter bar and these buttons are left off the paper.
 */
export function ReportActions({ path, query, extra, csvLabel = "Download CSV", showPrint = true }: Props) {
  const [busy, setBusy] = useState(false);
  const [saved, setSaved] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const download = async () => {
    setBusy(true); setSaved(null); setError(null);
    try {
      const file = await exportCsv(path, query, extra);
      saveFile(file.fileName, file.blob);
      setSaved(`Downloaded ${file.fileName}.`);
    } catch (e) {
      setError(reportErrorMessage(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="alv-rep__actions alv-rep__noprint">
      <Button onClick={() => void download()} disabled={busy}>{busy ? "Preparing the file…" : csvLabel}</Button>
      {showPrint && <Button onClick={() => window.print()}>Print</Button>}
      {saved && <p role="status" className="alv-rep__hint">{saved}</p>}
      {error && <p role="alert" className="alv-rep__problem">{error}</p>}
    </div>
  );
}
