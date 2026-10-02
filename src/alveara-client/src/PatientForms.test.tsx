import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { App } from "./App";
import { FakeFormsServer } from "./test/fakeFormsServer";
import { json, makePatient } from "./test/fakePatientServer";

/**
 * ALV-N010: the patient's Forms tab - starting, completing, reviewing, signing and voiding a form - exercised through the real <App />
 * (real router, shell, auth) against an in-memory fake of the forms API. The rules themselves are proven by the backend tests and the
 * real-backend browser walkthrough; what is proven here is the UI's behavior for each outcome.
 */
const A = "aaaaaaaa-0000-0000-0000-000000000001";
const B = "bbbbbbbb-0000-0000-0000-000000000002";
const T = "tmpl-privacy";

let server: FakeFormsServer;

function open(path: string) {
  window.history.pushState({}, "", path);
  return render(<App />);
}
function go(path: string) {
  act(() => {
    window.history.pushState({}, "", path);
    window.dispatchEvent(new PopStateEvent("popstate"));
  });
}

beforeEach(() => {
  server = new FakeFormsServer();
  server.add(makePatient({ id: A, firstName: "Ann", lastName: "Lee" }));
  server.add(makePatient({ id: B, firstName: "Ben", lastName: "Moss", dateOfBirth: "1990-01-01", phone: "555-020-0200" }));
  server.addTemplate("privacy-notice", { id: T });
  server.install();
});
afterEach(() => vi.unstubAllGlobals());

const signCalls = () => server.callsToForms("POST", "/api/forms/").filter((c) => c.url.endsWith("/sign"));

async function openReview(patientId = A) {
  const draft = server.addReadyDraft(patientId, T);
  open(`/patients/${patientId}/forms/${draft.id}`);
  await userEvent.click(await screen.findByRole("button", { name: "Review and sign" }));
  await screen.findByRole("heading", { name: "Review before signing" });
  return draft;
}

async function fillSigner(name = "Ann Lee", relationship = "Self (the patient)") {
  await userEvent.type(screen.getByLabelText("Signer's full name *"), name);
  await userEvent.selectOptions(screen.getByLabelText("Relationship to the patient *"), relationship);
  await userEvent.type(screen.getByLabelText("Type your name as your signature *"), name);
  await userEvent.click(screen.getByRole("checkbox", { name: /I have read this form/ }));
}

