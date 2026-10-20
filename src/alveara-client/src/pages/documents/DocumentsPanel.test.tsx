import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { axe } from "jest-axe";
import { DocumentsPanel } from "./DocumentsPanel";
import { docOf, entryOf, json, patient, signedForm, stub } from "./documentsTestHarness";

/**
 * STORY-010: the Documents tab against a scripted API. The rules (types, sizes, duplicates, logging) are proven by the backend tests; what is proven here is the screen: who gets the import form and the edit
 * buttons, that the library lists documents and signed forms apart, that every wrong entry is named beside its box with what was typed kept, that a retry reuses its key and a new attempt does not, that a
 * duplicate is warned about and added only on an explicit choice, that an edit sends the version it read, and that only the types the server allows are offered to open in the browser.
 */
async function open(canImport = true) {
  const view = render(<MemoryRouter><DocumentsPanel patient={patient} canImport={canImport} /></MemoryRouter>);
  await screen.findByRole("heading", { name: "Documents" });
  await screen.findByText(/Loading documents/i).catch(() => null);
  await waitFor(() => expect(screen.queryByText(/Loading documents/i)).toBeNull());
  return view;
}

type User = ReturnType<typeof userEvent.setup>;
const importForm = () => within(screen.getByRole("form", { name: "Import a document" }));
const pick = (name = "scan.png", size = 1024, type = "image/png") => new File([new Uint8Array(Math.min(size, 16))], name, { type, lastModified: 1 }) as File;
async function chooseFile(user: User, file: File) {
  await user.upload(importForm().getByLabelText("File"), file);
}
async function fillImport(user: User, file = pick()) {
  await chooseFile(user, file);
  await user.selectOptions(importForm().getByLabelText("Category"), "Radiograph");
  await user.clear(importForm().getByLabelText("Date of document"));
  await user.type(importForm().getByLabelText("Date of document"), "2030-03-09");
  await user.type(importForm().getByLabelText("Description (optional)"), "Left side.");
}
const submit = (user: User) => user.click(importForm().getByRole("button", { name: "Import document" }));

afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks(); });

describe("the library", () => {
  it("lists documents and signed forms apart, newest first, with who imported and the size", async () => {
    stub({ entries: [entryOf(docOf()), signedForm()] });
    await open();
    const table = screen.getByRole("table", { name: "Documents" });
    const rows = within(table).getAllByRole("row").slice(1);
    expect(rows).toHaveLength(2);
    expect(within(rows[0]).getByText("bitewing.png")).toBeInTheDocument();
    expect(within(rows[0]).getByText("Radiograph")).toBeInTheDocument();
    expect(within(rows[0]).getByText("2 KB")).toBeInTheDocument();
    expect(within(rows[0]).getByText("Olive Office")).toBeInTheDocument();
    expect(within(rows[1]).getByText("Privacy notice")).toBeInTheDocument();
    expect(within(rows[1]).getByText("Signed form")).toBeInTheDocument();
    expect(within(rows[1]).getByRole("link", { name: "View form Privacy notice" })).toHaveAttribute("href", "/patients/p1/forms/f1");
    expect(within(rows[1]).queryByRole("button", { name: /Edit/ })).toBeNull();
    expect(within(rows[1]).queryByRole("link", { name: /Download/ })).toBeNull();
  });

  it("marks a voided signed form and a document added as a duplicate with words", async () => {
    stub({ entries: [entryOf(docOf({ duplicateOfDocumentId: "d0" })), signedForm({ isVoided: true })] });
    await open();
    expect(screen.getByText("Duplicate")).toBeInTheDocument();
    expect(screen.getByText("Voided")).toBeInTheDocument();
  });

  it("says plainly when there is nothing yet, and when it cannot be loaded", async () => {
    stub();
    const { unmount } = await open();
    expect(screen.getByText("No documents yet")).toBeInTheDocument();
    unmount();
    vi.unstubAllGlobals();
    stub({ loadFails: true });
    render(<MemoryRouter><DocumentsPanel patient={patient} canImport /></MemoryRouter>);
    expect(await screen.findByText("Could not load the documents")).toBeInTheDocument();
  });

  it("filters by category", async () => {
    const api = stub({ entries: [entryOf(docOf())] });
    const user = userEvent.setup();
    await open();
    const filters = within(screen.getByRole("search", { name: "Filter documents" }));
    await user.selectOptions(filters.getByLabelText("Category"), "Photo");
    await user.click(filters.getByRole("button", { name: "Apply filters" }));
    await waitFor(() => expect(api.lists().some((c) => c.url.includes("categoryId=c-photo"))).toBe(true));
  });

  it("gives staff who cannot import no import form and no edit buttons, but they can still open and download", async () => {
    stub({ entries: [entryOf(docOf())] });
    await open(false);
    expect(screen.queryByRole("form", { name: "Import a document" })).toBeNull();
    expect(screen.queryByRole("button", { name: /Edit/ })).toBeNull();
    expect(screen.getByRole("link", { name: "Download bitewing.png" })).toBeInTheDocument();
  });
});

