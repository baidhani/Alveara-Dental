# ALV-N019 dependency advisory report (R05 F-001, F-005), revision for attempt R02

Session CC-20261109-8w3d. Queries run 2026-11-10. Read-only: no package, lock file or project file was changed and `npm audit fix` was not run (reviewer decision O-7: no update without the separate PD-26 decision). This revision answers review correction 05 of `.alveara/reviews/ALV-N019/R01.md`: it reads the advisory text, states a disposition and a reachability analysis for each advisory, proves the shipped-artifact boundary, and keeps `System.Security.Cryptography.Pkcs` an OPEN blocker.

Raw evidence (in `.alveara/handoffs/ALV-N019/R02-evidence/artifacts/advisory/`): `dotnet-list-vulnerable.txt`, `dotnet-nuget-why-pkcs.txt`, `npm-audit-all.json`, `npm-audit-omit-dev.json`, `npm-advisory-details.json` (the advisory records fetched from the public GitHub advisory API), and the boundary checks listed under each section.

## 1. Shipped dependency: `System.Security.Cryptography.Pkcs` 9.0.18 (NuGet)

| Item | Finding |
|---|---|
| Path | Transitive, not a `PackageReference`: `Microsoft.EntityFrameworkCore.SqlServer` 10.0.12 brings `Microsoft.Data.SqlClient` 7.1.0, which depends on `System.Security.Cryptography.Pkcs` 9.0.18 (`dotnet nuget why`). |
| GHSA-846f-f764-76mx (CVE-2026-84008), High, CVSS 7.5 | `SignedCms.CheckSignature` can repeatedly reparse attacker-controlled X.509 data: unauthenticated quadratic CPU exhaustion (CWE-407). Affected for the 9.0 line: >= 9.0.0, <= 9.0.20; fixed in 9.0.21. For the 10.0 line: <= 10.0.12; fixed in 10.0.13. |
| GHSA-2fmv-rv2c-59x7 (CVE-2026-70162), Moderate, CVSS 5.9 | On Windows `EnvelopedCms.Decode` can do quadratic work on attacker-supplied CMS/PKCS#7 attributes: pre-authentication CPU exhaustion (CWE-405). Same affected and fixed versions. |
| Resolved version | 9.0.18, inside the affected range of both advisories (fixed in 9.0.21). |

### Is the vulnerable binary in the application? (evidence, not inference)
1. **Not in the build output.** `src/Alveara.Api/bin/Debug/net10.0/` contains no `System.Security.Cryptography.Pkcs.dll` and no copy under `runtimes/`.
2. **Not in the runtime manifest.** `Alveara.Api.deps.json` has no entry for the package, although `obj/project.assets.json` lists the package with a runtime asset for net10.0. The SDK drops the package copy because the ASP.NET Core shared framework already provides a higher version of the same assembly.
3. **The shared framework supplies its own copy.** `C:\Program Files\dotnet\shared\Microsoft.AspNetCore.App\10.0.14\System.Security.Cryptography.Pkcs.dll` is file version 10.0.1426.52326 (product 10.0.14); the 10.0.12 framework folder holds 10.0.1226.42308. The app's `runtimeconfig.json` asks for framework 10.0.0, which rolls forward to the newest installed 10.0.x patch.
4. **Consequence on this machine:** the Pkcs code that would run is the framework's 10.0.14 copy, which is at or above the 10.0.13 fix. A machine whose newest ASP.NET Core 10.0 runtime is **10.0.12 or older** would run a vulnerable copy (10.0.12 is also installed here and is in the affected range).

### Reachability
- **Application code:** no use of `SignedCms`, `EnvelopedCms`, `CmsSigner`, `CmsRecipient` or the Pkcs namespace anywhere under `src` (repository search, no matches).
- **The consuming library:** a binary string search of the shipped `Microsoft.Data.SqlClient.dll` finds no reference to `SignedCms`, `EnvelopedCms`, `CmsSigner` or the Pkcs assembly name (0 matches for each). This is a weak check (a metadata search, not a call-graph analysis) and is stated as such.
- **No attacker-controlled input path:** the API does not accept CMS or PKCS#7 data from clients; neither affected method is called by anything I could find. The application connects to SQL Server with Windows authentication and no Always Encrypted setting (`Column Encryption`, `ColumnEncryptionSetting`, attestation settings: no matches in `src`, `scripts`, `docs/operations`).
- **Disposition (evidence-backed):** *not reachable from application code and the vulnerable package binary is not deployed;* exposure exists only if the host runs an ASP.NET Core 10.0 runtime older than 10.0.13. This does not remove the scanner finding: the restore graph still resolves 9.0.18, so `NU1902` and `NU1903` still print on every build.

