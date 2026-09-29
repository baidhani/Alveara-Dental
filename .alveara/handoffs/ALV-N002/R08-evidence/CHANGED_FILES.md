# ALV-N002 R08 — Changed Files

Implementation commit `02a8c98533cc604fce4ac9f27c46f119f5b9065f` (`ALV-N002 R08: server harness performs and verifies checks itself, no manual step`):

| File | Change |
|---|---|
| `ops/deployment-verification/Invoke-ServerVerification.ps1` | Rewritten: the service-account write/read and ordinary-account denial checks are now both executed by the script itself (`Start-Process -Credential -Wait`), with each child process recording its own actual `WindowsIdentity` rather than the parent interpolating an expected name; the summary is computed only after every check (including both `-Wait` calls) has completed, so its timestamp can never precede a result it reports; added a forced `/api/systemstatus` request with retained response body; added a SQL session-to-API-PID correlation check via `sys.dm_exec_sessions`; fixed a latent invalid-parameter bug in the role-membership comparison; corrected the `.EXAMPLE`/docs to use the API's own HTTP endpoint instead of an HTTPS URL that would fail on a certificate name mismatch unrelated to what that check verifies |

This evidence commit additionally adds the raw, unedited, machine-generated output from running the fixed script against the real server VM:

| Path | Content |
|---|---|
| `.alveara/handoffs/ALV-N002/R08-evidence/artifacts/server/*` | Server-side verification run output (7 result files + 1 summary), all genuinely produced by the script in a single unattended pass |
| `.alveara/handoffs/ALV-N002/R08-evidence/artifacts/client/*` | Byte-identical copies of R07's already-accepted client-side evidence, included for a self-contained package |

No file under `.colaberry/`, `docs/`, `CLAUDE.md`, `index.html`, `assets/`, or `GPT Docs/` was touched (confirmed zero diff — see `PARENT_REGRESSION.md`).
