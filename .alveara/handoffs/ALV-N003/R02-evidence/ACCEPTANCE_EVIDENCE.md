# ALV-N003 R02 — Acceptance Evidence

## R01 findings closed this attempt

| Finding | Correction | Permanent regression evidence |
|---|---|---|
| **ALV-N003-R01-01 (P1)** stale response shows/saves another provider's schedule | `AvailabilityTab` binds rows, revision, blocked time and every completion to the provider they were loaded for; numbered loads discard superseded responses; switching clears and hides the editor immediately; save/blocked-time act only on `loadedFor` and ignore late completions; dirty-switch prompt retained | `ConfigurationHubPage.test.tsx`: deterministic out-of-order test (shows B's 13:00 and the save goes to `/providers/p2/availability` with B's rows and revision), editor-hidden-while-loading, dirty-switch prompt |
| **ALV-N003-R01-02 (P1)** concurrent replacements merge into an overlapping union | `ProviderProfile.AvailabilityRevision` (concurrency token, migration `AddProviderAvailabilityRevision`); replacement requires the caller's revision and bumps it atomically with row replacement + audit; stale → shared 409; API contract `{ revision, windows }` / `{ windows, revision }`; UI sends the revision and shows the shared conflict banner with reload | `AvailabilityConcurrencyTests` (barrier races for empty and populated schedules, stale editor, missing revision, revision advance, audit rollback) — verified red without the fix; `ConfigurationApiTests` revision contract; `ConfigurationHubPage.test.tsx` conflict → failed reload → retry |
| **ALV-N003-R01-03 (P2)** main navigation bypasses unsaved-change protection | data router (`createBrowserRouter`) + `UnsavedChangesProvider` registry + `NavigationGuard` (`useBlocker`) prompting on any pathname change while an editor is dirty; sign-out prompts; only a *signed-in* session is blocked, so a lost session redirects immediately and unmounted editors clear their registry entry | `App.unsavedNavigation.test.tsx` (shell link decline/accept, clean = no prompt, browser back, sign-out, 401 not held hostage) — verified red without the guard; Playwright real-dialog test |
| **ALV-N003-R01-04 (P2)** DST fold slot reported available | slots whose elapsed time spans a UTC-offset change are refused (`crosses_dst_transition`); constant-offset slots evaluated by wall-clock arithmetic from the start; blocked time stays a UTC overlap check | `SchedulingConfigurationTests`: exact reviewer reproduction, normal day, spring-forward, ends-at-fold, blocked time across the transition |
| **ALV-N003-R01-05 (P2)** failed conflict reload closes the draft | `reloadAfterConflict` replaces the draft only after a successful read; failure keeps form/draft/conflict with a retryable error; absent record distinguished from read failure (inactivated records looked up); same rule for the availability recovery | `ConfigEntityPanel.test.tsx` (failed reload + retry, inactivated elsewhere, truly missing); `ConfigurationHubPage.test.tsx` availability equivalent |

Related defect found and fixed: `ConcurrencyConflictBanner`'s reload button defaulted to `type="submit"` inside forms and re-submitted the stale edit; now `type="button"`, asserted by `update` being called exactly once across a reload.

## Original acceptance items (unchanged and re-verified)

| # | Acceptance item | Evidence |
|---|---|---|
| 1 | Provider, operatory and appointment type/duration configurable and immediately consumed by scheduling | `SchedulingConfigurationTests`, `ConfigurationApiTests.End_to_end…`, Playwright configuration flow — all pass in the 262/113/11 runs |
| 2 | Availability and blocked time persisted in practice-local time semantics | `StaffProviderServiceTests` (wall-clock storage, CST/CDT conversion, DST gap/overlap rejection, winter/summer), plus the new DST scheduling regressions and versioned persistence above |
| 3 | Accounts, staff and provider profiles distinct but linkable | `StaffProviderServiceTests` linkage tests — unchanged and passing |
| 4 | Inactive referenced configuration remains historically resolvable | inactivation/resolution tests — unchanged and passing |
| 5 | Material changes audited | audit assertions unchanged; the availability replacement audit is now additionally proven to roll back with a rejected stale write |

R01's per-item evidence mapping (`.alveara/handoffs/ALV-N003/R01-evidence/ACCEPTANCE_EVIDENCE.md`) remains valid and is preserved unchanged.
