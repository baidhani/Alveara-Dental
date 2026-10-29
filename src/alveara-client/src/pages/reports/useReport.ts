import { useEffect, useState } from "react";
import { reportErrorMessage, wasCancelled } from "../../services/reportsApi";

export type Outcome<T> = { ok: true; data: T } | { ok: false; message: string };

/**
 * Loads one thing from the server and keeps the answer with the key it was asked for, so a screen is "loading" until the answer for ITS key has arrived (never showing the previous answer for a new question).
 * `again()` asks the same question once more. A request that is replaced or abandoned is cancelled and its answer is ignored.
 */
export function useReport<T>(fetcher: (signal: AbortSignal) => Promise<T>, questionKey: string): { outcome: Outcome<T> | null; again: () => void } {
  const [attempt, setAttempt] = useState(0);
  const [done, setDone] = useState<{ key: string; outcome: Outcome<T> } | null>(null);
  const key = `${questionKey}#${attempt}`;
  useEffect(() => {
    const controller = new AbortController();
    fetcher(controller.signal)
      .then((data) => setDone({ key, outcome: { ok: true, data } }))
      .catch((e) => { if (!wasCancelled(e)) setDone({ key, outcome: { ok: false, message: reportErrorMessage(e) } }); });
    return () => controller.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps -- the question key stands for everything the fetcher closes over
  }, [key]);
  return { outcome: done?.key === key ? done.outcome : null, again: () => setAttempt((n) => n + 1) };
}
