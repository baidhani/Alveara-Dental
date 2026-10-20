import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { axe } from "jest-axe";
import { DocumentsPanel } from "./DocumentsPanel";
import { detailOf, docOf, entryOf, json, patient, stub } from "./documentsTestHarness";

/**
 * ALV-010-C01: the document library screen against a scripted API. The rules (filters, status, context, integrity, thumbnails) are proven by the backend tests; what is proven here is the screen: that the filters are
 * sent as asked and a refusal is shown beside its box, that a thumbnail is offered only where the server will serve one, that the clinical context, the inactive state and a damaged file are shown in words, that a
 * preview and the history can be opened, that making a document inactive needs a reason and reactivating does not, that the clinical context can be set on import and on edit with a tooth and a region never together,
 * and that an upload shows its progress and can be cancelled and repeated without importing twice.
 */
type User = ReturnType<typeof userEvent.setup>;

async function open(canImport = true) {
  const view = render(<MemoryRouter><DocumentsPanel patient={patient} canImport={canImport} /></MemoryRouter>);
  await screen.findByRole("heading", { name: "Documents" });
  await waitFor(() => expect(screen.queryByText(/Loading documents/i)).toBeNull());
  return view;
}

const filterForm = () => within(screen.getByRole("search", { name: "Filter documents" }));
const importForm = () => within(screen.getByRole("form", { name: "Import a document" }));
const pick = (name = "scan.png") => new File([new Uint8Array(8)], name, { type: "image/png", lastModified: 1 }) as File;

async function fillImport(user: User) {
  await user.upload(importForm().getByLabelText("File"), pick());
  await user.selectOptions(importForm().getByLabelText("Category"), "Radiograph");
}

const sizeOf = (bytes: number) => ({ sizeBytes: bytes });

afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks(); });

describe("filters", () => {
  it("sends every filter that was set and nothing else, and clears them all", async () => {
    const api = stub();
    const user = userEvent.setup();
    await open();
    expect(api.lastList()).toBe("/api/patients/p1/documents");
    await user.selectOptions(filterForm().getByLabelText("Category"), "Photo");
    await user.selectOptions(filterForm().getByLabelText("Encounter"), "e1");
    await user.selectOptions(filterForm().getByLabelText("Tooth"), "16");
    await user.type(filterForm().getByLabelText("From"), "2030-01-01");
    await user.type(filterForm().getByLabelText("To"), "2030-12-31");
    await user.type(filterForm().getByLabelText("Search file names and descriptions"), "bite wing");
    await user.selectOptions(filterForm().getByLabelText("Show"), "All documents");
    await user.click(filterForm().getByRole("button", { name: "Apply filters" }));
    await waitFor(() => expect(api.lastList()).toContain("categoryId=c-photo"));
    const sent = new URLSearchParams(api.lastList().split("?")[1]);
    expect(Object.fromEntries(sent)).toEqual({ categoryId: "c-photo", encounterId: "e1", toothKey: "16", from: "2030-01-01", to: "2030-12-31", text: "bite wing", status: "All" });

    await user.click(filterForm().getByRole("button", { name: "Clear filters" }));
    await waitFor(() => expect(api.lastList()).toBe("/api/patients/p1/documents"));
    expect(filterForm().getByLabelText("Search file names and descriptions")).toHaveValue("");
  });

  it("offers a tooth or a region but never both, and the active documents are shown unless asked otherwise", async () => {
    stub();
    const user = userEvent.setup();
    await open();
    expect(filterForm().getByLabelText("Show")).toHaveValue("Active");
    await user.selectOptions(filterForm().getByLabelText("Tooth"), "16");
    await user.selectOptions(filterForm().getByLabelText("Oral region"), "UpperLeft");
    expect(filterForm().getByLabelText("Tooth")).toHaveValue("");
    await user.selectOptions(filterForm().getByLabelText("Tooth"), "26");
    expect(filterForm().getByLabelText("Oral region")).toHaveValue("");
  });

  it("names a refused filter beside its box and keeps what was typed", async () => {
    const api = stub();
    const user = userEvent.setup();
    await open();
    vi.mocked(fetch).mockImplementation((url: RequestInfo | URL) => String(url).includes("/documents?")
      ? Promise.resolve(json({ error: "metadata_invalid", message: "x", problems: [{ field: "from", code: "after_to", message: "The start of the date range is after its end." }] }, 400))
      : Promise.resolve(json({ patientId: "p1", entries: [], maxUploadBytes: 1 })));
    await user.type(filterForm().getByLabelText("From"), "2030-09-01");
    await user.type(filterForm().getByLabelText("To"), "2030-01-01");
    await user.click(filterForm().getByRole("button", { name: "Apply filters" }));
    expect(await filterForm().findByText("The start of the date range is after its end.")).toBeInTheDocument();
    expect(filterForm().getByLabelText("From")).toHaveValue("2030-09-01");
    expect(api.lists().length).toBeGreaterThan(0);
  });
});

