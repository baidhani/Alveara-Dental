import { useEffect, useState } from "react";
import { Button } from "../../components/Button";
import { LoadingState } from "../../components/StatePatterns";
import { getDocument } from "../../services/documentsApi";
import type { DocumentDetail } from "../../services/documentsApi";
import { formatDateTime, historyText } from "./documentText";

/**
 * ALV-010-C01: one document's history: every import, move between categories, edit and open with who and when, and every inactivation (with its reason) and reactivation. Read-only; nothing here changes a document.
 */
export function DocumentDetailsPanel({ documentId, title, onClose }: { documentId: string; title: string; onClose: () => void }) {
  const [detail, setDetail] = useState<DocumentDetail | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    getDocument(documentId, controller.signal).then(setDetail).catch(() => { if (!controller.signal.aborted) setFailed(true); });
    return () => controller.abort();
  }, [documentId]);

  return (
    <section className="alv-documents__details" aria-label={`Details of ${title}`}>
      <div className="alv-documents__actions">
        <h3 className="alv-documents__details-title">History of {title}</h3>
        <Button type="button" onClick={onClose}>Close details</Button>
      </div>
      {failed && <p className="alv-form-field__error" role="alert">Could not load the details</p>}
      {!failed && detail === null && <LoadingState label="Loading details…" />}
      {detail && (
        <>
          <ul aria-label="History" className="alv-documents__history">
            {detail.history.map((h) => <li key={h.eventNumber}>{historyText(h)}</li>)}
          </ul>
          {(detail.statusChanges ?? []).length > 0 && (
            <>
              <h4 className="alv-documents__details-title">Status changes</h4>
              <ul aria-label="Status changes" className="alv-documents__history">
                {(detail.statusChanges ?? []).map((c, i) => (
                  <li key={i}>{c.toStatus === "Inactive" ? `Made inactive: ${c.reason ?? ""}` : "Made active again"} (by {c.byName}, {formatDateTime(c.atUtc)})</li>
                ))}
              </ul>
            </>
          )}
        </>
      )}
    </section>
  );
}
