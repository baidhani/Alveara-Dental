import { useCallback, useEffect, useState } from "react";
import { fetchExportHistory, wasCancelled } from "../../services/patientExportsApi";
import type { ExportHistoryItem } from "../../services/patientExportsApi";

/** The export history, newest first, a page at a time. A failed read is shown (with a retry), never left as an empty list that looks like "no exports". */
export function useExportHistory(pageSize = 25) {
  const [items, setItems] = useState<ExportHistoryItem[] | null>(null);
  const [failed, setFailed] = useState(false);
  const [hasMore, setHasMore] = useState(false);
  const [loadingMore, setLoadingMore] = useState(false);

  const refresh = useCallback(async (signal?: AbortSignal) => {
    try {
      const rows = await fetchExportHistory(pageSize, undefined, signal);
      setItems(rows); setHasMore(rows.length === pageSize); setFailed(false);
    } catch (e) {
      if (!wasCancelled(e)) setFailed(true);
    }
  }, [pageSize]);

  useEffect(() => {
    const controller = new AbortController();
    fetchExportHistory(pageSize, undefined, controller.signal)
      .then((rows) => { setItems(rows); setHasMore(rows.length === pageSize); setFailed(false); })
      .catch((e) => { if (!wasCancelled(e)) setFailed(true); });
    return () => controller.abort();
  }, [pageSize]);

  const loadOlder = async () => {
    if (!items?.length) return;
    setLoadingMore(true);
    try {
      const rows = await fetchExportHistory(pageSize, items[items.length - 1].requestedAtUtc);
      setItems([...items, ...rows]); setHasMore(rows.length === pageSize); setFailed(false);
    } catch {
      setFailed(true);                                                                   // the lines already shown stay; the retry button reloads the list
    } finally {
      setLoadingMore(false);
    }
  };

  return { items, failed, hasMore, loadingMore, refresh: () => refresh(), loadOlder };
}