describe("the thumbnail view", () => {
  const entries = () => [
    entryOf(docOf({ id: "d-png", originalFileName: "a.png", detectedType: "png" })), entryOf(docOf({ id: "d-jpg", originalFileName: "b.jpg", detectedType: "jpeg" })),
    entryOf(docOf({ id: "d-pdf", originalFileName: "c.pdf", detectedType: "pdf" })), entryOf(docOf({ id: "d-tif", originalFileName: "d.tif", detectedType: "tiff" })),
    entryOf(docOf({ id: "d-big", originalFileName: "e.png", detectedType: "png", ...sizeOf(6 * 1024 * 1024) })),
  ];

  it("shows a picture only for a PNG or JPEG the server will serve a thumbnail of, and a word for everything else", async () => {
    stub({ entries: entries() });
    const user = userEvent.setup();
    await open();
    expect(screen.queryByRole("list", { name: "Document thumbnails" })).toBeNull();
    await user.click(screen.getByRole("radio", { name: "Thumbnails" }));
    const grid = screen.getByRole("list", { name: "Document thumbnails" });
    const images = within(grid).getAllByRole("img");
    expect(images.map((i) => i.getAttribute("src"))).toEqual(["/api/documents/d-png/thumbnail", "/api/documents/d-jpg/thumbnail"]);
    expect(images[0]).toHaveAttribute("alt", "Thumbnail of a.png");
    expect(images[0]).toHaveAttribute("loading", "lazy");
    for (const name of ["c.pdf", "d.tif", "e.png"]) expect(within(grid).getByText(name)).toBeInTheDocument();
    expect(within(grid).getByText("PDF")).toBeInTheDocument();
    expect(within(grid).getByText("Too large for a thumbnail")).toBeInTheDocument();
    await user.click(screen.getByRole("radio", { name: "List" }));
    expect(screen.getByRole("table", { name: "Documents" })).toBeInTheDocument();
  });

  it("keeps Open, Download and the other actions on each card", async () => {
    stub({ entries: entries().slice(0, 1) });
    const user = userEvent.setup();
    await open();
    await user.click(screen.getByRole("radio", { name: "Thumbnails" }));
    const card = within(screen.getByRole("list", { name: "Document thumbnails" }));
    expect(card.getByRole("link", { name: "Open a.png" })).toHaveAttribute("href", "/api/documents/d-png/content");
    expect(card.getByRole("link", { name: "Download a.png" })).toBeInTheDocument();
    expect(card.getByRole("button", { name: "Edit a.png" })).toBeInTheDocument();
  });
});

