# ALV-N019 dependency advisory report (R05 F-001)

Session CC-20261109-8w3d. Date of the queries: 2026-11-10. Read-only: no package, lock file or project file was changed, and `npm audit fix` was not run (reviewer decision O-7: no update this attempt, report first).

## Commands run

| Command (working folder) | What it asks | Result |
|---|---|---|
| `dotnet list package --vulnerable --include-transitive` (`src/Alveara.Api`) | NuGet's vulnerability feed (`api.nuget.org`) | 1 vulnerable package, below |
| `dotnet nuget why Alveara.Api.csproj System.Security.Cryptography.Pkcs` | the project's own dependency graph | path below |
| `npm audit --json` (`src/alveara-client`) | the npm advisory service | 5 vulnerable packages: 2 moderate, 3 high, 0 critical |
| `npm audit --omit=dev --json` (`src/alveara-client`) | the same, production dependencies only | **0 vulnerabilities** |

## Shipped dependency: the API (NuGet)

| Package | Resolved | Severity | Advisories | How it gets in |
|---|---|---|---|---|
| `System.Security.Cryptography.Pkcs` | 9.0.18 | High and moderate | GHSA-846f-f764-76mx (high), GHSA-2fmv-rv2c-59x7 (moderate) | Transitive, not a direct `PackageReference`: `Microsoft.Data.SqlClient` 7.1.0 requires it, and `Microsoft.EntityFrameworkCore.SqlServer` 10.0.12 brings `Microsoft.Data.SqlClient`. |

- This is code that ships in the API. It is the F-001 finding and it is still open.
- **Not established in this report:** what the two advisories describe, whether the API reaches the affected code, and whether a fixed `Pkcs` version is available through a newer `Microsoft.Data.SqlClient` or by pinning the package. I did not read the advisory text and did not test any version. No claim about reachability or exploitability is made.
- Under O-7 and O-8 a fix is a package update that needs its own approval (PD-26) and a reviewed scope addition, and an accepted risk for a shipped dependency needs explicit product-owner approval.
- **Blocker recorded:** a vulnerable shipped dependency remains. ALV-N019 cannot close on F-001 without PD-26 (update, or an explicitly approved acceptance of the risk).

## Development dependencies: the client (npm)

All five are **development-only**: they appear in `npm audit` and not in `npm audit --omit=dev`. The production dependencies (`react`, `react-dom`, `react-router-dom`) have none. The production bundle is static files built by `vite build`.

| Package | Severity | Direct | Advisory titles (count) | Fix reported by npm |
|---|---|---|---|---|
| `vite` | high | yes | 6 advisories on `launch-editor` and Vite's dev server (GHSA-698v-4r3x-3vcv, GHSA-r825-w8mm-5pxw, GHSA-587h-7wmj-h5pm, GHSA-9jrq-w75r-8gcw, GHSA-rq7h-c2jc-7f22, GHSA-vfpm-58rq-9qcg); range 8.3.0 - 8.3.2 | available |
| `vitest` | moderate | yes | via `@vitest/mocker`; range 5.0.0-beta.1 - 5.0.2 | available |
| `@vitest/mocker` | moderate | no | GHSA-qxg6-89mg-rqv8, GHSA-xp26-3454-mjgp | available |
| `source-map-js` | high | no | GHSA-68fv-2mgg-jv7q (event-loop denial of service) | available |
| `undici` | high | no | 8 advisories (GHSA-4qv8-2p9j-2685 and seven others); range 8.0.0 - 8.12.1 | available |

- **Dev-only is proven; unreachable is not.** `vite` is the dev server the developer and the accumulated chain run on `localhost:5173` while testing, so its dev-server advisories apply on a machine while that server is running. Under O-8 these cannot be waved through as "unreachable" from this report alone. The reviewer may accept them as dev-only, or require the update under PD-26.
- I did not map each advisory to the way this repository runs Vite, Vitest or `undici`.

## What I did not do
- No update, pin or override of any package; no lock-file change.
- No reading of the advisory pages themselves.
- No scan of the repository's other tooling (for example the Playwright package or root-level scripts) beyond the two project trees above.

## Decisions needed
1. **PD-26 for F-001:** update the SQL client chain so `Pkcs` resolves to a fixed version, pin the transitive package, or accept the risk with explicit product-owner approval.
2. **Client dev dependencies:** accept as dev-only (with the `vite` caveat above), or update them under PD-26 in a reviewed scope addition.