describe("opening and downloading", () => {
  it("offers Open only for the types the server lets the browser show, and Download for all", async () => {
    stub({ entries: [
      entryOf(docOf({ id: "d-png", originalFileName: "a.png", detectedType: "png" })), entryOf(docOf({ id: "d-pdf", originalFileName: "b.pdf", detectedType: "pdf" })),
      entryOf(docOf({ id: "d-tif", originalFileName: "c.tif", detectedType: "tiff" })), entryOf(docOf({ id: "d-txt", originalFileName: "d.txt", detectedType: "other" })),
    ] });
    await open();
    expect(screen.getByRole("link", { name: "Open a.png" })).toHaveAttribute("href", "/api/documents/d-png/content");
    expect(screen.getByRole("link", { name: "Open b.pdf" })).toHaveAttribute("href", "/api/documents/d-pdf/content");
    expect(screen.queryByRole("link", { name: "Open c.tif" })).toBeNull();
    expect(screen.queryByRole("link", { name: "Open d.txt" })).toBeNull();
    expect(screen.getByRole("link", { name: "Download c.tif" })).toHaveAttribute("href", "/api/documents/d-tif/content?download=true");
    expect(screen.getByRole("link", { name: "Download d.txt" })).toHaveAttribute("href", "/api/documents/d-txt/content?download=true");
    expect(screen.getByRole("link", { name: "Open a.png" })).toHaveAttribute("rel", "noopener noreferrer");
  });
});