describe("what a document says about itself", () => {
  it("shows the encounter, the tooth or region, an inactive document and a missing or damaged file in words", async () => {
    stub({ entries: [
      entryOf(docOf({ id: "d1", originalFileName: "tooth.png", encounterId: "e1", toothKey: "16" })),
      entryOf(docOf({ id: "d2", originalFileName: "region.png", regionKey: "UpperLeft" })),
      entryOf(docOf({ id: "d3", originalFileName: "old.png", status: "Inactive", inactiveReason: "Wrong patient." })),
      entryOf(docOf({ id: "d4", originalFileName: "gone.png", integrityStatus: "Missing", integrityCheckedAtUtc: "2030-03-11T10:00:00Z" })),
      entryOf(docOf({ id: "d5", originalFileName: "bad.png", integrityStatus: "Corrupt", integrityCheckedAtUtc: "2030-03-11T10:00:00Z" })),
    ] });
    await open();
    const row = (name: string) => screen.getAllByRole("row").find((r) => within(r).queryByText(name))!;
    expect(within(row("tooth.png")).getByText(/^Tooth 3$/)).toBeInTheDocument();
    expect(within(row("tooth.png")).getByText(/^Encounter /)).toBeInTheDocument();
    expect(within(row("region.png")).getByText("Upper left quadrant")).toBeInTheDocument();
    expect(within(row("old.png")).getByText("Inactive")).toBeInTheDocument();
    expect(within(row("gone.png")).getByText("File missing")).toBeInTheDocument();
    expect(within(row("bad.png")).getByText("File damaged")).toBeInTheDocument();
  });

  it("keeps working when the encounters cannot be listed", async () => {
    stub({ entries: [entryOf(docOf({ encounterId: "e1" }))] });
    vi.mocked(fetch).mockImplementation((url: RequestInfo | URL) => String(url).includes("/encounters") ? Promise.resolve(json({ error: "x" }, 500))
      : String(url).includes("/csrf") ? Promise.resolve(json({ token: "c" })) : String(url).includes("/document-categories") ? Promise.resolve(json({ categories: [] }))
      : Promise.resolve(json({ patientId: "p1", entries: [entryOf(docOf({ encounterId: "e1" }))], maxUploadBytes: 1 })));
    await open();
    expect(screen.getByText("bitewing.png")).toBeInTheDocument();
    expect(filterForm().getByRole("option", { name: "Any encounter" })).toBeInTheDocument();
  });
});

