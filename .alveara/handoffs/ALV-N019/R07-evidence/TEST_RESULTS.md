# ALV-N019 R07: test results (focused stability proof; broad regression DEFERRED_TO_ALV-N028)

Run 2026-11-11 18:09:14 to 18:26:52, HEAD `2e35858` (implementation `3cf91fa` + records), product owner's explicit permission. Output written to `C:/tmp/r07-proof/` (outside the repository) and copied to `artifacts/focused-proof/`.

| # | Command (exact) | Repeats | Result | Elapsed | Output |
|---|---|---|---|---|---|
| 1 | `cd src && dotnet build Alveara.Api.Tests/Alveara.Api.Tests.csproj --nologo -v q` | 1 | exit 0, 0 errors, 32 warnings (unchanged) | 49 s | `build.txt` |
| 2 | `cd src/Alveara.Api.Tests && dotnet test --no-build --nologo --filter "FullyQualifiedName~Alveara.Api.Tests.RealMigrationInventoryTests" --logger "trx;LogFileName=inventory-<i>.trx"` | 20 independent | **20 of 20 runs passed; 5 of 5 tests in each** | 2 to 8 s per run | `inventory/run-1..20.txt`, `inventory/inventory-1.trx` |
| 3 | `dotnet test --no-build --nologo --filter "<sweep/filter.txt>" --logger "trx;LogFileName=sweep.trx"` (26 source-scanning classes + `TestDatabase*` + 3 fixture classes) | 1 | **375 of 375 passed, 0 failed, 0 skipped** | 15 m 29 s | `sweep/output.txt`, `sweep/sweep.trx`, `sweep/source-scanning-classes.txt` |
| 4 | `leak_counts.sh` before and after | 1 | identical: 0 `AlveraTest_*`/template/restore databases, 0 logins, 0 database files | | `leak_before.txt`, `leak_after.txt` |

The 5 `RealMigrationInventoryTests` ids (each passed in all 20 runs):
- `A_class_gets_the_fixture_of_its_group_a_real_migration_fixture_or_a_template_clone`
- `An_entry_listed_as_calling_the_migrator_still_does_and_each_entry_uses_the_fixture_of_its_group` (holds the composite-source omission proof)
- `Every_class_using_the_real_path_is_named_in_the_inventory_and_every_entry_in_that_group_uses_it`
- `No_class_that_calls_the_migrator_is_missing_from_the_inventory`
- `The_inventory_names_all_three_groups_with_the_real_path_kept_for_the_migration_pipeline_and_the_fixture`

`tsc`/lint: no client file changed in R06 or R07, so they do not apply; the .NET build is the type check of the changed project. Not repeated by design: login-cleanup (R05 12 of 12), Architecture Map (R05 8 of 8), forced-pruning preflight (R04). Not run: any broad regression (deferred to ALV-N028).