describe("importing", () => {
  it("shows the size limit, sends the file with its details, a CSRF token and a save key, then refreshes the list", async () => {
    const api = stub();
    const user = userEvent.setup();
    await open();
    expect(importForm().getByText(/Largest file: 100 MB/)).toBeInTheDocument();
    await fillImport(user);
    await submit(user);

    await waitFor(() => expect(api.imports()).toHaveLength(1));
    const call = api.imports()[0];
    expect(call.headers["X-CSRF-Token"]).toBe("csrf");
    expect(call.headers["Idempotency-Key"]).toMatch(/\S{8,}/);
    expect((call.form!.get("file") as File).name).toBe("scan.png");
    expect([call.form!.get("categoryId"), call.form!.get("documentDate"), call.form!.get("description")]).toEqual(["c-rad", "2030-03-09", "Left side."]);
    expect(await screen.findByRole("status")).toHaveTextContent("Imported new.png");
    await waitFor(() => expect(api.lists().length).toBeGreaterThanOrEqual(2));
    expect(importForm().getByLabelText("Description (optional)")).toHaveValue("");
  });

  it("offers only the categories in use", async () => {
    stub();
    await open();
    const options = within(importForm().getByLabelText("Category")).getAllByRole("option").map((o) => o.textContent);
    expect(options).toEqual(["Choose a category…", "Radiograph", "Photo"]);
  });

  it("refuses a file over the limit before sending anything", async () => {
    const api = stub({ max: 1024 * 1024 });
    const user = userEvent.setup();
    await open();
    const big = pick("huge.png");
    Object.defineProperty(big, "size", { value: 3 * 1024 * 1024 });
    await fillImport(user, big);
    await submit(user);
    expect(await importForm().findByText(/larger than the 1 MB limit/)).toBeInTheDocument();
    expect(api.imports()).toHaveLength(0);
  });

  it("names each wrong entry beside its box and keeps what was typed", async () => {
    stub({ import: () => json({ error: "metadata_invalid", message: "The document has entries that need correcting. Nothing was imported.", problems: [
      { field: "category", code: "inactive", message: "That category is no longer in use. Choose another." }, { field: "documentDate", code: "future", message: "The date of a document cannot be in the future." },
      { field: "description", code: "too_long", message: "The description can be at most 500 characters." }] }, 400) });
    const user = userEvent.setup();
    await open();
    await fillImport(user);
    await submit(user);
    expect(await importForm().findByText("That category is no longer in use. Choose another.")).toBeInTheDocument();
    expect(importForm().getByLabelText("Date of document")).toHaveAccessibleDescription("The date of a document cannot be in the future.");
    expect(importForm().getByLabelText("Description (optional)")).toHaveAccessibleDescription("The description can be at most 500 characters.");
    expect(importForm().getByLabelText("Date of document")).toHaveValue("2030-03-09");
    expect(importForm().getByLabelText("Description (optional)")).toHaveValue("Left side.");
    expect(importForm().getByLabelText("File")).toBeInTheDocument();
  });

  it.each([
    ["file_type_blocked", 400, "This type of file cannot be imported. Nothing was imported."],
    ["executable_content", 400, "This file is a program, which cannot be imported whatever it is called. Nothing was imported."],
    ["file_type_mismatch", 400, "The file is not the type its name says. Nothing was imported."],
    ["file_name_invalid", 400, "The file name cannot be used. Rename the file and try again. Nothing was imported."],
    ["file_empty", 400, "The file is empty. Nothing was imported."],
    ["file_too_large", 413, "The file is larger than the 100 MB limit. Nothing was imported."],
  ])("explains a %s refusal beside the file box", async (code, status, message) => {
    stub({ import: () => json({ error: code, message, problems: [] }, status) });
    const user = userEvent.setup();
    await open();
    await fillImport(user);
    await submit(user);
    expect(await importForm().findByText(message)).toBeInTheDocument();
    expect(importForm().getByLabelText("File")).toHaveAccessibleDescription(message);
  });

  it("says nothing was imported when the save fails, and the retry reuses the same key", async () => {
    let n = 0;
    const api = stub({ import: () => (++n === 1 ? json({ error: "save_failed", message: "The document could not be saved, so nothing was imported. Try again; if it keeps failing, contact support." }, 503) : json({ document: docOf({ id: "d-new", originalFileName: "new.png" }), replayed: false }, 201)) });
    const user = userEvent.setup();
    await open();
    await fillImport(user);
    await submit(user);
    expect(await screen.findByRole("alert")).toHaveTextContent("nothing was imported");
    await submit(user);
    await waitFor(() => expect(api.imports()).toHaveLength(2));
    expect(api.imports()[1].headers["Idempotency-Key"]).toBe(api.imports()[0].headers["Idempotency-Key"]);
    expect(await screen.findByRole("status")).toHaveTextContent("Imported");
  });

  it("uses a new key for the next import, and for a changed attempt", async () => {
    const api = stub();
    const user = userEvent.setup();
    await open();
    await fillImport(user);
    await submit(user);
    await screen.findByRole("status");
    await fillImport(user, pick("second.png"));
    await submit(user);
    await waitFor(() => expect(api.imports()).toHaveLength(2));
    expect(api.imports()[1].headers["Idempotency-Key"]).not.toBe(api.imports()[0].headers["Idempotency-Key"]);
  });

  it("does not send twice when the button is pressed twice", async () => {
    let release: () => void = () => {};
    const gate = new Promise<void>((r) => { release = r; });
    const api = stub({ import: async () => { await gate; return json({ document: docOf({ id: "d-new", originalFileName: "new.png" }), replayed: false }, 201); } });
    const user = userEvent.setup();
    await open();
    await fillImport(user);
    const button = importForm().getByRole("button", { name: "Import document" });
    await user.click(button);
    expect(importForm().getByRole("button", { name: "Importing…" })).toBeDisabled();
    await user.click(importForm().getByRole("button", { name: "Importing…" }));
    release();
    await screen.findByRole("status");
    expect(api.imports()).toHaveLength(1);
  });

  it("asks for a file, a category and a date before sending", async () => {
    const api = stub();
    const user = userEvent.setup();
    await open();
    await submit(user);
    expect(await importForm().findByText("Choose a file to import.")).toBeInTheDocument();
    expect(importForm().getByText("Choose a category.")).toBeInTheDocument();
    expect(api.imports()).toHaveLength(0);
  });
});

describe("duplicates", () => {
  const warn = () => json({ error: "duplicate_document", message: "This patient already has this file. Nothing was imported. Add it anyway only if you mean to.", problems: [],
    detail: { documentId: "d0", categoryName: "Radiograph", documentDate: "2030-02-01", importedByName: "Olive Office", importedAtUtc: "2030-02-02T10:00:00Z" } }, 409);

  it("warns, naming the existing document, and sends nothing more until the choice is made", async () => {
    const api = stub({ import: warn });
    const user = userEvent.setup();
    await open();
    await fillImport(user);
    await submit(user);
    const alert = await screen.findByRole("alert", { name: "This patient already has this file" });
    expect(alert).toHaveTextContent("Radiograph");
    expect(alert).toHaveTextContent("Olive Office");
    expect(within(alert).getByRole("button", { name: "Add anyway" })).toBeInTheDocument();
    expect(api.imports()).toHaveLength(1);
  });

  it("adds anyway by sending the same file again with the confirmation and the same key", async () => {
    let n = 0;
    const api = stub({ import: () => (++n === 1 ? warn() : json({ document: docOf({ id: "d-copy", originalFileName: "scan.png", duplicateOfDocumentId: "d0" }), replayed: false }, 201)) });
    const user = userEvent.setup();
    await open();
    await fillImport(user);
    await submit(user);
    await user.click(await screen.findByRole("button", { name: "Add anyway" }));
    await waitFor(() => expect(api.imports()).toHaveLength(2));
    expect(api.imports()[1].form!.get("confirmDuplicateOf")).toBe("d0");
    expect((api.imports()[1].form!.get("file") as File).name).toBe("scan.png");
    expect(api.imports()[1].headers["Idempotency-Key"]).toBe(api.imports()[0].headers["Idempotency-Key"]);
    expect(await screen.findByRole("status")).toHaveTextContent("Imported");
    expect(screen.queryByRole("button", { name: "Add anyway" })).toBeNull();
  });

  it("cancels without sending anything and keeps what was typed", async () => {
    const api = stub({ import: warn });
    const user = userEvent.setup();
    await open();
    await fillImport(user);
    await submit(user);
    await user.click(await screen.findByRole("button", { name: "Cancel" }));
    expect(screen.queryByRole("button", { name: "Add anyway" })).toBeNull();
    expect(api.imports()).toHaveLength(1);
    expect(importForm().getByLabelText("Description (optional)")).toHaveValue("Left side.");
  });
});

