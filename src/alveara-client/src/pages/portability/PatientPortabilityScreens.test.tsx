import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { axe } from "jest-axe";
import { ExportPatientButton } from "./ExportPatientButton";
import { PatientImportPage } from "./PatientImportPage";

// STORY-018: the export button and the import page against a small in-memory server that answers like the real API (stable error codes; the request body and headers recorded).

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
const err = (status: number, error: string, message = "server words", extra: Record<string, unknown> = {}) => json({ error, message, ...extra }, status);

type Call = { method: string; url: string; init?: RequestInit };
let calls: Call[];
let reply: (call: Call) => Response | null;

beforeEach(() => {
  calls = []; reply = () => null;
  vi.stubGlobal("fetch", vi.fn().mockImplementation((url: string, init?: RequestInit) => {
    const u = String(url);
    if (u.includes("csrf-token")) return Promise.resolve(json({ token: "t" }));
    const call = { method: init?.method ?? "GET", url: u, init };
    calls.push(call);
    return Promise.resolve(reply(call) ?? err(404, "not_found"));
  }));
});
afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks(); });

const exported = () => new Response(new Blob(['{"format":"alveara.patient-export"}'], { type: "application/json" }), { status: 200, headers: { "Content-Type": "application/json", "Content-Disposition": "attachment; filename=alveara-patient-abc123.json" } });
const patientFile = (name = "patient.json", size = 400) => new File([new Uint8Array(size).fill(32)], name, { type: "application/json" });
const importCalls = () => calls.filter((c) => c.url === "/api/patients/import");

