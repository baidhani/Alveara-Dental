# ALV-N001 R01 — Next Story Impact

## Assumptions the next stories can rely on

- `src/Alveara.Api` is the single ASP.NET Core Web API project; new controllers/endpoints should be added there, not in a new project, unless a later story explicitly justifies splitting it.
- `src/alveara-client` is the single React/TypeScript client; new pages go under `src/pages/`, new reusable components under `src/components/`, following the existing naming/CSS-co-location pattern (`Component.tsx` + `Component.css`).
- All colour/spacing/typography/radius/elevation/density values must come from `src/styles/tokens.css` variables — no component should introduce a literal hex/px value.
- `moduleRegistry.ts` (`src/app/moduleRegistry.ts`) is the single source of truth for shell navigation entries. Adding a new module means adding an entry here, not inventing a second navigation list.

## Interfaces the next story will touch

- **`ALV-N002`** (Core Architecture, Local Deployment, Data Invariants, Durable Background Work) — will add persistence/database/background-job infrastructure to `src/Alveara.Api`. The existing `GET /api/health` endpoint should remain a pure liveness check; a deeper dependency-health check (database reachability, etc.) should be a separate endpoint or an additive field, not a replacement, so the client's existing `useConnectionStatus` contract (`ok` → connected) doesn't silently change meaning.
- **`ALV-N009`** (Authorization-Aware Navigation, Session UX, Identity Context) — will replace the "no permission filtering" behavior in `moduleRegistry.ts`/`AppShell.tsx` with real role-based filtering. The registry's shape (`{ id, label, path }`) should be extended (e.g. with a `requiredPermission` field) rather than replaced, to avoid a structural rewrite (per the story's own security requirement: "UI controls must be designed so later server authorization can drive them without structural rewrite").
- **Patient-context region** (`AppShell.tsx`'s `.alv-shell__patient-context` placeholder) will be replaced by the real patient-context header once `ALV-003-C01` (Patient Identity/Household/Registration Workspace) exists. Later modules should extend this same region rather than creating a parallel patient-context UI.

## Migrations

None — no database/persistence exists yet.

## UI extension points

- `ShowcasePage` should keep growing as new reusable components are added by later stories, so there is always one page demonstrating every control that exists.
- New page-level routes are added to `App.tsx`'s `<Routes>`; the shared `AppShell` layout wraps all of them automatically via the parent `<Route element={<AppShell />}>`.

## Unresolved decisions relevant to the next story

- Exact database engine (SQLite vs. SQL Server/PostgreSQL) for the local Windows server was not decided by this story — that decision belongs to `ALV-N002` per the Execution Plan's phase ownership.
- Whether the production deployment serves the built React client from the same origin as the API (removing the need for the CORS policy in production) should be confirmed/implemented by the Windows installation story (`ALV-N013`), not assumed here.
