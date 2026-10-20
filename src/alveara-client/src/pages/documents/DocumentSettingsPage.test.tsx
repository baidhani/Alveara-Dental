import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { axe } from "jest-axe";
import type { BlockedExtension, CategoryView, DocumentSettings } from "../../services/documentsApi";
import { DocumentSettingsPage } from "./DocumentSettingsPage";

/**
 * STORY-010: the administrator's Document settings screen against a scripted API. The rules (categories never deleted, the last one stays in use, the limit between 1 MB and 1 GB, built-in types locked) are
 * proven by the backend tests; what is proven here is the screen: that it shows the truth, sends what was typed with the version it read, names each refusal where it happened, offers no way to remove a
 * built-in type, and explains a change made by someone else.
 */
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
const MB = 1024 * 1024;

const cat = (id: string, name: string, over: Partial<CategoryView> = {}): CategoryView => ({ id, code: name.toLowerCase().replace(/\W+/g, "_"), name, sortOrder: 10, isActive: true, rowVersion: `rv-${id}`, ...over });
const settingsOf = (over: Partial<DocumentSettings> = {}): DocumentSettings => ({ maxUploadBytes: 100 * MB, minAllowedBytes: 1 * MB, maxAllowedBytes: 1024 * MB, rowVersion: "sv1", ...over });
const blocked = (extension: string, isBuiltIn: boolean): BlockedExtension => ({ extension, isBuiltIn, addedAtUtc: "2030-01-01T00:00:00Z" });

interface Call { url: string; method: string; body: Record<string, unknown> | null }
interface Script {
  categories: CategoryView[]; settings: DocumentSettings; blocked: BlockedExtension[]; loadFails: boolean;
  addCategory: (b: Record<string, unknown>) => Response; editCategory: (id: string, b: Record<string, unknown>) => Response; setLimit: (b: Record<string, unknown>) => Response;
  addBlocked: (b: Record<string, unknown>) => Response; removeBlocked: (ext: string) => Response;
}

function stub(over: Partial<Script> = {}) {
  const script: Script = {
    categories: [cat("c1", "Radiograph"), cat("c2", "Photo"), cat("c3", "Other", { isActive: false })], settings: settingsOf(), blocked: [blocked("exe", true), blocked("js", true), blocked("swf", false)], loadFails: false,
    addCategory: (b) => json({ category: cat("c9", String(b.name)) }, 201), editCategory: (id, b) => json({ category: cat(id, String(b.name ?? "Renamed"), { rowVersion: "new", isActive: b.isActive !== false }) }),
    setLimit: (b) => json(settingsOf({ maxUploadBytes: Number(b.maxUploadBytes), rowVersion: "sv2" })), addBlocked: (b) => json({ blockedExtension: blocked(String(b.extension).replace(/^\./, "").toLowerCase(), false) }),
    removeBlocked: () => new Response(null, { status: 204 }), ...over,
  };
  const calls: Call[] = [];
  vi.stubGlobal("fetch", vi.fn().mockImplementation((url: RequestInfo | URL, init?: RequestInit) => {
    const u = String(url);
    const method = init?.method ?? "GET";
    const body = init?.body ? (JSON.parse(init.body as string) as Record<string, unknown>) : null;
    calls.push({ url: u, method, body });
    if (u.includes("/api/auth/csrf-token")) return Promise.resolve(json({ token: "csrf" }));
    if (script.loadFails && u.includes("/api/document")) return Promise.resolve(json({ error: "server_error" }, 500));
    if (u.includes("/api/document-categories") && method === "GET") return Promise.resolve(json({ categories: script.categories }));
    if (u.endsWith("/api/document-categories") && method === "POST") return Promise.resolve(script.addCategory(body!));
    if (u.includes("/api/document-categories/") && method === "PUT") return Promise.resolve(script.editCategory(u.split("/").pop()!, body!));
    if (u.endsWith("/api/document-settings") && method === "GET") return Promise.resolve(json(script.settings));
    if (u.endsWith("/api/document-settings") && method === "PUT") return Promise.resolve(script.setLimit(body!));
    if (u.endsWith("/blocked-extensions") && method === "GET") return Promise.resolve(json({ blockedExtensions: script.blocked }));
    if (u.endsWith("/blocked-extensions") && method === "POST") return Promise.resolve(script.addBlocked(body!));
    if (u.includes("/blocked-extensions/") && method === "DELETE") return Promise.resolve(script.removeBlocked(decodeURIComponent(u.split("/").pop()!)));
    return Promise.resolve(json({}));
  }));
  return { script, calls, sent: (method: string, suffix = "") => calls.filter((c) => c.method === method && c.url.endsWith(suffix)) };
}