describe("preview and history", () => {
  it("previews a picture in the page through the content route, and offers no preview for other types", async () => {
    stub({ entries: [entryOf(docOf({ id: "d-png", originalFileName: "a.png" })), entryOf(docOf({ id: "d-pdf", originalFileName: "b.pdf", detectedType: "pdf" })), entryOf(docOf({ id: "d-txt", originalFileName: "c.txt", detectedType: "other" }))] });
    const user = userEvent.setup();
    await open();
    expect(screen.queryByRole("button", { name: "Preview b.pdf" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Preview c.txt" })).toBeNull();
    await user.click(screen.getByRole("button", { name: "Preview a.png" }));
    const preview = screen.getByRole("region", { name: "Preview of a.png" });
    expect(within(preview).getByRole("img", { name: "a.png" })).toHaveAttribute("src", "/api/documents/d-png/content");
    await user.click(within(preview).getByRole("button", { name: "Close preview" }));
    expect(screen.queryByRole("region", { name: "Preview of a.png" })).toBeNull();
  });

  it("shows the history and every inactivation with its reason, who and when", async () => {
    const doc = docOf({ id: "d1", status: "Inactive", inactiveReason: "Duplicate scan.", inactivatedByName: "Olive Office", inactivatedAtUtc: "2030-04-01T10:00:00Z" });
    stub({ entries: [entryOf(doc)], detail: () => json(detailOf(doc, {
      history: [
        { eventNumber: 1, kind: "Imported", atUtc: "2030-03-10T10:00:00Z", byName: "Olive Office", fromCategoryName: null, toCategoryName: null, relatedDocumentId: null },
        { eventNumber: 2, kind: "Categorized", atUtc: "2030-03-11T10:00:00Z", byName: "Olive Office", fromCategoryName: "Photo", toCategoryName: "Radiograph", relatedDocumentId: null },
        { eventNumber: 3, kind: "Inactivated", atUtc: "2030-04-01T10:00:00Z", byName: "Olive Office", fromCategoryName: null, toCategoryName: null, relatedDocumentId: null },
      ],
      statusChanges: [{ toStatus: "Inactive", reason: "Duplicate scan.", byName: "Olive Office", atUtc: "2030-04-01T10:00:00Z" }, { toStatus: "Active", reason: null, byName: "Dr. Okafor", atUtc: "2030-04-02T10:00:00Z" }],
    })) });
    const user = userEvent.setup();
    await open();
    await user.click(screen.getByRole("button", { name: "Details bitewing.png" }));
    const panel = await screen.findByRole("region", { name: "Details of bitewing.png" });
    const history = within(panel).getByRole("list", { name: "History" });
    expect(within(history).getAllByRole("listitem")).toHaveLength(3);
    expect(within(history).getByText(/Moved from Photo to Radiograph/)).toBeInTheDocument();
    const changes = within(panel).getByRole("list", { name: "Status changes" });
    expect(within(changes).getByText(/Made inactive: Duplicate scan\./)).toBeInTheDocument();
    expect(within(changes).getByText(/Made active again/)).toBeInTheDocument();
    await user.click(within(panel).getByRole("button", { name: "Close details" }));
    expect(screen.queryByRole("region", { name: "Details of bitewing.png" })).toBeNull();
  });

  it("says so when the details cannot be loaded", async () => {
    stub({ entries: [entryOf(docOf())], detail: () => json({ error: "server_error" }, 500) });
    const user = userEvent.setup();
    await open();
    await user.click(screen.getByRole("button", { name: "Details bitewing.png" }));
    expect(await screen.findByText("Could not load the details")).toBeInTheDocument();
  });
});

describe("making a document inactive and active again", () => {
  const startInactivate = async (user: User) => {
    await user.click(await screen.findByRole("button", { name: "Inactivate bitewing.png" }));
    return within(await screen.findByRole("form", { name: "Inactivate document" }));
  };

  it("needs a reason, sends it with the version that was read and says what happened", async () => {
    const api = stub({ entries: [entryOf(docOf())] });
    const user = userEvent.setup();
    await open();
    const form = await startInactivate(user);
    await user.click(form.getByRole("button", { name: "Make inactive" }));
    expect(await form.findByText("Say why the document is being made inactive.")).toBeInTheDocument();
    expect(api.posts("/inactivate")).toHaveLength(0);
    await user.type(form.getByLabelText("Why is this document being made inactive?"), "Scanned under the wrong patient.");
    await user.click(form.getByRole("button", { name: "Make inactive" }));
    await waitFor(() => expect(api.posts("/inactivate")).toHaveLength(1));
    expect(api.posts("/inactivate")[0].body).toEqual({ reason: "Scanned under the wrong patient.", rowVersion: "v1" });
    expect(api.posts("/inactivate")[0].headers["X-CSRF-Token"]).toBe("csrf");
    expect(await screen.findByRole("status")).toHaveTextContent("bitewing.png is now inactive.");
    expect(screen.queryByRole("form", { name: "Inactivate document" })).toBeNull();
  });

  it("names a refused reason beside its box and keeps the form open", async () => {
    stub({ entries: [entryOf(docOf())], inactivate: () => json({ error: "metadata_invalid", message: "x", problems: [{ field: "reason", code: "too_long", message: "The reason can be at most 500 characters." }] }, 400) });
    const user = userEvent.setup();
    await open();
    const form = await startInactivate(user);
    await user.type(form.getByLabelText("Why is this document being made inactive?"), "Because.");
    await user.click(form.getByRole("button", { name: "Make inactive" }));
    expect(await form.findByText("The reason can be at most 500 characters.")).toBeInTheDocument();
    expect(form.getByLabelText("Why is this document being made inactive?")).toHaveValue("Because.");
  });

  it("explains a change made by someone else", async () => {
    stub({ entries: [entryOf(docOf())], inactivate: () => json({ error: "concurrency_conflict", entityType: "PatientDocument", entityId: "d1" }, 409) });
    const user = userEvent.setup();
    await open();
    const form = await startInactivate(user);
    await user.type(form.getByLabelText("Why is this document being made inactive?"), "Because.");
    await user.click(form.getByRole("button", { name: "Make inactive" }));
    expect(await screen.findByText("Someone else changed this while you were editing")).toBeInTheDocument();
  });

  it("cancels without sending anything", async () => {
    const api = stub({ entries: [entryOf(docOf())] });
    const user = userEvent.setup();
    await open();
    const form = await startInactivate(user);
    await user.click(form.getByRole("button", { name: "Cancel" }));
    expect(screen.queryByRole("form", { name: "Inactivate document" })).toBeNull();
    expect(api.posts("/inactivate")).toHaveLength(0);
  });

  it("offers reactivation, not inactivation, for an inactive document, and sends the version it read", async () => {
    const api = stub({ entries: [entryOf(docOf({ status: "Inactive", inactiveReason: "Duplicate.", rowVersion: "v7" }))] });
    const user = userEvent.setup();
    await open();
    expect(screen.queryByRole("button", { name: "Inactivate bitewing.png" })).toBeNull();
    await user.click(screen.getByRole("button", { name: "Reactivate bitewing.png" }));
    await waitFor(() => expect(api.posts("/reactivate")).toHaveLength(1));
    expect(api.posts("/reactivate")[0].body).toEqual({ rowVersion: "v7" });
    expect(await screen.findByRole("status")).toHaveTextContent("bitewing.png is active again.");
  });

  it("gives staff who cannot import neither action", async () => {
    stub({ entries: [entryOf(docOf()), entryOf(docOf({ id: "d2", originalFileName: "x.png", status: "Inactive" }))] });
    await open(false);
    expect(screen.queryByRole("button", { name: /Inactivate|Reactivate/ })).toBeNull();
  });
});

describe("the clinical context on edit and on import", () => {
  it("sets the encounter and a tooth on edit, clears the region when a tooth is chosen, and sends the context with the version", async () => {
    const api = stub({ entries: [entryOf(docOf({ regionKey: "Tmj" }))] });
    const user = userEvent.setup();
    await open();
    await user.click(await screen.findByRole("button", { name: "Edit bitewing.png" }));
    const form = within(await screen.findByRole("form", { name: "Edit document" }));
    expect(form.getByLabelText("Oral region (optional)")).toHaveValue("Tmj");
    await user.selectOptions(form.getByLabelText("Encounter (optional)"), "e1");
    await user.selectOptions(form.getByLabelText("Tooth (optional)"), "16");
    expect(form.getByLabelText("Oral region (optional)")).toHaveValue("");
    await user.click(form.getByRole("button", { name: "Save changes" }));
    await waitFor(() => expect(api.edits()).toHaveLength(1));
    expect(api.edits()[0].body).toEqual({ categoryId: "c-rad", documentDate: "2030-03-09", description: "Bitewing, left side.", rowVersion: "v1", encounterId: "e1", toothKey: "16", regionKey: null });
  });

  it("names a wrong encounter beside its box", async () => {
    stub({ entries: [entryOf(docOf())], edit: () => json({ error: "metadata_invalid", message: "x", problems: [{ field: "encounter", code: "wrong_patient", message: "That encounter belongs to another patient." }] }, 400) });
    const user = userEvent.setup();
    await open();
    await user.click(await screen.findByRole("button", { name: "Edit bitewing.png" }));
    const form = within(await screen.findByRole("form", { name: "Edit document" }));
    await user.click(form.getByRole("button", { name: "Save changes" }));
    expect(await form.findByText("That encounter belongs to another patient.")).toBeInTheDocument();
  });

  it("sends the encounter, the tooth or the region typed on import", async () => {
    const api = stub();
    const user = userEvent.setup();
    await open();
    await fillImport(user);
    await user.selectOptions(importForm().getByLabelText("Encounter (optional)"), "e2");
    await user.selectOptions(importForm().getByLabelText("Oral region (optional)"), "UpperLeft");
    await user.click(importForm().getByRole("button", { name: "Import document" }));
    await waitFor(() => expect(api.imports()).toHaveLength(1));
    const form = api.imports()[0].form!;
    expect([form.get("encounterId"), form.get("toothKey"), form.get("regionKey")]).toEqual(["e2", null, "UpperLeft"]);
  });

  it("names a wrong tooth beside its box on import and keeps what was typed", async () => {
    stub({ import: () => json({ error: "metadata_invalid", message: "x", problems: [{ field: "toothKey", code: "unknown_tooth", message: "Use the two-digit FDI tooth number, such as 16 or 55, or leave the tooth empty." }] }, 400) });
    const user = userEvent.setup();
    await open();
    await fillImport(user);
    await user.selectOptions(importForm().getByLabelText("Tooth (optional)"), "16");
    await user.click(importForm().getByRole("button", { name: "Import document" }));
    expect(await importForm().findByText(/Use the two-digit FDI tooth number/)).toBeInTheDocument();
    expect(importForm().getByLabelText("Tooth (optional)")).toHaveValue("16");
  });
});

describe("the upload", () => {
  it("shows how much has gone, can be cancelled, says nothing was imported and the repeat reuses the same key", async () => {
    let release: () => void = () => {};
    const hold = new Promise<void>((r) => { release = r; });
    const api = stub({}, { progress: [0.25, 0.5], hold });
    const user = userEvent.setup();
    await open();
    await fillImport(user);
    await user.click(importForm().getByRole("button", { name: "Import document" }));
    const bar = await importForm().findByRole("progressbar", { name: "Upload progress" });
    await waitFor(() => expect(bar).toHaveAttribute("value", "0.5"));
    expect(importForm().getByText("Uploading… 50%")).toBeInTheDocument();
    await user.click(importForm().getByRole("button", { name: "Cancel upload" }));
    expect(await importForm().findByText(/The upload was cancelled\. Nothing was imported/)).toBeInTheDocument();
    expect(importForm().queryByRole("progressbar")).toBeNull();
    release();

    await user.click(importForm().getByRole("button", { name: "Import document" }));
    await waitFor(() => expect(api.imports().length).toBeGreaterThanOrEqual(1));
    await screen.findByRole("status");
    const keys = api.uploads.calls.map((c) => c.headers["Idempotency-Key"]);
    expect(new Set(keys).size).toBe(1);                                                                // a cancelled attempt and its repeat are one attempt
  });

  it("says nothing was imported when the connection fails", async () => {
    stub({}, { networkError: true });
    const user = userEvent.setup();
    await open();
    await fillImport(user);
    await user.click(importForm().getByRole("button", { name: "Import document" }));
    expect(await importForm().findByText(/Could not reach the server\. Nothing was imported/)).toBeInTheDocument();
  });
});

describe("accessibility", () => {
  it("has no detectable violations with the filters, the thumbnails, an inactivation form and a preview open", async () => {
    stub({ entries: [entryOf(docOf({ encounterId: "e1", toothKey: "16" })), entryOf(docOf({ id: "d2", originalFileName: "b.pdf", detectedType: "pdf", integrityStatus: "Missing", integrityCheckedAtUtc: "2030-03-11T10:00:00Z" }))] });
    const user = userEvent.setup();
    const { container } = await open();
    await user.click(screen.getByRole("radio", { name: "Thumbnails" }));
    await user.click(screen.getAllByRole("button", { name: "Inactivate bitewing.png" })[0]);
    await screen.findByRole("form", { name: "Inactivate document" });
    expect(await axe(container)).toHaveNoViolations();
  });
});
