import { useCallback, useEffect, useState } from "react";
import { getBatch, wasCancelled } from "../../services/bulkImportApi";
import type { ImportBatch } from "../../services/bulkImportApi";

export const POLL_MS = 3_000;
export const POLL_LIMIT_MS = 10 * 60 * 1000;

/**
 * One batch, kept current. While it is Committing the page asks again every few seconds, so the person sees it finish without pressing anything; it stops after ten minutes (a commit that long deserves a
 * look, and a forgotten tab should not keep asking) and offers a Refresh instead, and it stops the moment the person leaves the page. A failed read is shown, never left looking like an empty batch.
 */
export function useImportBatch(id: string) {
  const [batch, setBatch] = useState<ImportBatch | null>(null);
  const [failed, setFailed] = useState(false);
  const [pollExpired, setPollExpired] = useState(false);
  const [pollRun, setPollRun] = useState(0);

  const reload = useCallback(async (signal?: AbortSignal) => {
    try {
      setBatch(await getBatch(id, signal));
      setFailed(false);
    } catch (e) {
      if (!wasCancelled(e)) setFailed(true);
    }
  }, [id]);

  useEffect(() => {
    const controller = new AbortController();
    getBatch(id, controller.signal)
      .then((loaded) => { setBatch(loaded); setFailed(false); })
      .catch((e) => { if (!wasCancelled(e)) setFailed(true); });
    return () => controller.abort();
  }, [id]);

  const committing = batch?.status === "Committing";
  useEffect(() => {
    if (!committing) return;
    const started = Date.now();
    const timer = setInterval(() => {
      if (Date.now() - started >= POLL_LIMIT_MS) { clearInterval(timer); setPollExpired(true); return; }
      reload();
    }, POLL_MS);
    return () => clearInterval(timer);
  }, [committing, reload, pollRun]);

  const refresh = () => { setPollExpired(false); setPollRun((n) => n + 1); return reload(); };
  return { batch, setBatch, failed, reload: () => reload(), refresh, pollExpired: committing && pollExpired };
}
