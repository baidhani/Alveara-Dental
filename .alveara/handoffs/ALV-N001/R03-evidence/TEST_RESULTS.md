# ALV-N001 R03 - Test Results

Implementation commit `70bb64a53edcf0c82a8c2cc60f038ebd37c29c77`.

- `npx vitest run`: **176 of 176 passing** (28 files; the new `buttonContrast.test.ts` contributes 18). The test fails against the previous `Button.css`/`tokens.css`.
- `npx tsc --noEmit`: clean. `npm run build`: clean. `npm run lint`: exit 0 (pre-existing warnings only).
- Authenticated axe scan against the real API (fresh migrated database, real Chromium): shell routes `/`, `/showcase`, `/system-status`, `/admin/permissions`, `/admin/configuration`, `/admin/backup`, `/settings/mfa`, not-found; light and dark; default, hover and keyboard-focus states; finite CSS animations awaited before measuring: **0 critical/serious across 48 scans** (`artifacts/n001-axe-state-scan.json`).
- Real-data revalidation scan (`artifacts/badge-uses-axe-results.json`): remaining violations are exclusively `.alv-status-badge` (light theme) and security-user "View" links (dark theme) - the `ALV-001-C01` R07 scope; no button-related violation remains.
- Backend: unchanged (no backend file touched); approved baseline 375/375.
