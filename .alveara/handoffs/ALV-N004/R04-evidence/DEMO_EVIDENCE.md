# ALV-N004 R04 — Demo Evidence

Playwright test 10 (`artifacts/playwright-real-backend/run-output.txt`): generated recovery key and passphrase shown once; real backup; isolated restore drill; the history row then reads **"Fully verified (restorable)"** (a drill earned restore proof); restore from the retained archive listed without history; target removal; audit entries.

New visible behavior: a verify-only result reads **"Backup contents verified (restore drill still needed)"** and states it does NOT prove the data restores into a consistent application and does not count toward trust; the history row shows "Contents verified - restore drill still needed" until a drill passes; probation text counts restore drills only. A backup a drill proved defective shows **Verification FAILED** with the reason and stays that way. Operator procedure: `docs/operations/BACKUP_AND_RECOVERY.md` ("Verification levels", "Probation", "A proven defect is permanent").
