#!/bin/sh
# ALV-N016 Stage 2: the frozen contract, run ONCE on the Stage 1 closure commit, with nothing else running on the machine and a clean working tree.
# usage: sh run_stage2.sh <implementation-sha> <out-folder>
# Exit code and verdict come from evaluate_stage2.py; nothing here decides pass or fail.
IMPL="$1"; OUT="$2"
R="C:/Users/firas/Documents/AI-Project-[Alveara-Dental]"
H="$R/.alveara/handoffs/ALV-N016/R03-evidence/stage2"
BASE="$R/.alveara/handoffs/ALV-010-C01/R03-regression-evidence/backend-tests.csv"
[ -n "$IMPL" ] && [ -n "$OUT" ] || { echo "usage: run_stage2.sh <implementation-sha> <out-folder>"; exit 2; }
rm -rf "$OUT"; mkdir -p "$OUT/trx" "$OUT/gates"
RUN=$(git -C "$R" rev-parse HEAD)
{
  echo "run commit: $RUN"
  echo "implementation commit: $IMPL"
  echo "working tree before: $(git -C "$R" status --porcelain | wc -l) changed paths (0 = clean)"
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
gate tsc npx tsc --noEmit
gate vitest npx vitest run
gate build npm run build
gate lint npm run lint
gate playwright_mocked npx playwright test
cd "$R" || exit 1
gate repo_checks node --test tests/*.test.mjs
sh "$H/leak_counts.sh" after > "$OUT/leak_after.txt" 2>&1
echo "end: $(date -Iseconds)" >> "$OUT/summary.txt"
echo "working tree after: $(git -C "$R" status --porcelain | wc -l) changed paths" >> "$OUT/summary.txt"
python "$H/evaluate_stage2.py" "$OUT" "$BASE" "$IMPL" "$RUN" > "$OUT/RESULT_ALV-N016.txt"; n=$?
cat "$OUT/RESULT_ALV-N016.txt"
python "$H/evaluate_stage2_alv007c01.py" "$OUT" "$BASE" "$IMPL" "$RUN" > "$OUT/RESULT_ALV-007-C01.txt"; m=$?
cat "$OUT/RESULT_ALV-007-C01.txt"
[ $n -eq 0 ] && [ $m -eq 0 ]; exit $?
