#!/bin/sh
# ALV-003-C01 R03: the shared frontend-only gate (for ALV-003-C01 R03, ALV-N016 R04 and ALV-007-C01 R03), run ONCE on the clean frozen closure tree after the Stage 1 approvals. No backend.
# usage: sh run_frontend_gate.sh <implementation-sha> <out-folder>
IMPL="$1"; OUT="$2"
R="C:/Users/firas/Documents/AI-Project-[Alveara-Dental]"
H="$R/.alveara/handoffs/ALV-003-C01/R03-evidence/stage2"
[ -n "$IMPL" ] && [ -n "$OUT" ] || { echo "usage: run_frontend_gate.sh <implementation-sha> <out-folder>"; exit 2; }
rm -rf "$OUT"; mkdir -p "$OUT/gates"
{
  echo "run commit: $(git -C "$R" rev-parse HEAD)"
  echo "implementation commit: $IMPL"
  echo "working tree before: $(git -C "$R" status --porcelain | wc -l) changed paths (0 = clean)"
  echo "start: $(date -Iseconds)"
} > "$OUT/summary.txt"
cd "$R/src/alveara-client" || exit 1
gate() { name="$1"; shift; g0=$(date +%s); "$@" > "$OUT/gates/$name.txt" 2>&1; code=$?; echo "== gate $name exit=$code elapsed=$(( $(date +%s) - g0 ))s" >> "$OUT/summary.txt"; }
gate tsc npx tsc --noEmit
gate vitest npx vitest run
gate build npm run build
gate lint npm run lint
gate playwright_mocked npx playwright test
cd "$R" || exit 1
gate repo_checks node --test tests/*.test.mjs
echo "end: $(date -Iseconds)" >> "$OUT/summary.txt"
echo "working tree after: $(git -C "$R" status --porcelain | wc -l) changed paths" >> "$OUT/summary.txt"
python "$H/evaluate_frontend_gate.py" "$OUT" "$IMPL"
exit $?
