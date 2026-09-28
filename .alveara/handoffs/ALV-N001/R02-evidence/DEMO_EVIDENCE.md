# ALV-N001 R02 — Demo Evidence

R01's demo evidence explicitly substituted jsdom + curl checks for a genuine browser demonstration; the reviewer correctly rejected that substitution for the responsive/visible-demonstration acceptance item. This attempt replaces it with real evidence.

## Genuine browser demonstration (Playwright + Chromium, headless but a real rendering engine)

Run via `npm run build && npm run test:e2e` (Playwright's `webServer` config serves the actual production build via `npm run preview`, then drives real Chromium against it):

- **Navigation:** dashboard → showcase via a real click; every showcase control (buttons, form field, loading/empty/error states — implicitly via the showcase render) confirmed visible.
- **Keyboard-only path:** real `Tab`/`Enter` key presses in the browser reach and activate the skip link, then the Component Showcase nav link.
- **Unknown route:** real navigation to a non-existent path renders the not-found page; the single `ButtonLink` control is clicked and returns to the dashboard.
- **Form validation:** real blur events in the browser trigger and clear the validation message.
- **Notifications:** real button clicks produce visible toast text.
- **Connected/disconnected/recovery:** real Chromium requests to a Playwright-mocked `/api/health` route produce the visible disconnected banner when down, no banner when healthy, and the banner clears after a simulated recovery (reload against a now-healthy mock).
- **Both themes:** the real theme-toggle button is clicked in the browser; the resulting `data-theme` attribute change is asserted and shown to persist across a real page reload.
- **Both viewports:** every test above runs twice — once at 1280×800 (desktop), once at 768×1024 (tablet) — via Playwright's two configured projects, with an explicit structural assertion that the sidebar genuinely collapses to a top bar at the tablet width.
- **Accessibility, both themes, three pages:** real-browser axe scans (WCAG 2 A/AA) against Dashboard, Showcase, and Not-Found, each in light and dark mode — 12 tests, 0 critical/serious violations, including the dark-mode contrast defect that the previous jsdom-only dashboard check could not have caught.

## Why this now counts as "demonstrated"

Playwright drives a real Chromium browser process — actual layout, actual paint, actual CSS cascade, actual focus/keyboard event dispatch — not a DOM simulation. This is a genuine, reproducible, recorded (via Playwright's test report) browser walkthrough, run against the real production build, satisfying the reviewer's explicit requirement without needing a manual screenshot session.

## Manual dev-server reproduction of the fixed connectivity defect

See `TEST_RESULTS.md`'s "Manual runtime reproduction" section: the real Vite dev server + real ASP.NET Core API were started, and the exact failure scenario the reviewer reproduced (relative `/api/health` returning a false-positive 200) was reproduced and then confirmed fixed. Both processes were stopped after verification.
