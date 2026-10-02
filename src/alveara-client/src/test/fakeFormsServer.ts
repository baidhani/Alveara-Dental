import { FakePatientServer, json } from "./fakePatientServer";
import type { Call } from "./fakePatientServer";
import type { FormField, FormSnapshot, PatientFormDetail, PatientFormSummary, TemplateDetail, TemplateSummary, TemplateVersion } from "../services/formsApi";

/**
 * ALV-N010: an in-memory stand-in for the forms API, on top of the fake patient server. It models the response SHAPES (same as
 * Controllers/FormsController), row-version checking (a stale write is the shared 409), the idempotent signature (the same key replays
 * the first result; another key on a signed form is 409 already_signed), and canned failures - including `dropNextSignResponse`, which
 * stores the signature and then fails the response like a dropped connection. The real rules are proven by the backend tests and the
 * real-backend walkthrough; this exists so the UI's handling of each outcome can be tested in isolation.
 */
interface StoredForm {
  id: string; patientId: string; templateId: string; version: TemplateVersion; status: "Draft" | "Signed" | "Void";
  responses: Record<string, string>; v: number; startedAtUtc: string; voidReason: string | null; voidedAtUtc: string | null;
  snapshot: FormSnapshot | null; events: PatientFormDetail["events"];
}
interface StoredTemplate { id: string; key: string; category: string; isActive: boolean; v: number; versions: TemplateVersion[]; required?: boolean }

export class FakeFormsServer extends FakePatientServer {
  templates = new Map<string, StoredTemplate>();
  forms = new Map<string, StoredForm>();
  formCalls: Call[] = [];
  /** Permissions of the signed-in user (forms ones added to the patient defaults). */
  constructor() {
    super();
    this.permissions = ["ViewPatientRecords", "RegisterPatients", "EditPatients", "CompleteForms", "ViewSignedForms"];
  }
  dropNextSignResponse = false;
  private injectedForms = new Map<string, Response[]>();
  /** The NEXT request matching `METHOD path-prefix` gets this response (e.g. a 500). */
  failForms(methodAndPath: string, response: Response) {
    this.injectedForms.set(methodAndPath, [...(this.injectedForms.get(methodAndPath) ?? []), response]);
  }
  private receipts = new Map<string, string>();
  private seq = 0;

  static fields(): FormField[] {
    return [
      { id: "acknowledged", label: "I have read the privacy notice", kind: "checkbox", required: true },
      { id: "nickname", label: "Preferred name", kind: "text", required: false },
      { id: "contact", label: "Preferred contact", kind: "choice", required: false, options: ["Email", "Phone"] },
    ];
  }

  addTemplate(key: string, over: { id?: string; category?: string; title?: string; body?: string; fields?: FormField[]; isActive?: boolean } = {}): StoredTemplate {
    const id = over.id ?? `t-${++this.seq}`;
    const t: StoredTemplate = { id, key, category: over.category ?? "Privacy", isActive: over.isActive ?? true, v: 1, versions: [] };
    this.templates.set(id, t);
    this.publish(id, over.title ?? "Privacy notice", over.body ?? "We protect your information.", over.fields ?? FakeFormsServer.fields());
    return t;
  }

  /** Publishes a new version (as the real API does on an edit). */
  publish(templateId: string, title: string, body: string, fields: FormField[]) {
    const t = this.templates.get(templateId)!;
    t.versions.unshift({
      id: `${templateId}-v${t.versions.length + 1}`, versionNumber: t.versions.length + 1, title, body, fields, contentHash: "h", changeNote: null,
      createdAtUtc: "2026-10-02T09:00:00Z", integrityVerified: true,
    });
    t.v++;
  }

  private current(t: StoredTemplate) { return t.versions[0]; }
  private summaryOfTemplate(t: StoredTemplate): TemplateSummary {
    return { id: t.id, key: t.key, category: t.category, isActive: t.isActive, rowVersion: `t${t.v}`, current: this.current(t), versionCount: t.versions.length, createdAtUtc: "2026-10-01T09:00:00Z", updatedAtUtc: null, requiredAtCheckIn: !!t.required };
  }
  private detailOfTemplate(t: StoredTemplate): TemplateDetail { return { template: this.summaryOfTemplate(t), versions: t.versions }; }

