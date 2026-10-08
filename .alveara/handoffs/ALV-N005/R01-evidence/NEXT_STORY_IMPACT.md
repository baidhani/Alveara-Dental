# ALV-N005 R01 - Next-story impact

The next item in the Execution Plan is item 28, `STORY-015`; the stories that will consume the catalog are course `STORY-014` (procedure catalog verification) and the treatment-planning, completion and billing work after it.

## Interfaces a consumer should use
- **Offer procedures when planning:** `GET /api/procedures/active?toothKey=&surface=&scope=&category=&search=&asOf=` (service: `ProcedureCatalogService.ActiveForPlanningAsync`). Returns snapshots (`ProcedureSnapshot`), already narrowed to the tooth's dentition and the surface.
- **Remember what a plan or charge was made with:** store the `versionId` (and procedure id) from the snapshot, and read it back with `GET /api/procedures/versions/{versionId}` (`VersionSnapshotAsync`). A version never changes (database trigger), so this is the way a plan or charge keeps the fee it was made with. Do **not** store the fee alone, and do not read "the current fee" for something already made.
- **Fee on a given date:** `GET /api/procedures/{id}/snapshot?asOf=YYYY-MM-DD`.
- **Warn before inactivation:** implement `IProcedureUsageSource { string Name; Task<int> CountAsync(Guid procedureId, CancellationToken) }` for each table that refers to a procedure and register it in `Program.cs` next to `FindingLinkProcedureUsageSource`. The catalog, the screen and the 409 `usage_confirmation_required` response then count it with no change.
- **Procedure references elsewhere** should hold the procedure id (GUID). The existing odontogram `Procedure` link holds the id as text and is the one source counted today.

## Assumptions the next story inherits
- Identity is code system + code and is fixed; to correct a wrong code, inactivate it and add the right one (history is kept).
- Fees are US dollars with two decimals; zero means no charge.
- A new version cannot start in the past, so "the fee on a past date" is always answered from versions that were in force then.
- `ViewBilling` reads and `ManageBilling` changes; `Dentist` and `FrontDesk` can read but not change.
- No licensed CDT text or code list is bundled; the practice types its own wording and names the licensed source it holds.

## Schema
Migration `AddProcedureCatalog`; triggers 51077-51082 are used, **51083 and up are free**. Tables `ProcedureDefinitions`, `ProcedureVersions`, `ProcedureEvents`.

## UI extension points
`/procedures` is a standalone page. Treatment planning should offer procedures through the planning query (not by embedding this page). `procedureText.ts` holds the user-facing words for code systems, categories, scopes and dentitions and `formatFee` / `parseFee`; reuse them rather than re-spelling.

## Unresolved decisions that may affect later work
- Whether planning needs a fee per tooth surface rather than one fee per procedure (not built; the version model can carry it as new fields).
- Whether a fee change by one person needs a second approver (not built).
- A particular surface (for example "occlusal only") is not stored; if a procedure must be restricted to named surfaces, add it as a new version field.
- Where a measurement event for catalog adoption belongs (planning, not the catalog).

## Prompt changes relevant to the next item
None needed for `STORY-015`. When the treatment-planning story is written, its prompt should name the snapshot/version-id contract above, because "fee changes do not rewrite existing plan history" (acceptance item 3 here) can only be proven end to end once a plan stores a version id.
