# ALV-N006 — Independent Stage 1 Review, R01

**Decision: APPROVED_PENDING_REGRESSION**

This is a Stage 1 approval only. It approves the implementation and focused evidence for one frozen Stage 2 run. It is not final completion and does not authorize source, test, or Stage 2 contract changes before that run.

## Package identity

- Package: `.alveara/dist/Alveara_Handoff_ALV-N006_4a7d6f4_R01.zip`
- SHA-256: `905054D219C31B611C8A57AE593BA5E41076FA0B039C7670B66A8588211F6A3F`
- Implementation commit: `4a7d6f48d98333152272bd470bcbb40ab9e3aa5e`
- Evidence commit: `515c34111a92394a9ddc599f539852bafb40fcc6`
- Status/packaging commit: `6318e76396aaa4bb8237ea6cc7db71e97f81bdd5`

## Independent findings

The repository, package, focused artifacts, and critical paths substantiate the seven acceptance conditions.

- The delivered report categories cover operations, finance, clinical activity, and outcome measurement. Financial reports reconcile classified ledger lines against an independent ledger total; operational reports reconcile their counts to source appointment states.
- The report controllers separate operational, financial, clinical, and measurement access with distinct permissions. Drill-downs and exports inherit the same group permission, requests are audited, and an audit-save failure refuses delivery.
- Baselines are completed-period, idempotent snapshots with metric-definition versions. Database triggers make baselines and stored metric rows append-only. Comparison output has no percentage or movement unless the period ordering, definition version, and both sample-size requirements are valid.
- Date ranges, source-row limits, export limits, unsupported filters, empty states, and CSV formula-like text are handled explicitly rather than silently truncated or misrepresented.
- Focused evidence independently parses as 1,682/1,682 passing backend tests and 964/964 passing API tests. The package records 131 exactly allowlisted new backend test identities, ten caught negative controls, client/repository gates, performance smoke, and the new real-backend reporting walkthrough (7/7).

### Inherited limitation

`playwright.billing-ledger.config.ts` fails at test 8 because a pre-existing strict selector resolves three status paragraphs. The package reproduces the identical failure on clean commit `6899818`, which contains no reporting implementation. It is therefore not an ALV-N006 defect and must not be repaired inside this approved run. Preserve it as known test debt for the Gate E evidence review; do not represent the billing-ledger walkthrough as passing until a separately authorized test-only correction is reviewed.

## Required Stage 2 procedure

1. Make only the normal review-closure/control-record commit. Do **not** alter `src/`, tests, the frozen Stage 2 files, or the approved implementation.
2. On that clean closure commit, execute the frozen run once, with no other workload using the test environment:

   ```sh
   sh .alveara/handoffs/ALV-N006/R01-evidence/stage2/run_stage2.sh 4a7d6f48d98333152272bd470bcbb40ab9e3aa5e C:/tmp/stage2-ALV-N006-R01
   ```

3. Submit the resulting regression package for final review. It must satisfy its frozen evaluator: exactly 5,612 backend tests passed with none failed or skipped; unchanged 5,481-test baseline identities/outcomes plus exactly 131 allowlisted new identities; clean tree; template/cleanup checks; all client and repository gates; and the twelve specified real-backend walkthroughs.

Any source/test/evidence-contract change after this approval invalidates it and requires a new Stage 1 handoff. Do not begin Gate E until final review marks ALV-N006 complete.
