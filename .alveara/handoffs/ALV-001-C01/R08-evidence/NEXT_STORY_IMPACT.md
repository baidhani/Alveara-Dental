# ALV-001-C01 R08 - Next Story Impact

Status text on tinted backgrounds uses `--color-warning-text` / `--color-success-text`; do not paint inline colour styles (the test forbids it on the auth pages). `--color-*-surface` tokens are still undefined; the stylesheets fall back to hard-coded tints, which `contrastSupport.ts` resolves. Next: `ALV-N004` (backup warning text, `BackupRecoveryPage.css`, 3.08:1 light, plus the intermittent stale-settings-save test). Then Gate B. No Execution Plan prompt change is required.
