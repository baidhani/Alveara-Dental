#!/bin/sh
# ALV-N021 A2 final evidence run, part 1 (cheap first; stops at the first failing stage). ApiTests are NOT here (asked separately).
OUT="$1"; R="C:/Users/firas/Documents/AI-Project-[Alveara-Dental]"; T="$2"
rm -rf "$OUT"; mkdir -p "$OUT/trx" "$OUT/gates"; S="$OUT/summary.txt"
{ echo "run commit: $(git -C "$R" rev-parse HEAD)"; echo "tree changed paths: $(git -C "$R" status --porcelain | wc -l)"; echo "start: $(date -Iseconds)"; } > "$S"
stop() { echo "== STOPPED after $1" >> "$S"; echo "end: $(date -Iseconds)" >> "$S"; exit 1; }
cd "$R/src/alveara-client" || exit 1
gate() { name="$1"; shift; g0=$(date +%s); "$@" > "$OUT/gates/$name.txt" 2>&1; code=$?; echo "== gate $name exit=$code elapsed=$(( $(date +%s) - g0 ))s: $(grep -E 'Tests  |Test Files|passed|failed|built in' "$OUT/gates/$name.txt" | tail -3 | tr '\n' ' ')" >> "$S"; [ $code -eq 0 ] || stop "$name"; }
gate tsc npx tsc --noEmit
gate build npm run build
gate lint npm run lint
gate vitest npx vitest run
gate playwright_mocked npx playwright test
rm -rf .vitest test-results
cd "$R" || exit 1
gate repo_checks node --test tests/*.test.mjs
cd "$R/src" || exit 1
gate backend_build dotnet build Alveara.Api.Tests -nologo -v q
F="FullyQualifiedName~Visit|FullyQualifiedName~Appointment|FullyQualifiedName~Checkout|FullyQualifiedName~Scheduling|FullyQualifiedName~BlockedTime|FullyQualifiedName~ProviderReassignment|FullyQualifiedName~Cancellation|FullyQualifiedName~UndoCheckIn|FullyQualifiedName~CheckInDst|FullyQualifiedName~PatientFlow|FullyQualifiedName~PracticeConfiguration|FullyQualifiedName~PermissionMatrix|FullyQualifiedName~PermissionPolicy|FullyQualifiedName~Migration|FullyQualifiedName~Schema"
gate backend_focused dotnet test Alveara.Api.Tests -nologo --no-build --filter "$F" --logger "trx;LogFileName=backend-focused.trx" --results-directory "$OUT/trx"
grep -E 'Passed!|Failed!' "$OUT/gates/backend_focused.txt" | tail -1 >> "$S"
w0=$(date +%s); powershell -NoProfile -ExecutionPolicy Bypass -File "$T/run_walkthroughs_a2.ps1" -Out "$OUT/walk" > "$OUT/gates/walkthroughs.txt" 2>&1
cat "$OUT/walk/summary.txt" >> "$S"; echo "== walkthroughs elapsed=$(( $(date +%s) - w0 ))s" >> "$S"
grep -q "STOPPED" "$OUT/walk/summary.txt" && stop walkthroughs
c0=$(date +%s); powershell -NoProfile -ExecutionPolicy Bypass -File "$T/run_chain.ps1" -Out "$OUT/chain" > "$OUT/gates/chain.txt" 2>&1; code=$?
echo "== chain exit=$code elapsed=$(( $(date +%s) - c0 ))s: $(tail -3 "$OUT/gates/chain.txt" | tr '\n' ' ')" >> "$S"
echo "end: $(date -Iseconds)" >> "$S"; echo "tree changed paths after: $(git -C "$R" status --porcelain | wc -l)" >> "$S"; echo "== PART 1 DONE" >> "$S"
