#!/bin/bash
# ALV-N020 R02 (owner-approved 2026-11-12): the chain (stage 0 + stage 1) and the runner self-test, one after the other.
R="C:/Users/firas/Documents/AI-Project-[Alveara-Dental]"
OUT="C:/tmp/n020-r02"
mkdir -p "$OUT"
LOG="$OUT/SUMMARY-chain.txt"
: > "$LOG"
LEAK="$R/.alveara/handoffs/ALV-N019/R04-evidence/stage2/leak_counts.sh"
sh "$LEAK" before > "$OUT/chain-leak_before.txt" 2>&1

t0=$(date +%s)
powershell -NoProfile -Command "Set-Location -LiteralPath '$R'; .\scripts\Invoke-AccumulatedChain.ps1 -ClockMode injected" > "$OUT/chain-output.txt" 2>&1
code=$?
echo "== chain exit=$code elapsed=$(( $(date +%s) - t0 ))s :: $(grep -E '[0-9]+ (passed|failed|skipped|flaky)' "$OUT/chain-output.txt" | tr -s ' ' | tr '\n' ' ')" >> "$LOG"
grep -E "^(FAIL|SKIPPED|TRACKED)|API control|FRESH run|API-STOP|API-START" "$OUT/chain-output.txt" >> "$LOG"
run=$(grep -oE "chain-output\\\\[0-9a-f]{32}" "$OUT/chain-output.txt" | head -1 | sed 's/.*\\//')
[ -n "$run" ] && cp -r "C:/.alvera-data/Test-Data/Temp/chain-output/$run" "$OUT/chain-evidence" && echo "== evidence copied ($run)" >> "$LOG"

t0=$(date +%s)
cd "$R"
node --test tests/stabilization/chain-runner-resilience.test.mjs > "$OUT/runner-selftest.txt" 2>&1
echo "== runner self-test exit=$? elapsed=$(( $(date +%s) - t0 ))s :: $(grep -E '^# (tests|pass|fail|skipped)' "$OUT/runner-selftest.txt" | tr '\n' ' ')" >> "$LOG"

sh "$LEAK" after > "$OUT/chain-leak_after.txt" 2>&1
if diff <(tail -n +2 "$OUT/chain-leak_before.txt") <(tail -n +2 "$OUT/chain-leak_after.txt") > /dev/null; then echo "== leftovers: none" >> "$LOG"; else echo "== leftovers: COUNTS DIFFER" >> "$LOG"; fi
echo "== R02 RUNS done" >> "$LOG"
cat "$LOG"
