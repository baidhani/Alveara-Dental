# Procedure and fee catalog (ALV-N005)

The practice's list of procedures it can plan, perform and charge for: a code in an explicit code system, a description, a category, where it applies, and a fee that is kept as versions over time. Treatment planning (STORY-016), completion and billing read from it; they never keep a catalog of their own.

## What it is, in one paragraph

A **procedure** has a fixed identity (code system + code). Everything else lives on an immutable **version**. Changing a fee, a description or a source edition appends a new version; nothing is rewritten. Every change also appends an **event** (who, when, why). A procedure is never deleted; it is **inactivated** with a reason and can be reactivated. Other modules ask the catalog for a **snapshot** (the version in effect on a date, or one exact version id) and copy it, so a later fee change never alters a past plan or charge.

## Data

| Table | Purpose | Rules the database itself enforces |
|---|---|---|
| `ProcedureDefinitions` | identity + current state | unique `(CodeSystem, Code)`; code shape per system; a `Local` code never looks like CDT (`D####`); no delete (trigger 51077); code and code system never change (51078) |
| `ProcedureVersions` | immutable fee/description history | fee 0 to 1,000,000.00 with at most two decimals; category, scope, dentition from fixed lists; dentition must be `Both` unless the scope is tooth-level; description present, no control characters; `ValidThrough` not before `EffectiveFrom`; no update/delete (51079); numbers contiguous (51081); a version never starts before the one it follows (51082) |
| `ProcedureEvents` | append-only history | change type from a fixed list; revise and inactivate carry a reason; no update/delete (51080) |

Code systems: `Local` (the practice's own codes), `CDT` (`D` + four digits; **no licensed CDT text is bundled** - the practice enters its own wording and names the licensed source and edition it holds), `External` (any other code set; the source must be named).

## Behaviour

- **Create**: all fields validated together; every wrong field is named in one answer (`validation_failed`, 400, `fieldErrors`). An identical repeat returns the existing procedure (idempotent); a *different* procedure with a used code is `procedure_exists` (409). Concurrent creation of the same code ends with one procedure (unique key + handled race).
- **Revise** (fee, description, category, scope, source edition, dates): needs a reason and the row version that was read. The code and code system are fixed (`identity_fixed`, 400). New versions start today or later and never before the version they follow, so history is not backdated. Saving with nothing changed adds no version. A fee scheduled for a future date waits for that date.
- **Inactivate**: needs a reason. If other records refer to the procedure the service refuses with `usage_confirmation_required` (409) and the counts until the caller confirms with `acknowledgeUsage`. Inactivating changes none of the records that refer to it; it only stops the procedure being offered for new work. **Reactivate** restores it (reason optional).
- **Planning query** `GET /api/procedures/active?toothKey&surface&scope&category&search&asOf`: procedures usable on a date, narrowed to those that fit the tooth's dentition and surface.
- **Snapshots**: `GET /api/procedures/{id}/snapshot?asOf=` and `GET /api/procedures/versions/{versionId}`.
- **Usage** is read through `IProcedureUsageSource`. The first source counts odontogram links of type `Procedure` whose reference is the procedure id. Treatment planning and billing register their own sources when they exist; the catalog does not change.

## Permissions

No new permission was invented. Reading (list, detail, history, usage, snapshots, planning query) needs `ViewBilling` (Dentist, FrontDesk, Billing, OfficeManager, Admin). Changing needs `ManageBilling` (Billing, OfficeManager, Admin) plus a CSRF token. Hygienists and assistants cannot see the catalog. Every route re-checks on the server; the screen only hides controls.

## Audit

Every create, revise, inactivate and reactivate writes an audit entry (event type, entity `ProcedureDefinition`, actor, the changed field names and old/new fee amounts) and a `ProcedureEvent`. Audit text contains no patient data.

## Failure-first notes

| Question | Answer |
|---|---|
| What if a save fails? | One transaction per change; nothing partial is stored. A unique-key race (SQL 2601/2627) is reported as a concurrency conflict, not a 500. |
| Retries? | The screen keeps what was typed and the user retries; the server operations are idempotent (identical create returns the same procedure; unchanged revise adds nothing). No automatic retry loops. |
| Recovery when it cannot proceed? | Nothing is queued: the call is synchronous and the refusal says why. A wrong entry is corrected by revising (history kept) or, for a wrong code, inactivating it and adding the right one. |
| Handled | validation, duplicates, races, stale versions, permission, CSRF, inactivation of referenced procedures, a failed usage check on the screen (it will not offer inactivation). |
| Not handled | A usage source that is down: `UsageAsync` fails loudly rather than reporting zero. Cross-module pricing rules (discounts, insurance fee schedules) are out of scope. |

## Assumptions made (ALV-N005)

1. Reuse `ViewBilling` / `ManageBilling` rather than add permissions (a new permission is a governance change).
2. The version model (identity + immutable versions + events) is the fee-history mechanism; no separate fee table.
3. `Dentition` is only meaningful for tooth-level scopes and is stored as `Both` otherwise.
4. New versions cannot start in the past (no backdating); creation may use any date from 2000-01-01 to three years ahead.
5. Fees are US dollars (the repository's money convention) with two decimals.
6. The usage warning counts odontogram procedure links only, because that is the only referencing record that exists today.

## Verification

Backend: `ProcedureCatalogRulesTests`, `ProcedureCatalogLifecycleTests`, `ProcedureCatalogSchemaTests`, `ProcedureCatalogApiTests` (real SQL Server). Frontend: `ProcedureCatalogPage.test.tsx` (vitest + axe). Browser: `e2e/procedures-real-backend.spec.ts` via `playwright.procedures.config.ts` (see `docs/testing/REAL_BACKEND_E2E.md`).
