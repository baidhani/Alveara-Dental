# ALV-N025 R01: demo evidence

`identity-audit-integrity-real-backend.spec.ts` on a fresh walkthrough database (runner `scripts/Invoke-RealBackendWalkthrough.ps1 -Config playwright.identity-audit.config.ts -OutputVariable IDENTITY_AUDIT_E2E_OUT`): **6/6, 12.6 s**. Output: `artifacts/walkthrough/`.

| Step | What a person sees | Screenshot |
|---|---|---|
| REGISTRATION | Carla Chen is registered and gets an 11-digit MRN. A second Carla Chen with another birth date and the same phone and address is flagged ("Same name and phone number, different birth date"; "Same name and address, different birth date"). "Register anyway" stays disabled until a reason is typed. The audit row names the acknowledged id, never the name. | `01-near-duplicate-flagged.png` |
| MRN | An exact MRN search lists that patient first; the header shows "MRN 63930782135" and Details shows "Patient number (MRN)". | `02-mrn-header.png` |
| ACCESS TRAIL | Opening the record, its History and the record again in one session leaves one PatientRecordOpened row. | - |
| SIGN-OUT | A user signs out with the Sign out button; exactly one UserSignedOut row is written for that account. | - |
| HISTORY LINK (BR-02) | After an edit, an Admin filters the audit page to PatientUpdated, follows "View in patient history" and sees the change. A Billing account granted ViewAuditLog with ViewPatientRecords revoked (through the real policy API) sees the same row with "History is available to staff who may see this patient.", no link and no values. Opening the History URL directly shows "You don't have permission to view this", and the API answers 403. | `03-history-from-audit.png`, `04-history-refused.png` |
| MATRIX (Q-5cl) | An office manager sees the permission matrix with no change buttons. | `05-manager-matrix.png` |

**Not in the browser (owner decision):** the absence of `SessionExpired` after a sign-out. It is proven server-side, because the shortest session timeout is 5 minutes.