  private formSummaryOf(f: StoredForm): PatientFormSummary {
    const t = this.templates.get(f.templateId)!;
    return {
      id: f.id, patientId: f.patientId, templateId: f.templateId, templateKey: t.key, category: t.category, title: f.version.title, templateVersionNumber: f.version.versionNumber,
      status: f.status, startedAtUtc: f.startedAtUtc, signedAtUtc: f.snapshot?.signedAtUtc ?? null, voidedAtUtc: f.voidedAtUtc, voidReason: f.voidReason, wasSigned: f.snapshot !== null,
      signerName: f.snapshot?.signerName ?? null, signerRelationship: f.snapshot?.signerRelationship ?? null,
      newerVersionAvailable: f.status === "Draft" && t.isActive && this.current(t).id !== f.version.id,
    };
  }
  private formDetailOf(f: StoredForm): PatientFormDetail {
    const t = this.templates.get(f.templateId)!;
    const newer = this.formSummaryOf(f).newerVersionAvailable ? this.current(t) : null;
    return { summary: this.formSummaryOf(f), rowVersion: `f${f.v}`, version: f.version, responses: f.responses, snapshot: f.snapshot, events: f.events, newerVersion: newer };
  }

  private newDraft(patientId: string, t: StoredTemplate, responses: Record<string, string> = {}): StoredForm {
    const f: StoredForm = {
      id: `f-${++this.seq}`, patientId, templateId: t.id, version: this.current(t), status: "Draft", responses, v: 1,
      startedAtUtc: "2026-10-02T10:00:00Z", voidReason: null, voidedAtUtc: null, snapshot: null,
      events: [{ eventType: "Started", actorUserId: "u1", occurredAtUtc: "2026-10-02T10:00:00Z", templateVersionNumber: this.current(t).versionNumber, detail: null }],
    };
    this.forms.set(f.id, f);
    return f;
  }

  /** Test helper: a draft with every answer given, ready to sign. */
  addReadyDraft(patientId: string, templateId: string): StoredForm {
    return this.newDraft(patientId, this.templates.get(templateId)!, { acknowledged: "true", nickname: "Annie", contact: "Email" });
  }

  callsToForms(method: string, pathPrefix: string) {
    return this.formCalls.filter((c) => c.method === method && new URL(c.url, "http://x").pathname.startsWith(pathPrefix));
  }

  private stale = (f: { v: number }, body: Record<string, unknown> | null, prefix: "f" | "t") =>
    body?.rowVersion !== `${prefix}${f.v}`;
  private conflict = (entityType: string, id: string) => json(409, { error: "concurrency_conflict", entityType, entityId: id, message: "Changed by someone else." });

