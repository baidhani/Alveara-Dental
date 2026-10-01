# ALV-001-C01 R07 - Test Results

Implementation commit `5c4b5db8200e10276372db09769ebe172ac0f097`.

- `npx vitest run`: **184 of 184** (28 files; 8 new in `adminUsersContrast.test.ts`, which fails against the old CSS). `tsc --noEmit` clean; `npm run build` clean; `npm run lint` exit 0 (pre-existing warnings only).
- Authenticated axe (WCAG 2.0 A/AA tags, unchanged threshold: zero critical/serious), real API + real Chromium, data seeded through the real API, finite CSS animations awaited, default + hover + keyboard-focus: **0 violations** on `/admin/users`, `/admin/users/:id`, `/admin/configuration` (operatories, appointment types) and `/admin/backup`, light and dark (`artifacts/badge-uses-axe-results.json`).
- Real-backend/real-browser spec, unmodified, longer timeouts: **12 of 12** (`artifacts/real-backend-playwright-extended-timeouts.txt`).
- Backend: no file changed; approved baseline 375/375.
