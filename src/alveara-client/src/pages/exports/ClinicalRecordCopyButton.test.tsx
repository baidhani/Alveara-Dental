import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { ClinicalRecordCopyButton } from "./ClinicalRecordCopyButton";

// ALV-N026 S8: the "Clinical record copy (PDF)" button: one POST (with the CSRF token and a fresh key) that records the issue and returns the PDF, opened in a new tab; a refusal is shown.

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

type Call = { method: string; url: string; init?: RequestInit };
let calls: Call[];
let reply: (call: Call) => Response | null;
let tab: { opener: unknown; location: { href: string }; close: ReturnType<typeof vi.fn> };

beforeEach(() => {
  calls = []; reply = () => null;
  tab = { opener: {}, location: { href: "" }, close: vi.fn() };
  vi.spyOn(window, "open").mockReturnValue(tab as unknown as Window);
  vi.stubGlobal("fetch", vi.fn().mockImplementation((url: string, init?: RequestInit) => {
    const u = String(url);
    if (u.includes("csrf-token")) return Promise.resolve(json({ token: "t" }));
    const call = { method: init?.method ?? "GET", url: u, init };
    calls.push(call);
    return Promise.resolve(reply(call) ?? json({ error: "not_found", message: "nope" }, 404));
  }));
  Object.defineProperty(URL, "createObjectURL", { configurable: true, writable: true, value: vi.fn(() => "blob:copy") });
  Object.defineProperty(URL, "revokeObjectURL", { configurable: true, writable: true, value: vi.fn() });
});
afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks(); });

const pdf = () => new Response(new Blob(["%PDF"]), { status: 200, headers: { "Content-Type": "application/pdf" } });

describe("the clinical record copy button", () => {
  it("issues a copy with the CSRF token and a new key on every click and opens the PDF in a new tab", async () => {
    reply = () => pdf();
    const { container } = render(<ClinicalRecordCopyButton patientId="p1" />);
    const button = screen.getByRole("button", { name: "Clinical record copy (PDF)" });

    await userEvent.click(button);
    expect(await screen.findByText("A clinical record copy dated now was issued and opened as a PDF.")).toBeInTheDocument();
    await userEvent.click(button);

    expect(calls.map((c) => [c.method, c.url])).toEqual([["POST", "/api/patients/p1/clinical-record-copies"], ["POST", "/api/patients/p1/clinical-record-copies"]]);
    expect((calls[0].init?.headers as Record<string, string>)["X-CSRF-Token"]).toBe("t");
    const keys = calls.map((c) => (JSON.parse(String(c.init?.body)) as { idempotencyKey: string }).idempotencyKey);
    expect(keys[0]).not.toBe(keys[1]);                                                          // each click is a new issue
    expect(tab.location.href).toBe("blob:copy");
    expect(await axe(container)).toHaveNoViolations();
  });

  it("shows the server's reason, closes the tab and prints nothing when refused", async () => {
    reply = () => json({ error: "patient_merged", message: "That record was merged into another patient record." }, 409);
    render(<ClinicalRecordCopyButton patientId="p1" />);
    await userEvent.click(screen.getByRole("button", { name: "Clinical record copy (PDF)" }));
    expect(await screen.findByText("Nothing was printed. That record was merged into another patient record.")).toBeInTheDocument();
    expect(tab.close).toHaveBeenCalled();
  });
});