  protected override async handleExtra(path: string, method: string, headers: Record<string, string>, body: Record<string, unknown> | null, u: URL): Promise<Response | null> {
    const isForms = path.startsWith("/api/forms") || /^\/api\/patients\/[^/]+\/(forms|signed-documents)/.test(path);
    if (!isForms) return null;
    this.formCalls.push({ method, url: u.pathname + u.search, headers, body });
    for (const [key, queue] of this.injectedForms) {
      const [m, prefix] = key.split(" ");
      if (m === method && path.startsWith(prefix) && queue.length > 0) return queue.shift()!;
    }
    const parts = path.split("/");

    // ----- templates -----
    if (path === "/api/forms/templates" && method === "GET") return json(200, [...this.templates.values()].map((t) => this.summaryOfTemplate(t)));
    if (path === "/api/forms/templates/available" && method === "GET")
      return json(200, [...this.templates.values()].filter((t) => t.isActive).map((t) => this.summaryOfTemplate(t)));
    if (path === "/api/forms/templates" && method === "POST") {
      const key = String(body?.key ?? "");
      if (!/^[a-z0-9][a-z0-9-]{1,58}[a-z0-9]$/.test(key)) return json(400, { error: "validation_failed", message: "Some fields need attention.", fieldErrors: { key: "Use 3-60 lowercase letters, digits and hyphens." } });
      if ([...this.templates.values()].some((t) => t.key === key)) return json(409, { error: "template_key_taken", message: `A template with the key '${key}' already exists.` });
      const t = this.addTemplate(key, { category: String(body?.category), title: String(body?.title), body: String(body?.body), fields: body?.fields as FormField[] });
      return json(201, this.detailOfTemplate(t));
    }
    if (parts[3] === "templates" && parts[4]) {
      const t = this.templates.get(parts[4]);
      if (!t) return json(404, { error: "template_not_found", message: "That form template was not found." });
      if (method === "GET") return json(200, this.detailOfTemplate(t));
      if (parts[5] === "required-at-check-in" && body!.required === !!t.required) return json(200, this.detailOfTemplate(t)); // a repeat changes nothing
      if (this.stale(t, body, "t")) return this.conflict("FormTemplate", t.id);
      if (parts[5] === "required-at-check-in") { t.required = body!.required as boolean; t.v++; return json(200, this.detailOfTemplate(t)); }
      if (parts[5] === "active") { t.isActive = body!.isActive as boolean; t.v++; return json(200, this.detailOfTemplate(t)); }
      const fields = body?.fields as FormField[];
      const cur = this.current(t);
      if (String(body?.title) !== cur.title || String(body?.body) !== cur.body || JSON.stringify(fields) !== JSON.stringify(cur.fields))
        this.publish(t.id, String(body?.title), String(body?.body), fields);
      return json(200, this.detailOfTemplate(t));
    }

    // ----- a patient's forms -----
    const patientMatch = /^\/api\/patients\/([^/]+)\/forms$/.exec(path);
    if (patientMatch) {
      const patientId = patientMatch[1];
      if (method === "GET") return json(200, [...this.forms.values()].filter((f) => f.patientId === patientId).map((f) => this.formSummaryOf(f)));
      const t = this.templates.get(String(body?.templateId));
      if (!t) return json(404, { error: "template_not_found", message: "That form template was not found." });
      const open = [...this.forms.values()].find((f) => f.patientId === patientId && f.templateId === t.id && f.status === "Draft");
      if (open) return json(200, this.formDetailOf(open));
      return json(201, this.formDetailOf(this.newDraft(patientId, t)));
    }
    if (/\/signed-documents$/.test(path)) return json(200, []);

    // ----- one form -----
    const f = this.forms.get(parts[3] ?? "");
    if (!f) return json(404, { error: "form_not_found", message: "That form was not found." });
    if (method === "GET") return json(200, this.formDetailOf(f));
    const action = parts[4];

    if (action === "responses") {
      if (f.status !== "Draft") return json(409, { error: "form_not_draft", message: "Only a draft can be changed." });
      if (this.stale(f, body, "f")) return this.conflict("PatientForm", f.id);
      f.responses = Object.fromEntries(Object.entries((body?.responses ?? {}) as Record<string, string>).filter(([, v]) => v !== ""));
      f.v++;
      return json(200, this.formDetailOf(f));
    }
    if (action === "restart") {
      if (this.stale(f, body, "f")) return this.conflict("PatientForm", f.id);
      const t = this.templates.get(f.templateId)!;
      f.status = "Void"; f.voidReason = `Replaced by version ${this.current(t).versionNumber} of the template.`; f.voidedAtUtc = "2026-10-02T11:00:00Z"; f.v++;
      f.events.push({ eventType: "Superseded", actorUserId: "u1", occurredAtUtc: "2026-10-02T11:00:00Z", templateVersionNumber: f.version.versionNumber, detail: f.voidReason });
      const carried = Object.fromEntries(Object.entries(f.responses).filter(([k]) => this.current(t).fields.some((x) => x.id === k)));
      return json(200, this.formDetailOf(this.newDraft(f.patientId, t, carried)));
    }
    if (action === "void") {
      if (f.status === "Void") return json(200, this.formDetailOf(f));
      if (!String(body?.reason ?? "").trim()) return json(400, { error: "validation_failed", message: "A reason is required.", fieldErrors: { reason: "A reason is required." } });
      if (f.status === "Signed" && !this.permissions.includes("VoidForms")) return json(403, { error: "void_not_permitted", message: "You do not have permission to void a signed form." });
      if (this.stale(f, body, "f")) return this.conflict("PatientForm", f.id);
      f.status = "Void"; f.voidReason = String(body!.reason); f.voidedAtUtc = "2026-10-02T11:00:00Z"; f.v++;
      f.events.push({ eventType: "Voided", actorUserId: "u1", occurredAtUtc: "2026-10-02T11:00:00Z", templateVersionNumber: f.version.versionNumber, detail: f.voidReason });
      return json(200, this.formDetailOf(f));
    }
    if (action === "sign") return this.sign(f, headers, body);
    return json(404, { error: "not_found" });
  }

