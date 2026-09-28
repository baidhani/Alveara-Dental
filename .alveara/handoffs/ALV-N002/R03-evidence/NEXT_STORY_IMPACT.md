# ALV-N002 R03 — Next Story Impact

Carries forward R02's `NEXT_STORY_IMPACT.md` in full (repository path: `.alveara/handoffs/ALV-N002/R02-evidence/NEXT_STORY_IMPACT.md`). This file adds only what changed in R03.

## New facts the next stories can rely on

- **The API now serves the built client from the same origin, when a build exists.** `Program.cs` reads an optional `ClientBuildPath` config value (defaulting to `../alveara-client/dist` relative to the API's content root) and, if `index.html` is found there, wires `UseDefaultFiles`/`UseStaticFiles`/`MapFallbackToFile`. `ALV-N013`'s installation story can point this at wherever the production client build is deployed, or override it entirely; it is inert (no-op) whenever the directory doesn't exist, so it never interferes with the Vite dev workflow.
- **A concrete pattern now exists for proving a storage location is not accidentally web-servable**: `StorageIsolationTests.cs` stores through the real `IBlobStorage` implementation inside a `WebApplicationFactory` host with static file serving active, then asserts the stored content's bytes never appear in any HTTP response for plausible guessed paths — accounting correctly for an SPA fallback's 200 response, which serves `index.html`, not a 404. Any later story serving other file-backed content (documents, reports, backups) should add an equivalent test rather than relying on directory-naming conventions alone.
- **PHI-safe failure diagnostics has a concrete implementation, not just a convention.** `BackgroundJobRunner.SafeErrorSummary(ex, jobId) = "{ExceptionTypeName} ({correlation token via PhiSafeLog.Correlate})"` is the pattern: never persist or log `Exception.Message` from a handler whose exception could embed caller-supplied (patient) data. Other components performing similar failure logging (future controllers/services handling patient-adjacent input) should follow the same shape.
- **Idempotent-enqueue-under-race is now a proven pattern**: catch the `DbUpdateException` from a unique-constraint violation on the intended insert, clear the change tracker, and re-query the row the other caller committed — rather than checking existence before inserting. Any future "get-or-create by unique key" operation under concurrent callers should follow this shape instead of check-then-insert.

## Unresolved from R02, still unresolved

- A genuinely separate physical/virtual LAN client machine, a real production Windows Service account, CA/AD-CS-issued TLS certificate deployment/trust, and disabling public internet on the deployment machine remain **not established** by this story — `ALV-N013`/`ALV-N014`'s job, honestly documented as open rather than solved. R03 closes the specific technical gaps the reviewer identified (no static serving, `curl -k` bypass, no storage-isolation proof) but does not fabricate the physical-infrastructure parts.
- Production same-origin deployment strategy (exact `ClientBuildPath` value, build/publish pipeline wiring it up) remains `ALV-N013`'s decision — this story only proves the mechanism works when a build is present.
- DST-ambiguity-resolution UX and database-engine choice — unchanged, not reopened.
