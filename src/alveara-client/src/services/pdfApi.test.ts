import { describe, it, expect, vi, afterEach } from "vitest";
import { openPdf } from "./pdfApi";
import { ApiError } from "./authApi";

/** ALV-N026: the shared "open a server-made PDF" step - the tab opens at once, is filled only with a real PDF, and is closed again with the server's reason on a refusal. */
afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals(); });

function fakeTab() {
  return { opener: {} as unknown, location: { href: "" }, close: vi.fn() };
}

describe("openPdf", () => {
  it("fills a tab with no opener with the PDF the server returned", async () => {
    const tab = fakeTab();
    vi.spyOn(window, "open").mockReturnValue(tab as unknown as Window);
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(new Blob(["%PDF-1.7"]), { status: 200, headers: { "Content-Type": "application/pdf" } })));
    const createUrl = vi.fn().mockReturnValue("blob:pdf-1");
    vi.stubGlobal("URL", Object.assign(URL, { createObjectURL: createUrl, revokeObjectURL: vi.fn() }));
    await openPdf("/api/x/pdf");
    expect(tab.opener).toBeNull();
    expect(tab.location.href).toBe("blob:pdf-1");
    expect(tab.close).not.toHaveBeenCalled();
  });

  it("runs the prepare step after the tab is open and fetches the url it returns (ALV-N026 S6)", async () => {
    const tab = fakeTab();
    const opened = vi.spyOn(window, "open").mockReturnValue(tab as unknown as Window);
    const fetched = vi.fn().mockResolvedValue(new Response(new Blob(["%PDF-1.7"]), { status: 200, headers: { "Content-Type": "application/pdf" } }));
    vi.stubGlobal("fetch", fetched);
    vi.stubGlobal("URL", Object.assign(URL, { createObjectURL: vi.fn().mockReturnValue("blob:pdf-2"), revokeObjectURL: vi.fn() }));
    const prepare = vi.fn(async () => { expect(opened).toHaveBeenCalled(); return "/api/prescription-issues/i1/pdf"; });
    await openPdf(prepare);
    expect(fetched).toHaveBeenCalledWith("/api/prescription-issues/i1/pdf", { credentials: "include" });
    expect(tab.location.href).toBe("blob:pdf-2");
  });

  it("closes the tab and throws the server's reason when no PDF comes back", async () => {
    const tab = fakeTab();
    vi.spyOn(window, "open").mockReturnValue(tab as unknown as Window);
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify({ error: "pdf_engine_unavailable", message: "PDFs cannot be produced right now." }), { status: 503, headers: { "Content-Type": "application/json" } })));
    const failure = await openPdf("/api/x/pdf").catch((e: unknown) => e);
    expect(failure).toBeInstanceOf(ApiError);
    expect((failure as ApiError).code).toBe("pdf_engine_unavailable");
    expect((failure as ApiError).message).toBe("PDFs cannot be produced right now.");
    expect(tab.close).toHaveBeenCalled();
    expect(tab.location.href).toBe("");
  });
});
