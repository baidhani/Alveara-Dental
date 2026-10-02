# ALV-N010 R01 — Demo evidence

Demo result: **verified**. A real Chromium against the real Vite proxy, the real `Alveara.Api` and a real LocalDB database — nothing mocked (`e2e/forms-real-backend.spec.ts`, run with `playwright.forms.config.ts`; **10 of 10**). Users: an office manager, a front-desk user, a dentist and a billing user. Results: `artifacts/playwright-real-backend/forms-walkthrough/forms-e2e.json`.

| Test | What it shows | Screenshot |
|---|---|---|
| TEMPLATES | An office manager builds a versioned privacy template through the UI (title, wording, a required checkbox and a text question); the front desk gets "don't have permission" in the UI and 403 from the API. | `01-template-editor.png` |
| COMPLETE | The front desk starts the form for a patient; it shows "Draft - unsigned"; a saved draft survives a reload; a missing required answer ("This must be checked.") blocks review. | `02-draft.png` |
| TEMPLATE CHANGE DURING COMPLETION | The manager publishes version 2; the open draft still shows version 1 wording and offers "Move to version 2". | `03-newer-version-offered.png` |
| SIGN | Review shows the wording and answers that will be signed; signing with nothing entered is refused with a message per field; a **keyboard-only** signature (Space, Enter) records signer, relationship "Parent" and template version 1; the signed copy shows an integrity result and the SHA-256 fingerprint and is read-only. | `04-review.png`, `05-signed.png` |
| TEMPLATE EDIT AFTER SIGNING / IMMUTABLE | After version 3 with completely different wording the signed copy still shows version 1 wording and the identical fingerprint; replaying the same signature returns the first result (200), a different key is 409 `already_signed`, and the patient has exactly one signed document. | — |
| INTERRUPTED SIGNATURE | The server stores the signature but the response is lost; the screen says it could not confirm the outcome; "Sign again" (same idempotency key) ends with exactly one `Signed` event. | `06-unknown-outcome.png` |
| VOID | The front desk has no void control and the API returns 403; the office manager voids with a reason (an empty reason is refused); the signed copy stays visible; the list shows Signed and "Void (was signed)". | `07-void.png`, `08-forms-list.png` |
| ROLES | A dentist can start a form; billing can read forms but has no start control and the API returns 403. | — |
| PATIENT SWITCH | Another patient's Forms tab is empty; a form id opened under the wrong patient is "not found for this patient" and shows nothing of the real patient's. | — |

Accessibility: **14 axe scans** (template admin, draft, review with errors, signed copy, unknown outcome, void, forms list; each in light and dark) — **0 violations of any impact**.
