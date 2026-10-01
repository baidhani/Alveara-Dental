# ALV-N004 R03 — Demo Evidence

Unchanged user-visible workflow from R02 (Playwright test 10, `artifacts/playwright-real-backend/run-output.txt`): generated recovery key and passphrase shown once; real backup; isolated restore; restore from the retained archive listed without history; target removal; audit entries.

New behavior demonstrable at the API: a server whose configuration contradicts its database's recorded deployment settings (a) refuses to create a backup (`deployment_mismatch`, visible failed backup, notification), (b) answers domain calls 503 `deployment_mismatch`, and (c) while it has not yet verified (startup, unreachable database) answers 503 `deployment_unverified`; System Status shows the state and the exact settings to apply. A drill of a contradictory archive fails with `restored_database_deployment_matches_manifest` or `deployment_asset_matches_manifest`. Operator procedure: `docs/operations/BACKUP_AND_RECOVERY.md` ("Deployment settings").