### Status and decision needed (PD-26)
**OPEN BLOCKER, retained.** The vulnerable resolved version remains in the restore graph and the dependency scanner will keep reporting it. Final closure still needs PD-26: either remediation (a reviewed change so the graph resolves 9.0.21 or later, by an update of the SQL client chain or a reviewed pin of the transitive package) or explicit product-owner risk acceptance, which can now cite the analysis above. Whatever is chosen, release engineering (ALV-N013) should state the minimum ASP.NET Core runtime patch level (10.0.13 or later) for deployments. Nothing was changed in this attempt.

## 2. Development dependencies: the client (npm)

**Shipped-artifact boundary (proved).**
- `npm audit --omit=dev --json`: **0 vulnerabilities** (`npm-audit-omit-dev.json`). The production dependencies are `react`, `react-dom` and `react-router-dom` (`npm ls --omit=dev --all`: `cookie`, `scheduler`, `set-cookie-parser` below them).
- The built bundle in `dist/` contains none of the five vulnerable packages: a text search for `undici`, `source-map-js`, `launch-editor`, `vitest` and `@vitest` finds 0 files each. `dist/` is static files built by `vite build`; no Node package runs in production.
- `vite.config.ts` sets no `server.host`, so the development server uses Vite's default of `localhost` only.

**Dependency paths** (`npm ls`): `vite` 8.3.1 and `vitest` 5.0.2 are direct dev dependencies. `@vitest/mocker` 5.0.2 comes from `vitest`. `undici` 8.11.2 comes from `jsdom` 30.1.1 (the Vitest DOM test environment). `source-map-js` 1.2.1 comes from `postcss` (under `vite`) and from `css-tree` (under `jsdom`). `launch-editor` is not a separate package: the advisories are filed against `vite`, which bundles it.

**Test-network note:** the client's test files reference only `http://x` and `https://example.test` as URLs (10 and 1 occurrences); the product code uses neither `EventSource` nor `WebSocket`.

