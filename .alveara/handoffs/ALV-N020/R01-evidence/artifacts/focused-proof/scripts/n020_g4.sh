#!/bin/bash
# ALV-N020 group 4: the three affected real-backend walkthroughs, through the existing runner, one after another.
R="C:/Users/firas/Documents/AI-Project-[Alveara-Dental]"
OUT="C:/tmp/n020-proof/g4"
mkdir -p "$OUT"
LOG="$OUT/SUMMARY.txt"
: > "$LOG"
sh "$R/.alveara/handoffs/ALV-N019/R04-evidence/stage2/leak_counts.sh" before > "$OUT/leak_before.txt" 2>&1
skip=""
for cfg in playwright.auth.config.ts playwright.workspace.config.ts playwright.permission-policy-admin.config.ts; do
  t0=$(date +%s)
  powershell -NoProfile -Command "Set-Location -LiteralPath '$R'; .\scripts\Invoke-RealBackendWalkthrough.ps1 -Config $cfg $skip" > "$OUT/walk_$cfg.txt" 2>&1
  code=$?
  echo "== walk $cfg exit=$code elapsed=$(( $(date +%s) - t0 ))s :: $(grep -E '[0-9]+ (passed|failed|skipped|flaky)' "$OUT/walk_$cfg.txt" | tr -s ' ' | tr '\n' ' ')" >> "$LOG"
  skip="-SkipBuild"
done
sh "$R/.alveara/handoffs/ALV-N019/R04-evidence/stage2/leak_counts.sh" after > "$OUT/leak_after.txt" 2>&1
if diff <(tail -n +2 "$OUT/leak_before.txt") <(tail -n +2 "$OUT/leak_after.txt") > /dev/null; then echo "== leftovers: none" >> "$LOG"; else echo "== leftovers: COUNTS DIFFER" >> "$LOG"; fi
echo "== G4 done" >> "$LOG"
cat "$LOG"
