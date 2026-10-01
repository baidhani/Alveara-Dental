# ALV-002-C01 R04 - Test Results

Implementation commit `7a318450d22aabc17a75ef4a0a1dd208d4280e95`.

- `npx vitest run`: **188 of 188** (29 files; `AuditLogPage.keyboard.test.tsx` adds 4; the original `AuditLogPage.test.tsx` passes unmodified). `tsc --noEmit` clean; `npm run build` clean; `npm run lint` exit 0 (pre-existing warnings only).
- Real API + real Chromium, overflowing audit data (`artifacts/audit-keyboard-results.json`): both themes - region reachable by Tab, 2px outline, ArrowRight scrolls (0 -> 240px), 0 axe violations, Dentist denied (page and `403`).
- Real-backend/real-browser spec, unmodified, longer timeouts: **12 of 12**.
- Backend: no file changed; approved baseline 375/375.