async function open() {
  const view = render(<MemoryRouter><DocumentSettingsPage /></MemoryRouter>);
  await screen.findByRole("heading", { name: "Document settings" });
  await screen.findByRole("table", { name: "Categories" });
  return view;
}
const row = (name: string) => within(screen.getByRole("table", { name: "Categories" })).getByRole("row", { name: new RegExp(name) });

afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks(); });

describe("what it shows", () => {
  it("lists every category with whether it is in use, the current limit in megabytes and the blocked types with the built-in ones marked locked", async () => {
    stub();
    await open();
    expect(within(row("Radiograph")).getByText("In use")).toBeInTheDocument();
    expect(within(row("Other")).getByText("Not in use")).toBeInTheDocument();
    expect(screen.getByLabelText("Largest file (MB)")).toHaveValue(100);
    const list = screen.getByRole("list", { name: "Blocked file types" });
    expect(within(list).getAllByRole("listitem")).toHaveLength(3);
    expect(within(list).getAllByText("Locked (built in)")).toHaveLength(2);
    expect(within(list).getByRole("button", { name: "Unblock .swf" })).toBeInTheDocument();
    expect(within(list).queryByRole("button", { name: "Unblock .exe" })).toBeNull();
  });

  it("links to the form templates, which are managed there and not here", async () => {
    stub();
    await open();
    expect(screen.getByRole("link", { name: "Manage form templates" })).toHaveAttribute("href", "/admin/form-templates");
  });

  it("says plainly when it cannot be loaded", async () => {
    stub({ loadFails: true });
    render(<MemoryRouter><DocumentSettingsPage /></MemoryRouter>);
    expect(await screen.findByText("Could not load the document settings")).toBeInTheDocument();
  });
});