describe("editing", () => {
  async function startEdit(user: User) {
    await user.click(await screen.findByRole("button", { name: "Edit bitewing.png" }));
    return within(await screen.findByRole("form", { name: "Edit document" }));
  }

  it("sends the changes with the version that was read and refreshes the list", async () => {
    const api = stub({ entries: [entryOf(docOf())] });
    const user = userEvent.setup();
    await open();
    const form = await startEdit(user);
    expect(form.getByLabelText("Category")).toHaveValue("c-rad");
    await user.selectOptions(form.getByLabelText("Category"), "Photo");
    await user.clear(form.getByLabelText("Description (optional)"));
    await user.type(form.getByLabelText("Description (optional)"), "Retaken.");
    await user.click(form.getByRole("button", { name: "Save changes" }));
    await waitFor(() => expect(api.edits()).toHaveLength(1));
    expect(api.edits()[0].body).toEqual({ categoryId: "c-photo", documentDate: "2030-03-09", description: "Retaken.", rowVersion: "v1", encounterId: null, toothKey: null, regionKey: null });
    expect(api.edits()[0].headers["X-CSRF-Token"]).toBe("csrf");
    await waitFor(() => expect(screen.queryByRole("form", { name: "Edit document" })).toBeNull());
  });

  it("still lists a category that has since been deactivated, so the document can keep it", async () => {
    stub({ entries: [entryOf(docOf({ categoryId: "c-old", categoryName: "Other", categoryCode: "other" }))] });
    const user = userEvent.setup();
    await open();
    const form = await startEdit(user);
    expect(within(form.getByLabelText("Category")).getByRole("option", { name: "Other" })).toBeInTheDocument();
  });

  it("names a wrong entry beside its box and keeps the form open", async () => {
    stub({ entries: [entryOf(docOf())], edit: () => json({ error: "metadata_invalid", message: "x", problems: [{ field: "documentDate", code: "future", message: "The date of a document cannot be in the future." }] }, 400) });
    const user = userEvent.setup();
    await open();
    const form = await startEdit(user);
    await user.click(form.getByRole("button", { name: "Save changes" }));
    expect(await form.findByText("The date of a document cannot be in the future.")).toBeInTheDocument();
    expect(screen.getByRole("form", { name: "Edit document" })).toBeInTheDocument();
  });

  it("explains a change made by someone else and reloads the current version on request", async () => {
    const api = stub({ entries: [entryOf(docOf())], edit: () => json({ error: "concurrency_conflict", entityType: "PatientDocument", entityId: "d1" }, 409) });
    const user = userEvent.setup();
    await open();
    const form = await startEdit(user);
    await user.click(form.getByRole("button", { name: "Save changes" }));
    expect(await screen.findByText("Someone else changed this while you were editing")).toBeInTheDocument();
    const before = api.lists().length;
    await user.click(screen.getByRole("button", { name: "Reload current version" }));
    await waitFor(() => expect(api.lists().length).toBeGreaterThan(before));
  });

  it("cancels without sending anything", async () => {
    const api = stub({ entries: [entryOf(docOf())] });
    const user = userEvent.setup();
    await open();
    const form = await startEdit(user);
    await user.click(form.getByRole("button", { name: "Cancel" }));
    expect(screen.queryByRole("form", { name: "Edit document" })).toBeNull();
    expect(api.edits()).toHaveLength(0);
  });
});

describe("accessibility", () => {
  it("has no detectable violations with documents, the import form and an open edit form", async () => {
    stub({ entries: [entryOf(docOf()), signedForm()] });
    const user = userEvent.setup();
    const { container } = await open();
    await user.click(await screen.findByRole("button", { name: "Edit bitewing.png" }));
    await screen.findByRole("form", { name: "Edit document" });
    expect(await axe(container)).toHaveNoViolations();
  });
});
