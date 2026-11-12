# ALV-N019 R06 — Focused Stability Proof and Program-Level Regression Notice

**Authority:** Product-owner decision of 2026-11-11, recorded in the Execution Plan, Engineering/Product Reference, and `.alveara/HANDOFF_SCHEMA.md`.

## Non-negotiable instruction

**Do not run the 90-minute Stage 2/full-regression contract for R06.**

The previous R04 broad run is preserved as evidence. It established that the
original pool-timeout failure did not recur across 4,354 clones, with zero
fallbacks and no leak-accounting discrepancy. R05 correctly isolated its four
non-qualifying failures, but its inventory correction is itself incomplete. R06
exists to correct and prove that narrow test-harness defect only.

Broad regression for this and later stabilization packages is deferred to the
single integrated release-candidate certification owned by `ALV-N028`. This is
not permission to skip focused safety evidence or to change product behavior.

## Required R06 implementation

Correct the source lookup in
`RealMigrationInventoryTests.An_entry_listed_as_calling_the_migrator_still_does_and_each_entry_uses_the_fixture_of_its_group()`.

The R05 form below is prohibited:

```csharp
Classes().GroupBy(c => c.Name).ToDictionary(g => g.Key, g => g.First())
```

It depends on unspecified filesystem enumeration order and can inspect only one
fragment of a partial class. Construct a deterministic composite source for a
class name by combining the text of **every** fragment in a stable order before
the guard applies `MigratorCall`, `TemplateFixtureUse`, or `RealFixtureUse`.
Do not exclude `TestDatabaseFixture`, suppress the guard, or weaken the
inventory requirement.

Add a focused proof that would fail if either the base fixture fragment or the
retry partial fragment were omitted from the composite source.

Do not alter production code, packages, project/solution files, schema,
configuration, the R04 fixture algorithm, acceptance criteria, the frozen R04
evidence, or unrelated tests.

## Required focused stability proof

Record exact commands, test identities, repeat counts, elapsed times, and all
output locations outside the repository in `TEST_RESULTS.md`.

1. Run all `RealMigrationInventoryTests` at least **20 independent repetitions**
   after the composite-source correction. Include the new partial-fragment proof.
2. Run the existing source-scanning/TestDatabase fixture sweep once. It must
   have zero failures and zero leftover test databases/files.
3. Run the normal targeted build/type/lint checks for the changed test projects.

Do **not** repeat the login-cleanup or Architecture Map repetitions in this
attempt: R06 does not modify their code or dependencies, and their R05 focused
evidence remains valid history. Re-run them only if the source-scanning change
or its proof unexpectedly affects their projects. This is deliberate
change-impact selection, not an evidence waiver.

The R04 forced-pruning proof need not be repeated because R06 must not change
fixture implementation. Preserve and reference its accepted 800-cycle control
and 900-cycle treatment evidence instead.

Any failure in this proof stops the attempt for diagnosis and a new focused
correction. Do not compensate by launching the broad suite.

## Packaging and closure

The already generated R06 ZIP is immutable evidence that no tests were run; do
not overwrite it. After this focused proof, produce a new immutable `ALV-N019`
**R07** Stage 1 handoff package. It must:

- clearly list every R04 Stage 2 failure and its current disposition;
- show the deterministic composite-source diff and the new omission-proof;
- retain R04/R05 evidence as prior-attempt history;
- state `fullRegression: "DEFERRED_TO_ALV-N028"`;
- state that this package is not release-certified and identify `ALV-N028` as
  the one final integrated broad-regression owner; and
- add/update `.alveara/PROGRAM_REGRESSION_LEDGER.md` with ALV-N019's final R06
  implementation SHA, changed subsystems, exact focused proof, and deferred
  backend/client/repository/mock-browser/walkthrough/chain obligations.

Set the story to `AWAITING_REVIEW` and stop. Do not self-close it, start
`ALV-N020`, or run any broad regression. Independent review will decide whether
the focused proof is sufficient for the authorized deferred-regression closure.