describe("categories", () => {
  it("adds a category with the name typed and shows it", async () => {
    const api = stub();
    const user = userEvent.setup();
    await open();
    await user.type(screen.getByLabelText("New category name"), "Consent copy");
    await user.click(screen.getByRole("button", { name: "Add category" }));
    await waitFor(() => expect(api.sent("POST", "/api/document-categories")).toHaveLength(1));
    expect(api.sent("POST", "/api/document-categories")[0].body).toEqual({ name: "Consent copy" });
    expect(await screen.findByRole("status")).toHaveTextContent("Added Consent copy");
    expect(screen.getByLabelText("New category name")).toHaveValue("");
  });

  it("names a refused category beside its box and keeps what was typed", async () => {
    stub({ addCategory: () => json({ error: "category_exists", message: "A category with that name already exists." }, 409) });
    const user = userEvent.setup();
    await open();
    await user.type(screen.getByLabelText("New category name"), "photo");
    await user.click(screen.getByRole("button", { name: "Add category" }));
    expect(await screen.findByText("A category with that name already exists.")).toBeInTheDocument();
    expect(screen.getByLabelText("New category name")).toHaveValue("photo");
  });

  it("asks for a name before sending anything", async () => {
    const api = stub();
    const user = userEvent.setup();
    await open();
    await user.click(screen.getByRole("button", { name: "Add category" }));
    expect(await screen.findByText("Enter a name for the category.")).toBeInTheDocument();
    expect(api.sent("POST")).toHaveLength(0);
  });

  it("renames with the version it read", async () => {
    const api = stub();
    const user = userEvent.setup();
    await open();
    await user.click(within(row("Photo")).getByRole("button", { name: "Rename Photo" }));
    const box = screen.getByLabelText("New name for Photo");
    await user.clear(box);
    await user.type(box, "Clinical photo");
    await user.click(screen.getByRole("button", { name: "Save name" }));
    await waitFor(() => expect(api.sent("PUT")).toHaveLength(1));
    expect(api.sent("PUT")[0].body).toEqual({ name: "Clinical photo", rowVersion: "rv-c2" });
  });

  it("deactivates and reactivates, never deletes, and offers no delete at all", async () => {
    const api = stub();
    const user = userEvent.setup();
    await open();
    expect(screen.queryByRole("button", { name: /Delete/ })).toBeNull();
    await user.click(within(row("Photo")).getByRole("button", { name: "Stop using Photo" }));
    await waitFor(() => expect(api.sent("PUT")).toHaveLength(1));
    expect(api.sent("PUT")[0].body).toEqual({ isActive: false, rowVersion: "rv-c2" });
    await user.click(within(row("Other")).getByRole("button", { name: "Use Other again" }));
    await waitFor(() => expect(api.sent("PUT")).toHaveLength(2));
    expect(api.sent("PUT")[1].body).toEqual({ isActive: true, rowVersion: "rv-c3" });
  });

  it("explains that the last category in use cannot be stopped", async () => {
    stub({ editCategory: () => json({ error: "last_active_category", message: "At least one category must stay in use, so the last one cannot be deactivated." }, 409) });
    const user = userEvent.setup();
    await open();
    await user.click(within(row("Photo")).getByRole("button", { name: "Stop using Photo" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("At least one category must stay in use");
  });

  it("explains a change made by someone else and reloads on request", async () => {
    const api = stub({ editCategory: () => json({ error: "concurrency_conflict", entityType: "DocumentCategory", entityId: "c2" }, 409) });
    const user = userEvent.setup();
    await open();
    await user.click(within(row("Photo")).getByRole("button", { name: "Stop using Photo" }));
    expect(await screen.findByText("Someone else changed this while you were editing")).toBeInTheDocument();
    const categoryLoads = () => api.calls.filter((c) => c.method === "GET" && c.url.includes("/api/document-categories")).length;
    const loads = categoryLoads();
    await user.click(screen.getByRole("button", { name: "Reload current version" }));
    await waitFor(() => expect(categoryLoads()).toBeGreaterThan(loads));
  });
});

describe("the size limit", () => {
  it("sends the limit in bytes with the version it read and shows it saved", async () => {
    const api = stub();
    const user = userEvent.setup();
    await open();
    const box = screen.getByLabelText("Largest file (MB)");
    await user.clear(box);
    await user.type(box, "250");
    await user.click(screen.getByRole("button", { name: "Save size limit" }));
    await waitFor(() => expect(api.sent("PUT", "/api/document-settings")).toHaveLength(1));
    expect(api.sent("PUT", "/api/document-settings")[0].body).toEqual({ maxUploadBytes: 250 * MB, rowVersion: "sv1" });
    expect(await screen.findByRole("status")).toHaveTextContent("Size limit saved");
  });

  it.each(["0", "1025", "-3", "", "2.5x"])("refuses %j before sending anything and says the allowed range", async (typed) => {
    const api = stub();
    const user = userEvent.setup();
    await open();
    const box = screen.getByLabelText("Largest file (MB)");
    await user.clear(box);
    if (typed) await user.type(box, typed);
    await user.click(screen.getByRole("button", { name: "Save size limit" }));
    expect(await screen.findByText(/Enter a whole number of megabytes from 1 to 1024/)).toBeInTheDocument();
    expect(api.sent("PUT")).toHaveLength(0);
  });

  it("names a refusal from the server beside the box", async () => {
    stub({ setLimit: () => json({ error: "metadata_invalid", message: "x", problems: [{ field: "maxUploadBytes", code: "out_of_range", message: "The size limit must be between 1 MB and 1024 MB." }] }, 400) });
    const user = userEvent.setup();
    await open();
    await user.click(screen.getByRole("button", { name: "Save size limit" }));
    expect(await screen.findByText("The size limit must be between 1 MB and 1024 MB.")).toBeInTheDocument();
  });
});

describe("blocked file types", () => {
  it("blocks a type with the text typed and shows it", async () => {
    const api = stub();
    const user = userEvent.setup();
    await open();
    await user.type(screen.getByLabelText("File type to block"), ".ISO");
    await user.click(screen.getByRole("button", { name: "Block file type" }));
    await waitFor(() => expect(api.sent("POST", "/blocked-extensions")).toHaveLength(1));
    expect(api.sent("POST", "/blocked-extensions")[0].body).toEqual({ extension: ".ISO" });
    expect(await screen.findByRole("status")).toHaveTextContent("Blocked .iso");
  });

  it("names an unusable type beside its box", async () => {
    stub({ addBlocked: () => json({ error: "metadata_invalid", message: "x", problems: [{ field: "extension", code: "invalid", message: "Enter a file type of 1 to 16 letters or digits, such as swf. Do not include a dot or other punctuation." }] }, 400) });
    const user = userEvent.setup();
    await open();
    await user.type(screen.getByLabelText("File type to block"), "a b");
    await user.click(screen.getByRole("button", { name: "Block file type" }));
    expect(await screen.findByText(/Enter a file type of 1 to 16 letters or digits/)).toBeInTheDocument();
    expect(screen.getByLabelText("File type to block")).toHaveValue("a b");
  });

  it("unblocks a type an administrator added", async () => {
    const api = stub();
    const user = userEvent.setup();
    await open();
    await user.click(screen.getByRole("button", { name: "Unblock .swf" }));
    await waitFor(() => expect(api.sent("DELETE")).toHaveLength(1));
    expect(api.sent("DELETE")[0].url).toContain("/blocked-extensions/swf");
  });

  it("shows the server's reason if a locked type is refused anyway", async () => {
    stub({ blocked: [blocked("exe", false)], removeBlocked: () => json({ error: "built_in_locked", message: "The .exe type is blocked for everyone and cannot be unblocked." }, 409) });
    const user = userEvent.setup();
    await open();
    await user.click(screen.getByRole("button", { name: "Unblock .exe" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("cannot be unblocked");
  });
});

describe("accessibility", () => {
  it("has no detectable violations", async () => {
    stub();
    const { container } = await open();
    expect(await axe(container)).toHaveNoViolations();
  });
});
