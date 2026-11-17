# ALV-N022 A1 — Step 0 Addendum 01 Review

**Package:** `Alveara_Addendum_ALV-N022_A1_81fafda_01.zip`  
**Package SHA-256:** `18E2ECD98C05C5B92916A37D956B16E4FB08F0DC8FCEC7A48001927FFDA43C83`  
**Reviewed authority:** `.alveara/reviews/ALV-N022/BRIEFING-R03.md`  
**Decision:** **CHANGES_REQUIRED — documentation addendum only**

## Findings accepted

The agent correctly stopped before implementation. Step 0 establishes that routing encounter medication and medical-history writes into the chart would encounter the existing chart-wide unique live-item index. The proposed response is accepted:

- when a medication or medical-history item already exists on the chart, the encounter must present the existing item and offer an explicit **confirm current** or **update existing** action;
- confirm records the reviewed event with the current encounter provenance; update appends a version with that provenance; and
- no second current item is created, no chart item is silently changed, and no schema/index change is introduced.

The following reuse points are also accepted for A1, provided the briefing is made accurate:

- reuse the existing chart-write open-encounter boundary and its stable `encounter_not_open` refusal for signed/finalized encounter chart writes; this does not authorize any signed-draft policy change;
- canonicalize **new allergy input** by trimming and collapsing internal whitespace while preserving case; do not rewrite historical rows or silently merge records.

Before a real deployment database is used, the A1 migration/preflight evidence must perform the same read-only normalized-allergy collision check against that target database. A collision stops deployment/migration work for review; it is never automatically merged or normalized.

## Required Addendum 02 correction

### A1-01 — Active-versus-resolved allergy conflict

The briefing's approved rule describes refusal of an **active** duplicate allergy. Step 0 proves that the existing unique index rejects a same-name live record regardless of whether it is Active, Resolved, or Discontinued. The submitted addendum treats that behavior as already meeting OD-03, but it does not.

Revise the decision record, briefing, scope matrix, and focused proof plan to preserve one record and make the user-visible outcome precise:

1. **Active matching allergy:** do not add another item; direct the user to update the active item.
2. **Resolved or discontinued matching allergy:** do not add another item; offer an explicit **reactivate/update the existing record** action. The user must choose the new status; no status is silently changed.
3. **Cancellation:** leaves every existing item unchanged.
4. **Concurrency:** two simultaneous adds produce one stable record and no duplicate; the losing request receives the existing-record direction.

This is consistent with the owner's no-silent-merge direction and with the existing unique index, while correcting the inaccurate “active only” claim. It does not require an index change or a new clinical rule.

## Boundary for Addendum 02

Addendum 02 must be documentation only: update the A1 Step 0/addendum record and affected A1 planning/decision documents. Do not edit product code, schema/migrations, tests, configuration, or integration files. Do not run tests.

After this narrow clarification is reviewed and approved, A1 implementation may start within the existing A1 authorization and focused-test boundary.
