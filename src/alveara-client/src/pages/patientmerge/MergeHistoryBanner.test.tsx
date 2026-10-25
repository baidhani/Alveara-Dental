import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { axe } from "jest-axe";
import { MergeHistoryBanner } from "./MergeHistoryBanner";

// ALV-017-C01: the merge lineage on a patient's own page, against a small in-memory server.

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
const link = (over: Record<string, unknown> = {}) => ({ mergeId: "m1", patientId: "p2", patientName: "Ann Lee", mergedAtUtc: "2026-10-25T10:00:00Z", reason: "Registered twice at the desk", mergedByName: "admin-hana", ...over });

let history: unknown;
let status = 200;
let urls: string[];

beforeEach(() => {
  history = { mergedInto: null, absorbed: [] }; status = 200; urls = [];
  vi.stubGlobal("fetch", vi.fn().mockImplementation((url: string) => { urls.push(String(url)); return Promise.resolve(json(history, status)); }));
});
afterEach(() => vi.unstubAllGlobals());

const renderBanner = (id = "p1") => render(<MemoryRouter><MergeHistoryBanner patientId={id} /></MemoryRouter>);

describe("the merge history banner", () => {
  it("says nothing for a patient who was never part of a merge", async () => {
    const { container } = renderBanner();
    await vi.waitFor(() => expect(urls).toHaveLength(1));
    expect(urls[0]).toBe("/api/patients/p1/merge-history");
    expect(container).toBeEmptyDOMElement();
  });

  it("tells a retired record in words that it was merged, by whom, when and why, and links to the record that carries on", async () => {
    history = { mergedInto: link(), absorbed: [] };
    const { container } = renderBanner();
    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("This record was merged into another and takes no new records.");
    expect(alert).toHaveTextContent("admin-hana");
    expect(alert).toHaveTextContent("Registered twice at the desk");
    expect(screen.getByRole("link", { name: "Ann Lee" })).toHaveAttribute("href", "/patients/p2");
    expect(await axe(container)).toHaveNoViolations();
  });

  it("lists the records merged into a surviving record with a link to each", async () => {
    history = { mergedInto: null, absorbed: [link({ mergeId: "m1", patientId: "p3", patientName: "Anne Lee" }), link({ mergeId: "m2", patientId: "p4", patientName: "A. Lee", mergedByName: "" })] };
    const { container } = renderBanner();
    expect(await screen.findByText("Records merged into this one:")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /Anne Lee on .* by admin-hana/ })).toHaveAttribute("href", "/patients/p3");
    expect(screen.getByRole("link", { name: /A\. Lee on .* by an administrator/ })).toHaveAttribute("href", "/patients/p4");
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    expect(await axe(container)).toHaveNoViolations();
  });

  it("treats an answer of the wrong shape as unreadable rather than failing the page", async () => {
    history = { id: "p1", firstName: "Ann" };
    renderBanner();
    expect(await screen.findByRole("status")).toHaveTextContent("Could not check whether this record was merged into another");
  });

  it("says so when the history cannot be read instead of leaving a retired record looking like any other", async () => {
    status = 500; history = { error: "internal_error" };
    renderBanner();
    expect(await screen.findByRole("status")).toHaveTextContent("Could not check whether this record was merged into another");
  });
});