| Package (resolved) | Advisory | Severity | Affected, fixed in | Reachability on a developer or CI machine | Disposition |
|---|---|---|---|---|---|
| vite 8.3.1 | GHSA-698v-4r3x-3vcv launch-editor: `escapeCmdArgs()` omits `%` escaping on Windows | medium | 8.3.0 to 8.3.2, fixed 8.3.3 | Per the advisory, only Windows hosts where an attacker can place a file with a malicious name (containing `&` or `%`) and also inject an environment variable or an NTFS alternate data stream. The path goes through Vite's open-in-editor endpoint on the localhost dev server, so a local process or a page the developer opens could request it; the preconditions on the machine are not met by the repository itself. | Dev-only, low practical exposure; update under PD-26 to 8.3.3 or later. |
| vite 8.3.1 | GHSA-r825-w8mm-5pxw launch-editor: editor CLI flag misinterpretation, arbitrary command execution for some editors | medium | same | Per the advisory, the attacker must be able to create a file or directory whose name starts with `+` or `-` in the working directory and the developer must use an affected editor (Vim, Emacs, Notepad-style editors listed in the advisory); reachable only through the same open-in-editor endpoint. | Same. |
| vite 8.3.1 | GHSA-587h-7wmj-h5pm launch-editor: `fs.existsSync()` check bypass on Windows | high | same | Windows only; it removes the file-placement requirement of GHSA-698v and combines with it for command injection when the attacker controls the file argument of an open-in-editor request. | Same. |
| vite 8.3.1 | GHSA-9jrq-w75r-8gcw cross-origin script execution through custom middleware | low | same | The advisory's own precondition is a dev server that passes an unchanged request URL to `transformIndexHtml` (`middlewareMode` or `appType: 'custom'`). `vite.config.ts` sets neither (it configures only a proxy for `/api`), so the repository uses the default `spa` mode and does not meet the condition. | Dev-only; precondition not met by this configuration; update under PD-26. |
| vite 8.3.1 | GHSA-rq7h-c2jc-7f22 files outside `server.fs.allow` served when project paths match system paths (non-Windows) | medium | same | The advisory describes a project importing a file path that is the project root concatenated with an absolute host path (for example `<root>/etc/hosts`); it is described for non-Windows hosts. I did not search every import for such a path; the development machine is Windows, and a CI or other developer host could differ. | Dev-only; update under PD-26. |
| vite 8.3.1 | GHSA-vfpm-58rq-9qcg first 4 bytes of files outside `server.fs` exposed by `?vite-wasm-instance` | medium | 8.3.0 to 8.3.2 only | The advisory applies only to apps that expose the dev server to the network with `--host` or `server.host`; this repository does neither (`dev` is `vite`, and the scripts pass only `--port`), so the condition is not met. | Dev-only; precondition not met; update under PD-26. |
| vitest 5.0.2 / @vitest/mocker 5.0.2 | GHSA-qxg6-89mg-rqv8 code injection via unauthenticated manual mock registration | medium (4.7) | 5.0.0-beta.1 to 5.0.2, fixed 5.0.3 | The vulnerable handler is `@vitest/mocker`'s interceptor plugin on the Vite dev-server WebSocket, reachable by any client that can reach that dev server. The repository's dev server (`vite.config.ts`) registers only the React plugin, and Vitest runs as `vitest run` in jsdom (`vitest.config.ts`: `environment: "jsdom"`), not in a long-running browser-mode server. | Dev-only, not reachable in the way tests are run here; update under PD-26. |
| @vitest/mocker 5.0.2 | GHSA-xp26-3454-mjgp Windows backslash traversal bypasses the redirect allowlist | medium (5.9) | 5.0.0-rc.2 to 5.0.2, fixed 5.0.3 | Same handler (the redirect branch), same condition: Windows only, and only where that interceptor plugin is serving. | Same. |
| source-map-js 1.2.1 | GHSA-68fv-2mgg-jv7q event-loop DoS via indexed source-map section offsets | high (7.5) | 1.0.0 to 1.2.1, fixed 1.2.2 | Needs an attacker-supplied source map to be parsed; the build and tests process only this repository's own files. | Dev-only; not reachable with untrusted input; update under PD-26. |
| undici 8.11.2 (via jsdom) | GHSA-4qv8-2p9j-2685 unbounded memory in the EventSource parser; GHSA-f72v-6w45-q472 SRI bypass; GHSA-xjj6-f33q-929c excessive content-encodings; GHSA-4w7w-5xrc-9h26 cache poisoning via cross-origin redirects; GHSA-2m5v-pch6-74hx TLS validation bypass via dropped tls options; GHSA-qrxm-fqhp-jwvm TLS 1.2 renegotiation DoS; GHSA-9fm3-97px-rjf6 WebSocket PONG buffering DoS; GHSA-55wc-3pg8-jfw6 decompress interceptor exception | 3 high, 5 medium (CVSS 5.9 to 7.5) | 8.0.0 to 8.12.1, fixed 8.12.2 | All need `undici` to talk to an attacker-controlled network peer. Inside `jsdom` under Vitest the tests use mocked responses and the URLs `http://x` and `https://example.test`; no test was found that connects to a real remote host. I did not audit every test's network use beyond that search. | Dev-only (Vitest/jsdom); not reachable from shipped code; update under PD-26. |

The full advisory records are in `npm-advisory-details.json`.

### Disposition summary for the dev-only findings
All 17 npm advisories (5 packages) are development-only: absent from the production dependency tree and from the built bundle. The Vite caveat the reviewer named is stated, not waved away: the dev server runs on a developer machine and during the chain and end-to-end walkthroughs (port 5173), bound to localhost, so its advisories are reachable there by local processes and by web pages the developer opens in the same session. Fixes are available in Vite 8.3.3, Vitest 5.0.3, `source-map-js` 1.2.2 and `undici` 8.12.2; applying them is a package update and needs PD-26 and a reviewed scope addition (O-7). Until then they are accepted only as dev-only findings, not as shipped risk.

## 3. What was not done
- No update, pin or override of any package; no lock-file change.
- No call-graph analysis of `Microsoft.Data.SqlClient`; the binary string search above is a weak, stated check.
- The runtime used in production is not known from the repository; the minimum-runtime statement is a requirement for release engineering, not a verified deployment fact.
- No audit of every Vitest test's network use beyond the URL search above.

## 4. Decisions needed
1. **PD-26 for F-001:** remediate (update or pin so the graph resolves Pkcs 9.0.21 or later) or have the product owner accept the risk with the facts in section 1.
2. **Client dev dependencies:** accept as dev-only (with the Vite caveat) or update them under PD-26.
3. **Release engineering (ALV-N013):** record the minimum ASP.NET Core runtime patch level (10.0.13 or later).
