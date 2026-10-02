/**
 * ALV-N010: typed client for the versioned forms/consents API (Controllers/FormsController).
 *
 * - Every change echoes the `rowVersion` it read; a stale one comes back as the shared 409 concurrency-conflict shape
 *   (see authApi.isConcurrencyConflict).
 * - Signing sends an `Idempotency-Key`: the caller makes one key when the review screen opens and reuses it on every retry of that
 *   same signature, so a retry after a dropped connection can never sign twice (the server replays the first result).
 * - A signed form is never edited; a correction is a void plus a new form.
 */
import { ApiError, fetchCsrfToken, request, requestWithCsrf } from "./authApi";

export const FORM_CATEGORIES = ["Privacy", "Financial", "GeneralConsent", "Treatment"] as const;
export const CATEGORY_LABELS: Record<string, string> = { Privacy: "Privacy", Financial: "Financial", GeneralConsent: "General consent", Treatment: "Treatment" };
export const FIELD_KINDS = ["text", "longText", "checkbox", "choice", "date"] as const;
export const KIND_LABELS: Record<string, string> = { text: "Short text", longText: "Long text", checkbox: "Checkbox", choice: "Choice", date: "Date" };
export const SIGNER_RELATIONSHIPS = ["Self", "Parent", "Legal guardian", "Spouse", "Legal representative", "Other"] as const;

/** The statement the signer agrees to; the server stores the same text verbatim in every snapshot. */
export const ATTESTATION =
  "I have read this form and the answers shown are correct. I am signing it as the person identified above, and I understand that typing my name here is my signature.";

export interface FormField {
  id: string;
  label: string;
  kind: string;
  required: boolean;
  options?: string[] | null;
}

export interface TemplateVersion {
  id: string;
  versionNumber: number;
  title: string;
  body: string;
  fields: FormField[];
  contentHash: string;
  changeNote: string | null;
  createdAtUtc: string;
  integrityVerified: boolean;
}

export interface TemplateSummary {
  id: string;
  key: string;
  category: string;
  isActive: boolean;
  rowVersion: string;
  current: TemplateVersion | null;
  versionCount: number;
  createdAtUtc: string;
  updatedAtUtc: string | null;
}

export interface TemplateDetail {
  template: TemplateSummary;
  versions: TemplateVersion[];
}

export interface TemplateInput {
  key: string;
  category: string;
  title: string;
  body: string;
  fields: FormField[];
  changeNote: string;
}

export interface FormSnapshot {
  id: string;
  patientFormId: string;
  templateVersionNumber: number;
  templateKey: string;
  category: string;
  title: string;
  body: string;
  fields: FormField[];
  responses: Record<string, string>;
  signerName: string;
  signerRelationship: string;
  signerRelationshipNote: string | null;
  signatureMethod: string;
  signatureText: string;
  attestation: string;
  signedAtUtc: string;
  capturedByUserId: string | null;
  snapshotHash: string;
  integrityVerified: boolean;
}

export interface FormEvent {
  eventType: string;
  actorUserId: string | null;
  occurredAtUtc: string;
  templateVersionNumber: number;
  detail: string | null;
}

export type FormStatus = "Draft" | "Signed" | "Void";

export interface PatientFormSummary {
  id: string;
  patientId: string;
  templateId: string;
  templateKey: string;
  category: string;
  title: string;
  templateVersionNumber: number;
  status: FormStatus;
  startedAtUtc: string;
  signedAtUtc: string | null;
  voidedAtUtc: string | null;
  voidReason: string | null;
  wasSigned: boolean;
  signerName: string | null;
  signerRelationship: string | null;
  newerVersionAvailable: boolean;
}

export interface PatientFormDetail {
  summary: PatientFormSummary;
  rowVersion: string;
  version: TemplateVersion;
  responses: Record<string, string>;
  snapshot: FormSnapshot | null;
  events: FormEvent[];
  newerVersion: TemplateVersion | null;
}

export interface SignInput {
  signerName: string;
  relationship: string;
  relationshipNote: string;
  signatureText: string;
  attested: boolean;
  templateVersionId: string;
  rowVersion: string;
}

/** One key per signature attempt (made when the review screen opens, reused on every retry of it). */
export const newIdempotencyKey = () => `sign-${crypto.randomUUID()}`;

// ---------- templates ----------
export const listTemplates = (signal?: AbortSignal) => request<TemplateSummary[]>("/api/forms/templates", { signal });
export const listAvailableTemplates = (signal?: AbortSignal) => request<TemplateSummary[]>("/api/forms/templates/available", { signal });
export const getTemplate = (id: string, signal?: AbortSignal) => request<TemplateDetail>(`/api/forms/templates/${id}`, { signal });
export const createTemplate = (input: TemplateInput) => requestWithCsrf<TemplateDetail>("/api/forms/templates", "POST", input);
export const publishTemplateVersion = (id: string, input: TemplateInput, rowVersion: string) =>
  requestWithCsrf<TemplateDetail>(`/api/forms/templates/${id}`, "PUT", { ...input, rowVersion });
export const setTemplateActive = (id: string, isActive: boolean, rowVersion: string) =>
  requestWithCsrf<TemplateDetail>(`/api/forms/templates/${id}/active`, "PUT", { isActive, rowVersion });

// ---------- a patient's forms ----------
export const listPatientForms = (patientId: string, signal?: AbortSignal) => request<PatientFormSummary[]>(`/api/patients/${patientId}/forms`, { signal });
export const getPatientForm = (formId: string, signal?: AbortSignal) => request<PatientFormDetail>(`/api/forms/${formId}`, { signal });
export const startPatientForm = (patientId: string, templateId: string) =>
  requestWithCsrf<PatientFormDetail>(`/api/patients/${patientId}/forms`, "POST", { templateId });
export const saveFormResponses = (formId: string, responses: Record<string, string>, rowVersion: string) =>
  requestWithCsrf<PatientFormDetail>(`/api/forms/${formId}/responses`, "PUT", { responses, rowVersion });
export const restartFormOnLatest = (formId: string, rowVersion: string) =>
  requestWithCsrf<PatientFormDetail>(`/api/forms/${formId}/restart`, "POST", { rowVersion });
export const voidPatientForm = (formId: string, reason: string, rowVersion: string) =>
  requestWithCsrf<PatientFormDetail>(`/api/forms/${formId}/void`, "POST", { reason, rowVersion });

export async function signPatientForm(formId: string, input: SignInput, idempotencyKey: string): Promise<PatientFormDetail> {
  const csrfToken = await fetchCsrfToken();
  return request<PatientFormDetail>(`/api/forms/${formId}/sign`, {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-CSRF-Token": csrfToken, "Idempotency-Key": idempotencyKey },
    body: JSON.stringify(input),
  });
}

/** The per-field messages of a 400 `validation_failed` response (empty for any other error). */
export function formFieldErrorsOf(err: unknown): Record<string, string> {
  if (!(err instanceof ApiError) || err.code !== "validation_failed") return {};
  const raw = err.body.fieldErrors;
  return raw && typeof raw === "object" ? (raw as Record<string, string>) : {};
}

/**
 * True when a failed request may or may not have reached the server (no HTTP response at all). A signature that fails this way might
 * have been stored, so the UI tells the user a retry cannot sign twice and keeps the same key.
 */
export const isNetworkFailure = (err: unknown) => !(err instanceof ApiError);

export const emptyField = (): FormField => ({ id: "", label: "", kind: "text", required: false, options: null });