describe("the Forms tab", () => {
  it("is shown to roles that may view forms, lists the patient's forms with the status in words, and nothing is implied signed", async () => {
    server.addReadyDraft(A, T);
    server.addReadyDraft(B, T); // another patient's form never appears here
    open(`/patients/${A}/forms`);

    const table = await screen.findByRole("table", { name: /This patient's forms/ });
    expect(within(table).getAllByRole("row")).toHaveLength(2); // header + Ann's draft
    expect(within(table).getByText("Draft - unsigned")).toBeInTheDocument();
    expect(within(screen.getByRole("navigation", { name: "Patient workspace sections" })).getAllByRole("link").map((l) => l.textContent)).toEqual(
      ["Details", "Household & guarantor", "History", "Forms"],
    );
  });

  it("is hidden, and its route denied, without the permission to view forms", async () => {
    server.permissions = ["ViewPatientRecords"];
    open(`/patients/${A}/forms`);
    await screen.findByLabelText("First name *").catch(() => null);
    await waitFor(() => expect(screen.queryByRole("link", { name: "Forms" })).not.toBeInTheDocument());
    expect(screen.queryByRole("table", { name: /This patient's forms/ })).not.toBeInTheDocument();
    expect(server.callsToForms("GET", `/api/patients/${A}/forms`)).toHaveLength(0);
  });

  it("starts a form from an active template and opens the draft, showing the wording and that it is unsigned", async () => {
    server.addTemplate("hidden-form", { id: "tmpl-off", isActive: false, title: "Retired form" });
    open(`/patients/${A}/forms`);
    const select = await screen.findByLabelText("Start a form");
    expect(within(select).queryByRole("option", { name: /Retired form/ })).not.toBeInTheDocument();

    await userEvent.selectOptions(select, T);
    await userEvent.click(screen.getByRole("button", { name: "Start form" }));

    expect(await screen.findByRole("heading", { name: "Privacy notice" })).toBeInTheDocument();
    expect(screen.getByText("Draft - unsigned")).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Form wording" })).toHaveTextContent("We protect your information.");
    expect(server.callsToForms("POST", `/api/patients/${A}/forms`)[0].body).toEqual({ templateId: T });
  });

  it("cannot start a form for an inactive patient", async () => {
    server.patients.get(A)!.isActive = false;
    open(`/patients/${A}/forms`);
    expect(await screen.findByText(/This patient is inactive/)).toBeInTheDocument();
    expect(screen.queryByLabelText("Start a form")).not.toBeInTheDocument();
  });

  it("a role that can view but not complete forms sees them read-only, with no way to start, save or sign", async () => {
    server.permissions = ["ViewPatientRecords", "ViewSignedForms"];
    const draft = server.addReadyDraft(A, T);
    open(`/patients/${A}/forms/${draft.id}`);

    expect(await screen.findByText(/can view this draft but not complete it/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Review and sign" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Save draft" })).not.toBeInTheDocument();
    expect(screen.getByLabelText(/I have read the privacy notice/)).toBeDisabled();
  });

  it("an error loading the list says so instead of showing an empty list", async () => {
    server.failForms(`GET /api/patients/${A}/forms`, json(500, { error: "boom" }));
    open(`/patients/${A}/forms`);
    expect(await screen.findByText("Could not load the forms")).toBeInTheDocument();
    expect(screen.queryByText("No forms yet")).not.toBeInTheDocument();
  });
});

describe("completing a draft", () => {
  it("saves the answers on request, sending the version it read", async () => {
    const draft = server.addReadyDraft(A, T);
    draft.responses = {};
    open(`/patients/${A}/forms/${draft.id}`);

    await userEvent.type(await screen.findByLabelText("Preferred name"), "Annie");
    expect(screen.getByText("Unsaved changes")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Save draft" }));

    expect(await screen.findByText("Draft saved.")).toBeInTheDocument();
    const put = server.callsToForms("PUT", `/api/forms/${draft.id}/responses`)[0];
    expect(put.body).toMatchObject({ responses: { nickname: "Annie" }, rowVersion: "f1" });
    expect(put.headers["X-CSRF-Token"]).toBe("csrf-1");
    expect(screen.queryByText("Unsaved changes")).not.toBeInTheDocument();
  });

  it("will not go to review while a required answer is missing, and says which", async () => {
    const draft = server.addReadyDraft(A, T);
    draft.responses = { nickname: "Annie" };
    open(`/patients/${A}/forms/${draft.id}`);

    await userEvent.click(await screen.findByRole("button", { name: "Review and sign" }));

    expect(await screen.findByText("This must be checked.")).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Review before signing" })).not.toBeInTheDocument();
  });

  it("a stale save is reported as a conflict instead of overwriting", async () => {
    const draft = server.addReadyDraft(A, T);
    open(`/patients/${A}/forms/${draft.id}`);
    await userEvent.type(await screen.findByLabelText("Preferred name"), "!");
    draft.v++; // someone else saved meanwhile

    await userEvent.click(screen.getByRole("button", { name: "Save draft" }));

    expect(await screen.findByText("Someone else changed this while you were editing")).toBeInTheDocument();
  });

  it("offers the newer template version without changing the draft, and moving to it keeps the answers that still fit", async () => {
    const draft = server.addReadyDraft(A, T);
    server.publish(T, "Privacy notice", "Version two wording.", FakeFormsServer.fields());
    open(`/patients/${A}/forms/${draft.id}`);

    expect(await screen.findByText(/A newer version \(version 2\)/)).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Form wording" })).toHaveTextContent("We protect your information."); // still its own version
    await userEvent.click(screen.getByRole("button", { name: "Move to version 2" }));

    await waitFor(() => expect(screen.getByRole("region", { name: "Form wording" })).toHaveTextContent("Version two wording."));
    expect(screen.getByLabelText("Preferred name")).toHaveValue("Annie");
    expect(screen.queryByText(/A newer version/)).not.toBeInTheDocument();
    expect(server.forms.get(draft.id)!.status).toBe("Void");
  });

  it("a draft can be discarded only with a reason", async () => {
    const draft = server.addReadyDraft(A, T);
    open(`/patients/${A}/forms/${draft.id}`);
    await userEvent.click(await screen.findByRole("button", { name: "Discard this draft" }));
    await userEvent.click(screen.getByRole("button", { name: "Confirm" }));
    expect(await screen.findByText("A reason is required.")).toBeInTheDocument();

    await userEvent.type(screen.getByLabelText("Reason (required)"), "Wrong visit");
    await userEvent.click(screen.getByRole("button", { name: "Confirm" }));

    expect(await screen.findByText(/This form is void/)).toBeInTheDocument();
    expect(screen.getByText(/Reason: Wrong visit/)).toBeInTheDocument();
  });
});

describe("review and sign", () => {
  it("shows exactly what will be signed - the wording and the saved answers - before anything is signed", async () => {
    await openReview();

    expect(screen.getByRole("region", { name: "Form wording" })).toHaveTextContent("We protect your information.");
    const answers = screen.getByRole("heading", { name: "Answers for Ann Lee" }).parentElement!;
    expect(within(answers).getByText("Annie")).toBeInTheDocument();
    expect(within(answers).getByText("Email")).toBeInTheDocument();
    expect(screen.getByText(/does not by itself establish that the form is legally sufficient/)).toBeInTheDocument();
    expect(signCalls()).toHaveLength(0);
  });

  it("refuses to sign without the signer's identity, relationship, signature and confirmation, naming each", async () => {
    await openReview();
    await userEvent.click(screen.getByRole("button", { name: "Sign form" }));

    expect(await screen.findByText("The signer's name is required.")).toBeInTheDocument();
    expect(screen.getByText("Say how the signer relates to the patient.")).toBeInTheDocument();
    expect(screen.getByText("The typed signature is required.")).toBeInTheDocument();
    expect(screen.getByText("The signer must confirm the statement before signing.")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Review before signing" })).toBeInTheDocument(); // still a draft
  });

  it("the Other relationship asks for a description", async () => {
    await openReview();
    expect(screen.queryByLabelText("Describe the relationship *")).not.toBeInTheDocument();
    await userEvent.selectOptions(screen.getByLabelText("Relationship to the patient *"), "Other");
    expect(screen.getByLabelText("Describe the relationship *")).toBeInTheDocument();
  });

  it("signs, then shows the signed copy read-only with the signer, the version, the answers at signing and an integrity result", async () => {
    const draft = await openReview();
    await fillSigner("Pat Lee", "Parent");
    await userEvent.click(screen.getByRole("button", { name: "Sign form" }));

    expect(await screen.findByRole("heading", { name: "Signed copy" })).toBeInTheDocument();
    expect(screen.getByText("Form signed.")).toBeInTheDocument();
    expect(screen.getByText("Signed", { selector: "span" })).toBeInTheDocument();
    const signature = screen.getByRole("heading", { name: "Signature" }).parentElement!;
    expect(within(signature).getByText("Signed by").nextElementSibling).toHaveTextContent("Pat Lee");
    expect(within(signature).getByText("Parent")).toBeInTheDocument();
    expect(screen.getByText(/Integrity check: matches the copy made at signing/)).toBeInTheDocument();
    expect(screen.getAllByText("Template version 1").length).toBeGreaterThan(0); // in the header and on the signed copy
    expect(screen.queryByLabelText("Preferred name")).not.toBeInTheDocument(); // nothing editable
    expect(screen.queryByRole("button", { name: "Save draft" })).not.toBeInTheDocument();
    expect(within(screen.getByRole("table", { name: /Status history/ })).getByText("Signed by Pat Lee (Parent).")).toBeInTheDocument();

    const call = signCalls()[0];
    expect(call.headers["Idempotency-Key"]).toMatch(/^sign-/);
    expect(call.headers["X-CSRF-Token"]).toBe("csrf-1");
    expect(call.body).toMatchObject({ signerName: "Pat Lee", relationship: "Parent", attested: true, templateVersionId: `${T}-v1`, rowVersion: "f1" });
    expect(server.forms.get(draft.id)!.status).toBe("Signed");
  });

  it("a second click while the signature is going out does not send a second request", async () => {
    await openReview();
    await fillSigner();
    const sign = screen.getByRole("button", { name: "Sign form" });
    await userEvent.dblClick(sign);

    await screen.findByRole("heading", { name: "Signed copy" });
    expect(signCalls()).toHaveLength(1);
  });

  it("if the connection drops after the signature was sent it says the outcome is unknown, and Sign again reuses the same key so it cannot sign twice", async () => {
    const draft = await openReview();
    await fillSigner();
    server.dropNextSignResponse = true;
    await userEvent.click(screen.getByRole("button", { name: "Sign form" }));

    expect(await screen.findByText(/could not confirm whether the signature was saved/)).toBeInTheDocument();
    expect(screen.getByText(/cannot be recorded twice/)).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Review before signing" })).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Sign again" }));

    expect(await screen.findByRole("heading", { name: "Signed copy" })).toBeInTheDocument();
    const keys = signCalls().map((c) => c.headers["Idempotency-Key"]);
    expect(keys).toHaveLength(2);
    expect(keys[0]).toBe(keys[1]);
    expect(server.forms.get(draft.id)!.events.filter((e) => e.eventType === "Signed")).toHaveLength(1);
  });

  it("Check status after an unknown outcome shows the form as it really is", async () => {
    await openReview();
    await fillSigner();
    server.dropNextSignResponse = true;
    await userEvent.click(screen.getByRole("button", { name: "Sign form" }));

    await userEvent.click(await screen.findByRole("button", { name: "Check status" }));

    expect(await screen.findByRole("heading", { name: "Signed copy" })).toBeInTheDocument(); // it had been stored
  });

  it("if someone else already signed the form it shows the signed copy instead of signing again", async () => {
    const draft = await openReview();
    await fillSigner();
    // another user signs it first
    draft.snapshot = { id: "s-other", patientFormId: draft.id, templateVersionNumber: 1, templateKey: "privacy-notice", category: "Privacy", title: "Privacy notice", body: "We protect your information.",
      fields: draft.version.fields, responses: draft.responses, signerName: "Someone Else", signerRelationship: "Self", signerRelationshipNote: null, signatureMethod: "typed-name",
      signatureText: "Someone Else", attestation: "x", signedAtUtc: "2026-10-02T10:00:00Z", capturedByUserId: "u2", snapshotHash: "cd".repeat(32), integrityVerified: true };
    draft.status = "Signed";
    draft.v++;

    await userEvent.click(screen.getByRole("button", { name: "Sign form" }));

    expect(await screen.findByRole("heading", { name: "Signed copy" })).toBeInTheDocument();
    expect(screen.getByText("Signed by").nextElementSibling).toHaveTextContent("Someone Else");
  });

  it("a draft changed after it was reviewed is reported as a conflict, never signed", async () => {
    const draft = await openReview();
    await fillSigner();
    draft.v++; // someone edited the draft after the review opened

    await userEvent.click(screen.getByRole("button", { name: "Sign form" }));

    expect(await screen.findByText("Someone else changed this while you were editing")).toBeInTheDocument();
    expect(draft.status).toBe("Draft");
  });

  it("Back to edit returns to the answers without signing", async () => {
    await openReview();
    await userEvent.click(screen.getByRole("button", { name: "Back to edit" }));
    expect(await screen.findByLabelText("Preferred name")).toBeInTheDocument();
    expect(signCalls()).toHaveLength(0);
  });
});

describe("signed and void forms", () => {
  async function signed() {
    await openReview();
    await fillSigner();
    await userEvent.click(screen.getByRole("button", { name: "Sign form" }));
    await screen.findByRole("heading", { name: "Signed copy" });
  }

  it("the front desk can see a signed form but has no way to void it", async () => {
    await signed();
    expect(screen.queryByRole("button", { name: "Void this signed form" })).not.toBeInTheDocument();
  });

  it("voiding a signed form needs permission and a reason, keeps the signed copy visible, and offers a corrected form", async () => {
    server.permissions = [...server.permissions, "VoidForms"];
    await signed();

    await userEvent.click(screen.getByRole("button", { name: "Void this signed form" }));
    await userEvent.type(screen.getByLabelText("Reason (required)"), "Wrong chart");
    await userEvent.click(screen.getByRole("button", { name: "Confirm" }));

    expect(await screen.findByText(/This form is void/)).toBeInTheDocument();
    expect(screen.getByText("Void (was signed)")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Signed copy" })).toBeInTheDocument(); // the signed copy is untouched
    await userEvent.click(screen.getByRole("button", { name: "Start a new form" }));
    await waitFor(() => expect(screen.getByText("Draft - unsigned")).toBeInTheDocument());
  });

  it("a form that belongs to another patient is never shown under this patient", async () => {
    const theirs = server.addReadyDraft(B, T);
    open(`/patients/${A}/forms/${theirs.id}`);
    expect(await screen.findByText("That form was not found for this patient")).toBeInTheDocument();
    expect(screen.queryByText("Preferred name")).not.toBeInTheDocument();
  });

  it("an unknown form id says so", async () => {
    open(`/patients/${A}/forms/nope`);
    expect(await screen.findByText("That form was not found for this patient")).toBeInTheDocument();
  });

  it("switching patients shows the new patient's forms, not the previous patient's", async () => {
    server.addReadyDraft(A, T);
    const bens = server.addReadyDraft(B, T);
    bens.version = { ...bens.version, title: "Ben's own form" };
    open(`/patients/${A}/forms`);
    await screen.findByRole("table", { name: /This patient's forms/ });

    go(`/patients/${B}/forms`);

    await screen.findByRole("link", { name: "Ben's own form" });
    expect(screen.queryByRole("link", { name: "Privacy notice" })).not.toBeInTheDocument();
  });
});

describe("accessibility", () => {
  it("the draft, the review and the signed copy have no detectable violations", async () => {
    const draft = server.addReadyDraft(A, T);
    const { container } = open(`/patients/${A}/forms/${draft.id}`);
    await screen.findByLabelText("Preferred name");
    expect(await axe(container)).toHaveNoViolations();

    await userEvent.click(screen.getByRole("button", { name: "Review and sign" }));
    await screen.findByRole("heading", { name: "Review before signing" });
    expect(await axe(container)).toHaveNoViolations();

    await fillSigner();
    await userEvent.click(screen.getByRole("button", { name: "Sign form" }));
    await screen.findByRole("heading", { name: "Signed copy" });
    expect(await axe(container)).toHaveNoViolations();
  });
});
