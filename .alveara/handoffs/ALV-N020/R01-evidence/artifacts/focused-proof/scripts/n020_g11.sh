#!/bin/bash
# ALV-N020 group 11 (owner-approved 2026-11-12): the added acceptance tests (client, server) and NC-5.
R="C:/Users/firas/Documents/AI-Project-[Alveara-Dental]"
OUT="C:/tmp/n020-proof/g11"
mkdir -p "$OUT"
LOG="$OUT/SUMMARY.txt"
: > "$LOG"
LEAK="$R/.alveara/handoffs/ALV-N019/R04-evidence/stage2/leak_counts.sh"
sh "$LEAK" before > "$OUT/leak_before.txt" 2>&1

t0=$(date +%s)
cd "$R/src/alveara-client"
npx vitest run src/services/authApi.loginTimeout.test.ts src/App.routeGuards.test.tsx > "$OUT/client.txt" 2>&1
echo "== client exit=$? elapsed=$(( $(date +%s) - t0 ))s :: $(grep -E 'Tests +[0-9]' "$OUT/client.txt" | tr -s ' ')" >> "$LOG"
grep -E "^\s*(×|FAIL)" "$OUT/client.txt" >> "$LOG"

t0=$(date +%s)
cd "$R/src/Alveara.Api.Tests"
dotnet test --nologo --filter "FullyQualifiedName~StaffInactivationAlertTests|FullyQualifiedName~ConfigurationApiTests" --logger "trx;LogFileName=g11.trx" --results-directory "C:/tmp/n020-proof/g11" > "$OUT/server.txt" 2>&1
echo "== server exit=$? elapsed=$(( $(date +%s) - t0 ))s :: $(grep -E '(Passed|Failed)!' "$OUT/server.txt" | tr -s ' ')" >> "$LOG"
grep -E "^\s+Failed Alveara" "$OUT/server.txt" >> "$LOG"

sh "$LEAK" after > "$OUT/leak_after.txt" 2>&1
if diff <(tail -n +2 "$OUT/leak_before.txt") <(tail -n +2 "$OUT/leak_after.txt") > /dev/null; then echo "== leftovers: none" >> "$LOG"; else echo "== leftovers: COUNTS DIFFER" >> "$LOG"; fi

t0=$(date +%s)
cd "$R"
python "C:/Users/firas/AppData/Local/Temp/claude/dfe629a69afb811c/b77e3b07-5179-44fb-8085-a45449d8984e/scratchpad/n020_nc5.py" > "$OUT/nc5.txt" 2>&1
echo "== nc5 exit=$? elapsed=$(( $(date +%s) - t0 ))s" >> "$LOG"
cat "$OUT/nc5.txt" >> "$LOG"
echo "== G11 done" >> "$LOG"
cat "$LOG"
