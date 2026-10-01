# ALV-N003 R03 — Acceptance Evidence

## R02 finding closed this attempt

| Finding | Correction | Permanent regression evidence |
|---|---|---|
| **ALV-N003-R02-01 (P2)** delayed write completion from an abandoned editing session overwrites a new draft when the same provider is reselected | `AvailabilityTab` completions (weekly save success/error/notification, blocked-time add/remove success/error/refresh) bound to an *editing-session generation* that advances on provider change and on every successful (re)load; request identity independent of provider identity; draft counters so an in-flight save never replaces newer typing and a blocked-time success never clears a newer form; Save disabled while saving | `ConfigurationHubPage.test.tsx` "Availability editing-session completion protection": A-save→B→A→new draft→old response (draft 10:00 kept, dirty, saveable at revision 2); delayed 409 ignored; edits-during-save kept with baseline/revision advanced; blocked-time add success/error and remove stale completions ignored — three verified red without the fix |
| (proactive, same defect class) `ConfigEntityPanel` and practice form completions | `ConfigEntityPanel` editing-session generation for save and conflict-reload completions + pending-write policy (fields, other rows' Edit and Add disabled while saving); practice form inputs disabled while saving | `ConfigEntityPanel.test.tsx` (+2), practice-form test (+1) |

## Correction of R02 claims

R02's statements that every completion was protected and that R01-01 was fully closed were too broad (see `R03.md`, "Correction of the R02 package's claims"). Accurate position: R02 repaired the A-load/B-load ordering sequence and protected *loads* by generation; write completions were protected only against a different provider id. R03 closes the same-provider return sequence and the in-flight-edit case. `R02-evidence/NEXT_STORY_IMPACT.md`'s "provider-bound async UI pattern" is superseded by the session-identity pattern in `R03-evidence/NEXT_STORY_IMPACT.md`.

## R01/R02 findings — status after R03

| Finding | Status |
|---|---|
| R01-01 stale cross-provider response | repaired R02 (loads) + R03 (write completions, same-provider return) |
| R01-02 concurrent schedule replacement | repaired R02 (versioned aggregate, real SQL Server races) — unchanged, re-run green |
| R01-03 navigation guard | repaired R02 — unchanged, re-run green |
| R01-04 DST fold slot | repaired R02 — unchanged, re-run green |
| R01-05 failed conflict reload | repaired R02 — unchanged, re-run green; reload completions now also session-bound |

## Original acceptance items

All five (provider/operatory/appointment type consumed by scheduling; practice-local availability and blocked time; distinct-but-linkable accounts/staff/providers; historical resolvability of inactive configuration; audited material changes) are unchanged and re-verified by the 262/122/11 runs. R01's per-item evidence map (`R01-evidence/ACCEPTANCE_EVIDENCE.md`) and R02's remain valid and preserved.
