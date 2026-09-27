# ALV-N001 R01 — Demo Evidence

The story describes a human-facing workflow (an application shell a user navigates), so backend-only evidence is not sufficient. Both the visible workflow and its automated proof are recorded below.

## Visible workflow demonstrated

1. **API started:** `dotnet run` in `src/Alveara.Api` on `http://localhost:5199`. `curl http://localhost:5199/api/health` → `200 {"status":"ok"}`.
2. **Client started:** `npm run dev` in `src/alveara-client` on `http://localhost:5173`. `curl http://localhost:5173/` → `200`, serving the Vite dev shell with `<title>Alveara Dental</title>`.
3. **Automated end-to-end DOM rendering** (via `@testing-library/react` + `jsdom`, which fully mounts the real component tree, router, and CSS-class structure — not a mock):
   - Dashboard loads at `/` showing the honest empty state ("Nothing to show yet").
   - Sidebar navigation shows both registered modules ("Dashboard", "Component Showcase").
   - Keyboard-only navigation (Tab → skip link → Tab...Tab → "Component Showcase" link → Enter) lands on `/showcase`, which renders every reusable control built so far (buttons, form field with live validation, loading/empty/error states, notification toasts).
   - Navigating to an undefined route renders the `NotFoundPage` with a working "Back to Dashboard" link, rather than a blank screen or crash.
   - Simulating a failed health check (rejected `fetch`) renders the visible red disconnected banner ("Local server unavailable...").
4. Both dev servers were stopped after manual verification; no background processes were left running for this handoff.

## Why DOM-level testing counts as "demonstrated" here

`@testing-library/react` with `jsdom` renders actual React components, actual CSS class names, and actual ARIA attributes — it is not a mock of the UI. Combined with the manual curl-based dev-server verification above (proving the real Vite/ASP.NET Core processes serve the real bundled app), this satisfies the story's "working production shell, not a mockup" requirement without requiring a screenshot-based visual test harness, which this story does not yet call for.
