import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { DownloadReadableRecordButton } from "./DownloadReadableRecordButton";

// ALV-018-C01: the "Download readable record" button on a patient's page, against a small in-memory server (stable error codes; the request recorded).

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

type Call = { method: string; url: string; init?: RequestInit };
let calls: Call[];
let reply: (call: Call) => Response | null;
let downloads: string[];

beforeEach(() => {
  calls = []; reply = () => null; downloads = [];
  vi.stubGlobal("fetch", vi.fn().mockImplementation((url: string, init?: RequestInit) => {
    const u = String(url);
    if (u.includes("csrf-token")) return Promise.resolve(json({ token: "t" }));
    const call = { method: init?.method ?? "GET", url: u, init };
    calls.push(call);
    return Promise.resolve(reply(call) ?? json({ error: "not_found", message: "nope" }, 404));
  }));
  Object.defineProperty(URL, "createObjectURL", { configurable: true, writable: true, value: vi.fn(() => "blob:test") });
  Object.defineProperty(URL, "revokeObjectURL", { configurable: true, writable: true, value: vi.fn() });
  vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(function (this: HTMLAnchorElement) { downloads.push(this.download); });
});
afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks(); });

const page = () => new Response(new Blob(["<html>"]), { status: 200, headers: { "Content-Type": "text/html", "Content-Disposition": "attachment; filename=alveara-readable-record-abc.html", "X-Export-Id": "e-1" } });

describe("the download readable record button", () => {
  it("downloads the page under the name the server gave it, with the CSRF token, and says the export was logged", async () => {
    reply = () => page();
    const { container } = render(<DownloadReadableRecordButton patientId="p1" />);

    await userEvent.click(screen.getByRole("button", { name: "Download readable record" }));

    expect(await screen.findByText("Saved alveara-readable-record-abc.html. The export was logged.")).toBeInTheDocument();
    expect(calls[0].url).toBe("/api/patient-exports/patients/p1/readable-record");
    expect(calls[0].method).toBe("POST");
    expect(new Headers(calls[0].init?.headers).get("X-CSRF-Token")).toBe("t");
    expect(downloads).toEqual(["alveara-readable-record-abc.html"]);
    expect(URL.revokeObjectURL).toHaveBeenCalledWith("blob:test");
    expect(await axe(container)).toHaveNoViolations();
  });

  it("is disabled while it works, so one click makes one export", async () => {
    let finish: (r: Response) => void = () => undefined;
    vi.mocked(fetch).mockImplementation((url) => String(url).includes("csrf-token") ? Promise.resolve(json({ token: "t" })) : new Promise<Response>((resolve) => { finish = resolve; }));
    render(<DownloadReadableRecordButton patientId="p1" />);

    await userEvent.click(screen.getByRole("button", { name: "Download readable record" }));
    expect(await screen.findByRole("button", { name: "Preparing…" })).toBeDisabled();
    finish(page());

    expect(await screen.findByRole("button", { name: "Download readable record" })).toBeEnabled();
    expect(downloads).toHaveLength(1);
  });

  it.each([
    [404, "patient_not_found", /not found/],
    [409, "patient_merged", /merged into another patient record/],
    [403, "permission_denied", /do not have permission/],
    [500, "export_failed", /nothing was delivered/],
  ])("keeps the reason on the screen and saves nothing when the server answers %s", async (status, code, words) => {
    reply = () => json({ error: code, message: "server words" }, status);
    render(<DownloadReadableRecordButton patientId="p1" />);

    await userEvent.click(screen.getByRole("button", { name: "Download readable record" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(words);
    expect(downloads).toEqual([]);
    expect(screen.queryByText(/Saved /)).not.toBeInTheDocument();
  });
});
