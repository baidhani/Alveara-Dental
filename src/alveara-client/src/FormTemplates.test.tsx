import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { App } from "./App";
import { FakeFormsServer } from "./test/fakeFormsServer";
import { json, makePatient } from "./test/fakePatientServer";
import { moduleRegistry } from "./app/moduleRegistry";

/** ALV-N010: form/consent template administration through the real <App /> against an in-memory fake of the forms API. */
let server: FakeFormsServer;

function open(path: string) {
  window.history.pushState({}, "", path);
  return render(<App />);
}

beforeEach(() => {
  server = new FakeFormsServer();
  server.permissions = ["ManageFormTemplates", "ViewPatientRecords"];
  server.add(makePatient({ id: "aaaaaaaa-0000-0000-0000-000000000001" }));
  server.addTemplate("privacy-notice", { id: "t1", title: "Privacy notice" });
  server.addTemplate("financial-policy", { id: "t2", category: "Financial", title: "Financial policy" });
  server.install();
});
afterEach(() => vi.unstubAllGlobals());

async function openTemplate(name: RegExp) {
  open("/admin/form-templates");
  await userEvent.click(await screen.findByRole("button", { name }));
  return screen.findByRole("form", { name: "Edit form template" });
}

describe("form template administration", () => {
  it("lists every template with its category, current version and active state", async () => {
    server.templates.get("t2")!.isActive = false;
    open("/admin/form-templates");

    const nav = await screen.findByRole("navigation", { name: "Form templates" });
    expect(within(nav).getByText(/Privacy · v1 · Active/)).toBeInTheDocument();
    expect(within(nav).getByText(/Financial · v1 · Inactive/)).toBeInTheDocument();
  });

  it("is reachable from the navigation and its route only with the template permission", async () => {
    open("/admin/form-templates");
    expect(await screen.findByRole("link", { name: "Form Templates" })).toHaveAttribute("aria-current", "page");
    expect(moduleRegistry.find((m) => m.id === "form-templates")?.requiredPermission).toBe("ManageFormTemplates");
  });

  it("without the permission neither the link nor the page is available", async () => {
    server.permissions = ["ViewPatientRecords", "CompleteForms", "ViewSignedForms"];
    open("/admin/form-templates");
    await waitFor(() => expect(screen.queryByRole("link", { name: "Form Templates" })).not.toBeInTheDocument());
    expect(screen.queryByRole("heading", { name: "Form templates" })).not.toBeInTheDocument();
    expect(server.callsToForms("GET", "/api/forms/templates")).toHaveLength(0);
  });

  it("creates a template with questions and sends the category, text and fields", async () => {
    open("/admin/form-templates");
    await userEvent.click(await screen.findByRole("button", { name: "New template" }));
    await userEvent.type(screen.getByLabelText(/Key \(cannot be changed later\)/), "treatment-consent");
    await userEvent.selectOptions(screen.getByLabelText("Category *"), "Treatment");
    await userEvent.type(screen.getByLabelText("Title *"), "Treatment consent");
    await userEvent.type(screen.getByLabelText("Form text shown to the signer *"), "I consent to treatment.");
    await userEvent.click(screen.getByRole("button", { name: "Add a question" }));
    await userEvent.type(screen.getByLabelText("Question text *"), "I understand the risks");
    await userEvent.type(screen.getByLabelText("Field id *"), "understood");
    await userEvent.click(screen.getByLabelText("Required to sign"));
    await userEvent.click(screen.getByRole("button", { name: "Create template" }));

    expect(await screen.findByText("Template created as version 1.")).toBeInTheDocument();
    const post = server.callsToForms("POST", "/api/forms/templates")[0];
    expect(post.body).toMatchObject({ key: "treatment-consent", category: "Treatment", title: "Treatment consent", fields: [{ id: "understood", label: "I understand the risks", kind: "text", required: true }] });
    expect(post.headers["X-CSRF-Token"]).toBe("csrf-1");
    expect(within(screen.getByRole("navigation", { name: "Form templates" })).getByText(/Treatment consent/)).toBeInTheDocument();
  });

  it("shows the server's message for an invalid key or a duplicate key", async () => {
    open("/admin/form-templates");
    await userEvent.click(await screen.findByRole("button", { name: "New template" }));
    await userEvent.type(screen.getByLabelText(/Key \(cannot be changed later\)/), "Bad Key");
    await userEvent.click(screen.getByRole("button", { name: "Create template" }));
    expect(await screen.findByText("Use 3-60 lowercase letters, digits and hyphens.")).toBeInTheDocument();

    await userEvent.clear(screen.getByLabelText(/Key \(cannot be changed later\)/));
    await userEvent.type(screen.getByLabelText(/Key \(cannot be changed later\)/), "privacy-notice");
    await userEvent.click(screen.getByRole("button", { name: "Create template" }));
    expect(await screen.findByText(/A template with the key 'privacy-notice' already exists/)).toBeInTheDocument();
  });

  it("editing publishes a NEW version, says so, and keeps the earlier version in the history", async () => {
    const form = await openTemplate(/Privacy notice/);
    expect(within(form).getByLabelText(/Key \(cannot be changed later\)/)).toBeDisabled();
    expect(within(form).getByText(/Saving changes publishes version 2/)).toBeInTheDocument();

    await userEvent.clear(within(form).getByLabelText("Form text shown to the signer *"));
    await userEvent.type(within(form).getByLabelText("Form text shown to the signer *"), "Reworded policy.");
    await userEvent.click(within(form).getByRole("button", { name: "Publish new version" }));

    expect(await screen.findByText("Version 2 published.")).toBeInTheDocument();
    const put = server.callsToForms("PUT", "/api/forms/templates/t1")[0];
    expect(put.body).toMatchObject({ body: "Reworded policy.", rowVersion: "t2" });
    const history = await screen.findByRole("table", { name: /Published versions/ });
    expect(within(history).getByText("2 (current)")).toBeInTheDocument();
    expect(within(history).getByText("1")).toBeInTheDocument();
    expect(server.templates.get("t1")!.versions.at(-1)!.body).toBe("We protect your information."); // version 1 is untouched
  });

  it("Publish is disabled until something changed (so a repeat does nothing)", async () => {
    const form = await openTemplate(/Privacy notice/);
    expect(within(form).getByRole("button", { name: "Publish new version" })).toBeDisabled();
  });

  it("a stale edit is reported as a conflict, not published", async () => {
    const form = await openTemplate(/Privacy notice/);
    server.templates.get("t1")!.v++; // another administrator published meanwhile
    await userEvent.type(within(form).getByLabelText("Title *"), "!");
    await userEvent.click(within(form).getByRole("button", { name: "Publish new version" }));

    expect(await screen.findByText("Someone else changed this while you were editing")).toBeInTheDocument();
    expect(server.templates.get("t1")!.versions).toHaveLength(1);
  });

  it("a choice question needs options and the editor offers one per line", async () => {
    open("/admin/form-templates");
    await userEvent.click(await screen.findByRole("button", { name: "New template" }));
    await userEvent.click(screen.getByRole("button", { name: "Add a question" }));
    await userEvent.selectOptions(screen.getByLabelText("Type"), "choice");
    expect(screen.getByLabelText("Options (one per line) *")).toBeInTheDocument();
  });

  it("a template can be inactivated and reactivated; forms already signed are unaffected (said on screen)", async () => {
    const form = await openTemplate(/Privacy notice/);
    await userEvent.click(within(form).getByRole("button", { name: "Inactivate template" }));
    expect(await screen.findByText(/Template inactivated. Forms already started or signed are unaffected/)).toBeInTheDocument();
    expect(server.templates.get("t1")!.isActive).toBe(false);
    await userEvent.click(await screen.findByRole("button", { name: "Reactivate template" }));
    await waitFor(() => expect(server.templates.get("t1")!.isActive).toBe(true));
  });

  it("a template can be required at check-in and no longer required; it publishes no new version and says nobody is blocked from checking in", async () => {
    const form = await openTemplate(/Privacy notice/);
    expect(within(form).getByRole("button", { name: "Require at check-in", pressed: false })).toBeInTheDocument();

    await userEvent.click(within(form).getByRole("button", { name: "Require at check-in" }));

    expect(await screen.findByText(/This form is now required at check-in. It only shows on the visit board; nobody is blocked from checking in./)).toBeInTheDocument();
    expect(server.templates.get("t1")!.required).toBe(true);
    expect(server.templates.get("t1")!.versions).toHaveLength(1); // no new version
    expect(screen.getByText(/Required at check-in/, { selector: "span" })).toBeInTheDocument(); // the list says so too
    await userEvent.click(await screen.findByRole("button", { name: "Stop requiring at check-in", pressed: true }));
    await waitFor(() => expect(server.templates.get("t1")!.required).toBe(false));
    expect(await screen.findByText("This form is no longer required at check-in.")).toBeInTheDocument();
  });

  it("a stale requirement change is the shared conflict and nothing is applied", async () => {
    const form = await openTemplate(/Privacy notice/);
    server.templates.get("t1")!.v++; // someone else changed the template first
    await userEvent.click(within(form).getByRole("button", { name: "Require at check-in" }));
    expect(await screen.findByText("Someone else changed this while you were editing")).toBeInTheDocument();
    expect(server.templates.get("t1")!.required).toBeFalsy();
  });

  it("says the wording is the practice's own and that legal sufficiency is not claimed", async () => {
    const form = await openTemplate(/Privacy notice/);
    expect(within(form).getByText(/does not make any wording legally sufficient/)).toBeInTheDocument();
  });

  it("offers Retry when the templates cannot be loaded", async () => {
    server.failForms("GET /api/forms/templates", json(500, { error: "boom" }));
    open("/admin/form-templates");
    await userEvent.click(await screen.findByRole("button", { name: "Retry" }));
    expect(await screen.findByRole("button", { name: /Privacy notice/ })).toBeInTheDocument();
  });

  it("has no detectable accessibility violations (list and editor)", async () => {
    const { container } = open("/admin/form-templates");
    await screen.findByRole("button", { name: /Privacy notice/ });
    expect(await axe(container)).toHaveNoViolations();
    await userEvent.click(screen.getByRole("button", { name: /Privacy notice/ }));
    await screen.findByRole("form", { name: "Edit form template" });
    expect(await axe(container)).toHaveNoViolations();
  });
});