  private sign(f: StoredForm, headers: Record<string, string>, body: Record<string, unknown> | null): Response {
    const key = headers["Idempotency-Key"];
    if (!key || key.length < 8) return json(400, { error: "idempotency_key_required", message: "A signature needs an Idempotency-Key." });
    const receipt = `${f.id}:${key}`;
    if (this.receipts.has(receipt)) return json(200, this.formDetailOf(f)); // the same submit again: the first result
    if (f.status === "Void") return json(409, { error: "form_void", message: "This form has been voided." });
    if (f.status === "Signed") return json(409, { error: "already_signed", message: "This form has already been signed.", existingId: f.snapshot!.id });
    if (this.stale(f, body, "f")) return this.conflict("PatientForm", f.id);

    const errors: Record<string, string> = {};
    for (const fd of f.version.fields) if (fd.required && !f.responses[fd.id]) errors[`responses.${fd.id}`] = "This is required.";
    if (!String(body?.signerName ?? "").trim()) errors.signerName = "The signer's name is required.";
    if (!String(body?.relationship ?? "")) errors.relationship = "Say how the signer relates to the patient.";
    if (body?.relationship === "Other" && !String(body?.relationshipNote ?? "").trim()) errors.relationshipNote = "Please describe the relationship is required.";
    if (!String(body?.signatureText ?? "").trim()) errors.signatureText = "The typed signature is required.";
    if (body?.attested !== true) errors.attested = "The signer must confirm the statement before signing.";
    if (Object.keys(errors).length > 0) return json(400, { error: "validation_failed", message: "Some details are missing.", fieldErrors: errors });

    f.snapshot = {
      id: `s-${f.id}`, patientFormId: f.id, templateVersionNumber: f.version.versionNumber, templateKey: this.templates.get(f.templateId)!.key, category: this.templates.get(f.templateId)!.category,
      title: f.version.title, body: f.version.body, fields: f.version.fields, responses: { ...f.responses }, signerName: String(body!.signerName), signerRelationship: String(body!.relationship),
      signerRelationshipNote: body!.relationship === "Other" ? String(body!.relationshipNote) : null, signatureMethod: "typed-name", signatureText: String(body!.signatureText),
      attestation: "I have read this form and the answers shown are correct.", signedAtUtc: "2026-10-02T10:30:00Z", capturedByUserId: "u1", snapshotHash: "ab".repeat(32), integrityVerified: true,
    };
    f.status = "Signed"; f.v++;
    f.events.push({ eventType: "Signed", actorUserId: "u1", occurredAtUtc: "2026-10-02T10:30:00Z", templateVersionNumber: f.version.versionNumber, detail: `Signed by ${f.snapshot.signerName} (${f.snapshot.signerRelationship}).` });
    this.receipts.set(receipt, f.snapshot.id);
    if (this.dropNextSignResponse) {
      this.dropNextSignResponse = false;
      throw new TypeError("Failed to fetch"); // the signature was stored, but the response never arrived
    }
    return json(201, this.formDetailOf(f));
  }
}
