# ALV-N002 R03 — Demo Evidence

R02's targeted real-process/real-network demos were accepted by the reviewer for what they covered, but the reviewer found the LAN/HTTPS check reached only the API from the server's own NIC — not a production shell, and `curl -k` bypassed certificate validation rather than proving it. This attempt adds a full same-origin shell demonstration with genuine (non-bypassed) TLS validation, plus a storage-isolation demonstration, targeting exactly those gaps (N002-R02-01), and reproduces-then-disproves the reviewer's two harness failures (N002-R02-02, N002-R02-03) as codified tests rather than one-off manual runs — see `TEST_RESULTS.md` for those.

## N002-R02-01: single-origin production shell, genuine LAN TLS validation, not `curl -k`

1. Rebuilt the API and the client (`npm run build`) so a real `dist/` exists for `Program.cs`'s static-file-serving path to activate.
2. `dotnet dev-certs https --trust` — the development certificate is genuinely trusted by this machine, not merely present.
3. Started the API bound to `0.0.0.0` on both `http://:5072` and `https://:7180`.
4. `curl https://192.168.1.180:7180/` with no `--resolve` (a direct raw-IP request) **correctly fails** TLS validation (`SEC_E_WRONG_PRINCIPAL`) — proving validation is actually happening, not silently skipped, since the dev cert's SAN doesn't cover the bare IP.
5. `curl --resolve localhost:7180:192.168.1.180 https://localhost:7180/<path>` — simulates a properly-configured LAN client whose hostname (matching the cert's `CN=localhost`) resolves via DNS/hosts to the server's real LAN IP. This is genuine, non-bypassed certificate validation reached over the real network interface, directly answering the reviewer's specific objection to `curl -k`. Confirmed `200` for `/` (shell HTML, `<title>Alveara Dental</title>` present in body), `/api/health`, `/system-status` (SPA fallback), a built static asset, and `/api/systemstatus` (same-origin API call reporting real database/runner health).
6. Stopped the process after verification.

## N002-R02-01: blob storage is not web-reachable even with static file serving active

`StorageIsolationTests.cs`'s `A_stored_blob_is_not_reachable_over_HTTP_even_with_static_file_serving_active` stores a real blob through a full `WebApplicationFactory` host with static file serving active, then requests several plausible guessed URLs for it and confirms the blob's actual byte content never appears in any response — including correctly accounting for the SPA fallback's 200 response (which serves `index.html`'s bytes, not the blob's).

## N002-R02-02: PHI-safe logging, reproduced then disproved

`A_handler_exception_containing_synthetic_patient_like_text_never_reaches_persisted_LastError` throws `InvalidOperationException("Synthetic patient Alice Example, treatment detail")` through the real runner and asserts `Alice Example` is absent from the persisted `LastError` column — the exact reviewer reproduction, now with the opposite (fixed) result. `The_configured_logging_sink_never_receives_the_raw_exception_message_only_the_safe_summary` does the same for the injected `ILogger` via a `CapturingLogger<T>` fake that records every formatted message string.

## N002-R02-03: concurrent enqueue, reproduced then disproved

`Two_concurrent_enqueue_calls_for_the_same_idempotency_key_both_succeed_and_return_the_same_job` fires two real, synchronized `EnqueueAsync` calls for the same idempotency key against real LocalDB and asserts both return successfully with the same job id and exactly one row persisted — the exact reviewer reproduction (which previously threw `DbUpdateException` on one caller), now converging cleanly.

## N002-R02-04: migration-failure rollback, reproduced then disproved

`A_partially_executed_failing_migration_is_fully_rolled_back_not_left_half_applied_and_does_not_destroy_existing_data` now performs a real `ALTER TABLE ... ADD COLUMN` before failing, then queries `INFORMATION_SCHEMA.COLUMNS` and asserts the column is absent — proving an actually-partial migration is rolled back, addressing the reviewer's specific complaint that the prior single-statement failing migration could not distinguish "never ran" from "rolled back."

## Why this counts as "demonstrated"

Every reproduction above uses the real compiled application against a real database engine (LocalDB) or a real trusted TLS certificate over the real network interface — not a mock, and (for the LAN/TLS case) not a bypass of the exact control the reviewer specifically flagged. What remains undemonstrated — a genuinely separate physical LAN client, a real production service account, disabled public internet — is stated plainly as an open limitation in `ACCEPTANCE_EVIDENCE.md` and `.alveara/BUILD_STATE.md`, not glossed over.
