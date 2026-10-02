# ALV-N010 R01 — Test results

Implementation commit `a4c6c4ff5dc98404ecec851cad28206dce9adaf0`; baseline HEAD `5869de7`. Raw output is in `artifacts/`.

| Suite | Result | Output |
|---|---|---|
| Backend `dotnet test` (real SQL Server LocalDB, one database per test class) | **602 of 602**, 0 failed, 0 skipped, 30 m 14 s (up from 492: **110 new**) | `artifacts/test-runs/backend-full.txt` |
| Frontend `npx vitest run` | **371 of 371** (40 files; up from 319: **52 new**) | `artifacts/test-runs/frontend-vitest.txt` |
| Mocked browser `npx playwright test` | **40 of 40** | `artifacts/test-runs/mocked-playwright.txt` |
| Real-backend/real-browser **ALV-N010 forms walkthrough** | **10 of 10**; 14 axe scans (light + dark), **0 violations of any impact** | `artifacts/playwright-real-backend/forms-walkthrough/` (JSON, 8 screenshots) |
| Regression: `ALV-003-C01` workspace walkthrough | **14 of 14** | `artifacts/playwright-real-backend/regression/run-output-ws.txt` |
| Regression: `STORY-003` original walkthrough | **7 of 7** | `…/run-output-s3.txt` |
| Regression: `auth-real-backend` | **12 of 12** | `…/run-output-auth.txt` |
| Regression: Gate A route scans (A2 axe on every shell route, A6 seven-role navigation matrix) | **3 of 3** | `…/run-output-gatea.txt`, `a2-axe-results.json`, `a6-role-navigation.json` |
| Repository checks `node --test tests/*.test.mjs` | **7 of 7** | `artifacts/test-runs/repo-checks.txt` |

**Total: 1066 passing, 0 failing.** Also clean: `tsc -b`, `npm run build`, `oxlint` (no warnings in files this story touched).

## The 110 new backend tests (five classes, real SQL Server)
- `FormTemplateVersionTests` — template/version model: version 1 on create, every category, an edit publishes a new version and leaves the old one exactly as it was, unchanged content publishes nothing, stale/missing row version, two administrators publishing at once (exactly one wins), duplicate/simultaneous key creation, validation of key/title/body/fields/options/limits, inactivate/reactivate/listing, PHI-free audit, **application refusal and database-trigger refusal (SQL error 51011) of any update/delete of a published version**, hash verification.
- `FormSigningTests` — complete and sign, snapshot content equals what was shown, other-signer relationships, history/audit contents, measurement event (and its failure never undoing a signature), **missing signer name/relationship/signature/attestation/required answer**, relationship list, draft rules, stale-after-review and template-version-mismatch refusals, **same key twice = one artifact; different key = `already_signed`; eight simultaneous submits (same key, and different keys) = exactly one snapshot**, missing/short idempotency key, **interrupted signature (audit failure) leaves a plain draft with no receipt, then the same retry succeeds; cancelled request writes nothing**, receipt scoped per form.
- `SignedFormSnapshotTests` — later template edit, template inactivation and a later patient name edit leave the snapshot unchanged; **application code cannot modify/delete a snapshot; the database refuses UPDATE/DELETE from any path (SQL error 51010)**; a signed form cannot be edited; void keeps the snapshot exactly; the hash covers every part.
- `FormLifecycleTests` — start pins the version and is audited; starting twice and eight simultaneous starts leave one open draft; unknown/inactive patient and template refused; **template edit during completion leaves the draft untouched and signable; moving to the newer version carries answers that still fit and records the old draft as replaced**; discard/void need a reason, only `VoidForms` may void a signed form, voiding twice is a no-op, audit without reason text; correction = void + new form; the document-library seam lists signed forms with hash, flags voided ones and exposes no answers.
- `FormsApiTests` — anonymous 401s; per-role access (template administration, starting, reading) in theories over the roles; billing read-only; front desk cannot void a signed form, office manager can; CSRF on every state-changing call; the full HTTP sign/replay/already-signed flow; missing idempotency key and missing signer details as 400s with field messages; stale/missing row versions; template publishing and its 409/400 shapes; available-templates visibility.

## The 52 new frontend tests
`PatientForms.test.tsx` (27, through the real `<App />` against an in-memory fake of the forms API) — tab visibility and permission gating, listing with status in words, start, inactive patient, read-only roles, list error, save draft with version, required answers blocking review, stale save conflict, newer-version offer and move, discard with reason, review contents, refusal of missing signer details, Other-relationship description, successful sign and signed-copy contents, **double-click sends one request, a dropped response shows "outcome unknown" and Sign again reuses the same key**, check status, already-signed by someone else, draft changed after review, back to edit, void permission/reason/signed copy stays/corrected form, wrong-patient form hidden, unknown form, patient switch, and axe on the draft, review and signed copy. `FormTemplates.test.tsx` (13) — listing, permission gating, create, validation/duplicate messages, publish a new version with history, disabled when unchanged, stale edit conflict, choice options, inactivate/reactivate, the no-legal-claim note, retry, axe. `formsContrast.test.ts` (12) — WCAG AA text contrast of every forms pairing in both themes, read from the real CSS and tokens.

## Run notes (honest account)
- A first full backend run was **discarded**: SQL Server LocalDB had stopped, so fixtures failed at setup (no test logic failed). LocalDB was restarted and only the complete second run is counted.
- The first attempt at the regression browser runs failed because my helper script did not create its log folder before starting the API; after the fix all four were re-run from fresh databases.
- During development several of my own tests were wrong (not the product): a full-replacement draft save sent partial answers, a raw SQL literal contained braces, the HTTP harness bootstrapped two administrators, a selector matched two elements. All fixed; the final counts above are from the final code only.
- Not re-run (unaffected by this story): Gate A A1/A9 specs and the recovery-key timing spec.
