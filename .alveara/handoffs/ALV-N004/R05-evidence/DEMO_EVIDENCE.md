# ALV-N004 R05 — Demo Evidence

Playwright test 10 (`artifacts/playwright-real-backend/run-output.txt`) is unchanged and passes: generated recovery key and passphrase shown once; real backup; isolated restore drill (row reads "Fully verified (restorable)"); restore from the retained archive; target removal; audit entries.

Defect behavior, demonstrable with the hardening tests: after a drill finds an archive defect, the history shows "Verification FAILED" with the reason, and running **Check file hash**, **Verify only**, a wrong key, or even a file-missing/restored cycle leaves that failure and its reason in place, with zero confidence and no restore proof; the API row carries `archiveDefectCode`. Operator procedure: `docs/operations/BACKUP_AND_RECOVERY.md` ("A proven defect is permanent").
