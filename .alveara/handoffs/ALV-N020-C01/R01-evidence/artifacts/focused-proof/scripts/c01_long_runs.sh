#!/bin/bash
# ALV-N020-C01 runs over five minutes (owner's explicit permission 2026-11-12): negative controls, chain stage 0 + stage 1, auth walkthrough.
R="C:/Users/firas/Documents/AI-Project-[Alveara-Dental]"
OUT="C:/tmp/n020c01"
LOG="$OUT/SUMMARY-long.txt"
: > "$LOG"
LEAK="$R/.alveara/handoffs/ALV-N019/R04-evidence/stage2/leak_counts.sh"
sh "$LEAK" before > "$OUT/long-leak_before.txt" 2>&1

t0=$(date +%s)
cd "$R"
python "C:/Users/firas/AppData/Local/Temp/claude/dfe629a69afb811c/b77e3b07-5179-44fb-8085-a45449d8984e/scratchpad/c01_negative_controls.py" > "$OUT/nc-output.txt" 2>&1
echo "== negative controls exit=$? elapsed=$(( $(date +%s) - t0 ))s (expected ~7 min)" >> "$LOG"
cat "$OUT/nc-output.txt" >> "$LOG"

t0=$(date +%s)
powershell -NoProfile -Command "Set-Location -LiteralPath '$R'; .\scripts\Invoke-AccumulatedChain.ps1 -ClockMode injected" > "$OUT/chain-output.txt" 2>&1
echo "== chain exit=$? elapsed=$(( $(date +%s) - t0 ))s (expected ~6.5 min) :: $(grep -E '[0-9]+ (passed|failed|skipped|flaky)' "$OUT/chain-output.txt" | tr -s ' ' | tr '\n' ' ')" >> "$LOG"
grep -E "^(FAIL|SKIPPED|TRACKED)|FRESH run|API control" "$OUT/chain-output.txt" >> "$LOG"
run=$(grep -oE "chain-output\\\\[0-9a-f]{32}" "$OUT/chain-output.txt" | head -1 | sed 's/.*\\//')
[ -n "$run" ] && cp -r "C:/.alvera-data/Test-Data/Temp/chain-output/$run" "$OUT/chain-evidence" && echo "== chain evidence copied ($run)" >> "$LOG"

t0=$(date +%s)
powershell -NoProfile -Command "Set-Location -LiteralPath '$R'; .\scripts\Invoke-RealBackendWalkthrough.ps1 -Config playwright.auth.config.ts" > "$OUT/walk-auth.txt" 2>&1
echo "== auth walkthrough exit=$? elapsed=$(( $(date +%s) - t0 ))s (expected ~2 min) :: $(grep -E '[0-9]+ (passed|failed|skipped|flaky)' "$OUT/walk-auth.txt" | tr -s ' ' | tr '\n' ' ')" >> "$LOG"

sh "$LEAK" after > "$OUT/long-leak_after.txt" 2>&1
if diff <(tail -n +2 "$OUT/long-leak_before.txt") <(tail -n +2 "$OUT/long-leak_after.txt") > /dev/null; then echo "== leftovers: none" >> "$LOG"; else echo "== leftovers: COUNTS DIFFER" >> "$LOG"; fi
echo "== LONG RUNS done" >> "$LOG"
cat "$LOG"
