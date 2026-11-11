#!/bin/sh
# ALV-N019 R06 Stage 2: the frozen contract of section 12.2 of the approved pre-implementation brief (same shape as R02 and R03, run on the R06 snapshot with the hardened test fixture), run ONCE on the approved R06 snapshot,
# with nothing else running on the machine and a clean working tree. Order: full backend suite, vitest, build, lint, tsc -b, the mocked Playwright suite, the repository checks, the five real-backend
# walkthroughs, one stage-0 chain run with the injected-clock host.
# usage: sh run_stage2.sh <approved-snapshot-sha> <out-folder>
# Exit code and verdict come from evaluate_stage2.py; nothing here decides pass or fail.
IMPL="$1"; OUT="$2"
R="C:/Users/firas/Documents/AI-Project-[Alveara-Dental]"
H="$R/.alveara/handoffs/ALV-N019/R06-evidence/stage2"
BASE="$R/.alveara/handoffs/ALV-N019/R02-regression-evidence/backend-tests.csv"      # the R02 Stage 2 test-identity baseline (5,697 ids); R04 adds the 18 ids of expected_new_ids.txt
[ -n "$IMPL" ] && [ -n "$OUT" ] || { echo "usage: run_stage2.sh <approved-snapshot-sha> <out-folder>"; exit 2; }
rm -rf "$OUT"; mkdir -p "$OUT/trx" "$OUT/gates"
# Before anything runs, prove every walkthrough writes into $OUT (outside the repository); a mismatch stops the run with nothing started.
python "$H/verify_runner_outputs.py" "$R" "$H/run_stage2.sh" "$OUT" > "$OUT/runner_proof.txt" 2>&1 || { cat "$OUT/runner_proof.txt"; echo "runner proof failed: nothing was run"; exit 2; }
RUN=$(git -C "$R" rev-parse HEAD)
{
  echo "run commit: $RUN"
  echo "approved snapshot: $IMPL"
  git -C "$R" status --porcelain > "$OUT/tree_before.txt"
  echo "tracked changes before: $(grep -vc '^??' "$OUT/tree_before.txt") (0 = none); untracked paths before: $(grep -c '^??' "$OUT/tree_before.txt") (the unrelated design drafts, which the run must neither add to nor remove)"
  echo "env: ALVERA_TEST_TEMPLATE=[${ALVERA_TEST_TEMPLATE}]"
  echo "start: $(date -Iseconds)"
} > "$OUT/summary.txt"
sh "$H/leak_counts.sh" before > "$OUT/leak_before.txt" 2>&1
export ALVERA_TEST_TEMPLATE_TELEMETRY="$OUT/telemetry.jsonl"; : > "$ALVERA_TEST_TEMPLATE_TELEMETRY"
cd "$R/src/Alveara.Api.Tests" || exit 1
echo "backend command: cd src/Alveara.Api.Tests && dotnet test --nologo --logger \"trx;LogFileName=full_serial.trx\" --results-directory $OUT/trx" >> "$OUT/summary.txt"
b0=$(date +%s)
dotnet test --nologo --logger "trx;LogFileName=full_serial.trx" --results-directory "$OUT/trx" > "$OUT/test_output.txt" 2>&1
echo "== backend exit=$? elapsed=$(( $(date +%s) - b0 ))s" >> "$OUT/summary.txt"
tail -6 "$OUT/test_output.txt" >> "$OUT/summary.txt"
unset ALVERA_TEST_TEMPLATE_TELEMETRY
cd "$R/src/alveara-client" || exit 1
gate() { name="$1"; shift; g0=$(date +%s); "$@" > "$OUT/gates/$name.txt" 2>&1; code=$?; echo "== gate $name exit=$code elapsed=$(( $(date +%s) - g0 ))s" >> "$OUT/summary.txt"; }
gate vitest npx vitest run
gate build npm run build
gate lint npm run lint
gate tsc npx tsc -b
gate playwright_mocked npx playwright test
cd "$R" || exit 1
gate repo_checks node --test tests/*.test.mjs
cd "$R" || exit 1
for w in "playwright.auth.config.ts -" "playwright.patient-portability.config.ts PATIENT_PORTABILITY_E2E_OUT" "playwright.patient-export-package.config.ts PATIENT_EXPORT_PACKAGE_E2E_OUT" "playwright.patient-merge.config.ts PATIENT_MERGE_E2E_OUT" "playwright.bulk-import.config.ts BULK_IMPORT_E2E_OUT"; do
  set -- $w; w0=$(date +%s)
  if [ "$2" = "-" ]; then OUTARGS=""; else OUTARGS="-OutputVariable $2 -OutputDir '$OUT/walkthroughs/$1'"; fi
  powershell -NoProfile -Command "Set-Location -LiteralPath '$R'; .\scripts\Invoke-RealBackendWalkthrough.ps1 -Config $1 $OUTARGS -SkipBuild" > "$OUT/gates/walk_$1.txt" 2>&1
  echo "== gate walk_$1 exit=$? elapsed=$(( $(date +%s) - w0 ))s" >> "$OUT/summary.txt"
done
cd "$R" || exit 1
c0=$(date +%s)
powershell -NoProfile -Command "Set-Location -LiteralPath '$R'; .\scripts\Invoke-AccumulatedChain.ps1 -SkipBuild -ClockMode injected; exit \$LASTEXITCODE" > "$OUT/gates/chain_stage0.txt" 2>&1
echo "== gate chain_stage0 exit=$? elapsed=$(( $(date +%s) - c0 ))s" >> "$OUT/summary.txt"
# The chain keeps its evidence under Test-Data\Temp\chain-output\<run id>; move that folder (this run's own, named in the runner's last line) into the out-folder so the artifact counts stay comparable.
CHAIN_DIR=$(grep -o "Evidence kept in .*" "$OUT/gates/chain_stage0.txt" | tail -1 | sed 's/^Evidence kept in //' | tr -d '\r')
if [ -n "$CHAIN_DIR" ]; then powershell -NoProfile -Command "Move-Item -LiteralPath '$CHAIN_DIR' -Destination '$OUT/chain_evidence'" > "$OUT/gates/chain_move.txt" 2>&1; fi
cd "$R" || exit 1
sh "$H/leak_counts.sh" after > "$OUT/leak_after.txt" 2>&1
echo "end: $(date -Iseconds)" >> "$OUT/summary.txt"
git -C "$R" status --porcelain > "$OUT/tree_after.txt"
echo "tracked changes after: $(grep -vc '^??' "$OUT/tree_after.txt")" >> "$OUT/summary.txt"
python "$H/evaluate_stage2.py" "$OUT" "$BASE" "$IMPL" "$RUN" > "$OUT/RESULT_ALV-N019_R06.txt"; n=$?
cat "$OUT/RESULT_ALV-N019_R06.txt"
exit $n
