import { ApiError } from "./authApi";

/**
 * ALV-N026: opens a server-produced PDF in a new tab - the browser prints that PDF, never the application page, so no address, title or blank sheet ends up on paper.
 * The tab is opened at once (so the browser does not block it) and filled only when the server has answered with a PDF; on a refusal it is closed and the server's message is thrown as an
 * ApiError for the screen to show (for example pdf_engine_unavailable or document_content_changed). The PDF is held as a temporary object URL in the browser, released after a minute.
 */
export async function openPdf(url: string): Promise<void> {
  const tab = window.open("", "_blank");
  if (tab) tab.opener = null;
  try {
    const res = await fetch(url, { credentials: "include" });
    if (!res.ok || !(res.headers.get("Content-Type") ?? "").startsWith("application/pdf")) {
      const body = (await res.json().catch(() => ({}))) as { error?: string; message?: string };
      throw new ApiError(res.status, body.error ?? "pdf_failed", body.message ?? "The document could not be produced as a PDF. Try again; if it keeps failing, ask the practice administrator to check System Status.", body as Record<string, unknown>);
    }
    const objectUrl = URL.createObjectURL(await res.blob());
    if (tab) tab.location.href = objectUrl; else window.location.assign(objectUrl);
    window.setTimeout(() => URL.revokeObjectURL(objectUrl), 60_000);
  } catch (err) {
    tab?.close();
    if (err instanceof ApiError) throw err;
    throw new ApiError(0, "network", "Could not reach the server, so the document was not opened. Check your connection and try again.");
  }
}