describe("the export button", () => {
  let downloads: string[];
  beforeEach(() => {
    downloads = [];
    Object.defineProperty(URL, "createObjectURL", { configurable: true, writable: true, value: vi.fn(() => "blob:test") });
    Object.defineProperty(URL, "revokeObjectURL", { configurable: true, writable: true, value: vi.fn() });
    vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(function (this: HTMLAnchorElement) { downloads.push(this.download); });
  });

  it("downloads the patient's file under the name the server gave it and says the export was logged", async () => {
    reply = () => exported();
    const { container } = render(<ExportPatientButton patientId="p1" />);

    await userEvent.click(screen.getByRole("button", { name: "Export patient file" }));

    expect(await screen.findByText("Saved alveara-patient-abc123.json. The export was logged.")).toBeInTheDocument();
    expect(calls[0].url).toBe("/api/patients/p1/export");
    expect(calls[0].method).toBe("GET");
    expect(downloads).toEqual(["alveara-patient-abc123.json"]);
    expect(URL.createObjectURL).toHaveBeenCalledWith(expect.any(Blob));
    expect(URL.revokeObjectURL).toHaveBeenCalledWith("blob:test");                                    // the temporary link is tidied up
    expect(await axe(container)).toHaveNoViolations();
  });

  it("keeps the reason on the screen when the export is refused, and saves nothing", async () => {
    reply = () => err(409, "patient_merged");
    render(<ExportPatientButton patientId="p1" />);

    await userEvent.click(screen.getByRole("button", { name: "Export patient file" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("merged into another patient record and is no longer exported");
    expect(downloads).toEqual([]);
    expect(screen.getByRole("button", { name: "Export patient file" })).toBeEnabled();                 // and it can be tried again
  });

  it("says in words when it is not allowed and when the server fails", async () => {
    reply = () => err(403, "permission_denied");
    render(<ExportPatientButton patientId="p1" />);
    await userEvent.click(screen.getByRole("button", { name: "Export patient file" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("You do not have permission to do that.");

    reply = () => err(500, "internal_error");
    await userEvent.click(screen.getByRole("button", { name: "Export patient file" }));
    expect(await screen.findByText(/That did not work\. Try again/)).toBeInTheDocument();
    expect(downloads).toEqual([]);
  });
});

describe("the import page", () => {
  const renderPage = () => render(<MemoryRouter><PatientImportPage /></MemoryRouter>);
  const choose = async (file: File) => userEvent.upload(screen.getByLabelText(/Patient file/), file);
  const send = async () => userEvent.click(screen.getByRole("button", { name: "Import patient" }));

  it("asks for a file, keeps Import off until one is chosen, and has no accessibility violations", async () => {
    const { container } = renderPage();

    expect(screen.getByRole("heading", { name: "Import a patient" })).toBeInTheDocument();
    expect(screen.getByLabelText(/Patient file/)).toHaveAttribute("type", "file");
    expect(screen.getByRole("button", { name: "Import patient" })).toBeDisabled();
    await choose(patientFile());
    expect(screen.getByRole("button", { name: "Import patient" })).toBeEnabled();
    expect(await axe(container)).toHaveNoViolations();
  });

  it("sends the chosen file itself as the body with the CSRF token, then says the patient was imported and links to the record", async () => {
    reply = () => json({ patientId: "p9", created: true, exportId: "e1" }, 201);
    renderPage();
    const file = patientFile();

    await choose(file);
    await send();

    expect(await screen.findByText("The patient was imported.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open the patient" })).toHaveAttribute("href", "/patients/p9");
    const [call] = importCalls();
    expect(call.method).toBe("POST");
    expect(call.init?.body).toBe(file);
    expect((call.init?.headers as Record<string, string>)["X-CSRF-Token"]).toBe("t");
  });

  it("says when a file was imported before and nothing new was created", async () => {
    reply = () => json({ patientId: "p9", created: false, exportId: "e1" }, 200);
    renderPage();

    await choose(patientFile());
    await send();

    expect(await screen.findByText(/This file was imported before\. The patient it created is shown; nothing new was created\./)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open the patient" })).toHaveAttribute("href", "/patients/p9");
  });

  it("lists every problem of a rejected file with the place in the file, says nothing was stored, and offers another file", async () => {
    reply = () => err(400, "import_rejected", "The file was not imported. Correct the problems listed and send it again.", {
      problems: [
        { code: "unsupported_version", path: "formatVersion", message: "This system reads format version 1 only; the file is version 7." },
        { code: "field_invalid", path: "patient.phone", message: "Phone must contain at least 7 digits." },
        { code: "not_json", path: "$", message: "The file is not valid UTF-8 JSON." },
      ],
    });
    const { container } = renderPage();

    await choose(patientFile());
    await send();

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("The file was not imported");
    expect(alert).toHaveTextContent("Correct the problems listed and choose the file again. Nothing was stored.");
    const rows = within(screen.getByRole("table")).getAllByRole("row").slice(1).map((r) => within(r).getAllByRole("cell").map((c) => c.textContent));
    expect(rows).toEqual([
      ["formatVersion", "This system reads format version 1 only; the file is version 7."],
      ["patient.phone", "Phone must contain at least 7 digits."],
      ["The whole file", "The file is not valid UTF-8 JSON."],
    ]);
    expect(screen.queryByRole("link", { name: "Open the patient" })).not.toBeInTheDocument();
    expect(await axe(container)).toHaveNoViolations();

    await userEvent.click(screen.getByRole("button", { name: "Choose another file" }));
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Import patient" })).toBeDisabled();                      // back to the start
  });

  it("refuses a patient who is already registered and links to the existing record", async () => {
    reply = () => err(409, "duplicate_patient", "A patient with the same name and date of birth is already registered.", { existingPatientId: "p3", candidates: [] });
    renderPage();

    await choose(patientFile());
    await send();

    expect(await screen.findByRole("alert")).toHaveTextContent("already registered. Nothing was imported.");
    expect(screen.getByRole("link", { name: "Open the existing patient" })).toHaveAttribute("href", "/patients/p3");
  });

  it("links to the first candidate when the patient may be someone already registered", async () => {
    reply = () => err(409, "possible_duplicate", "These patients may be the same person.", { existingPatientId: null, candidates: [{ id: "p4" }, { id: "p5" }] });
    renderPage();

    await choose(patientFile());
    await send();

    expect(await screen.findByRole("alert")).toHaveTextContent("may be someone who is already registered. Nothing was imported; register the patient from the registration form");
    expect(screen.getByRole("link", { name: "Open the existing patient" })).toHaveAttribute("href", "/patients/p4");
  });

  it("refuses a file over the size limit without sending it", async () => {
    renderPage();

    await choose(patientFile("huge.json", 300 * 1024));
    await send();

    expect(await screen.findByRole("alert")).toHaveTextContent("larger than 256 KB");
    expect(importCalls()).toHaveLength(0);                                                              // nothing was sent
  });

  it("says in words when it is not allowed or the server fails, and the file can be sent again", async () => {
    reply = () => err(403, "permission_denied");
    renderPage();
    await choose(patientFile());
    await send();
    expect(await screen.findByRole("alert")).toHaveTextContent("You do not have permission to do that.");

    reply = () => err(500, "internal_error");
    await send();
    expect(await screen.findByText(/That did not work\. Try again/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Import patient" })).toBeEnabled();
  });
});
