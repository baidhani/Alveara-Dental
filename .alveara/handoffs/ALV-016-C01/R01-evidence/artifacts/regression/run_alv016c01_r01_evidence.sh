#!/bin/sh
# ALV-016-C01: the frontend and repository gates, then the real-backend walkthroughs of the story, its parent and the changed neighbours, one after another on a quiet machine.
OUT="C:/tmp/c016c01/r01"; rm -rf "$OUT"; mkdir -p "$OUT/gates"; : > "$OUT/summary.txt"
cd "C:/Users/firas/Documents/AI-Project-[Alveara-Dental]/src/alveara-client" || exit 1
gate() { name="$1"; shift; "$@" > "$OUT/gates/$name.txt" 2>&1; code=$?; echo "== gate $name exit=$code" >> "$OUT/summary.txt"; tail -6 "$OUT/gates/$name.txt" | cut -c1-200 >> "$OUT/summary.txt"; }
gate tsc npx tsc --noEmit
gate vitest npx vitest run
gate build npm run build
gate lint npm run lint
gate playwright_mocked npx playwright test
cd "C:/Users/firas/Documents/AI-Project-[Alveara-Dental]" || exit 1
gate repo_checks node --test tests/*.test.mjs
echo GATES_DONE >> "$OUT/summary.txt"
run() { sh /c/tmp/run_e2e.sh "alveara_c16_$1" "$2" "$3" "$OUT/$1" > "$OUT/$1.txt" 2>&1; echo "== walkthrough $1" >> "$OUT/summary.txt"; grep -E "passed|failed|did not run" "$OUT/$1.txt" | tail -3 >> "$OUT/summary.txt"; }
run prescription_workflow playwright.prescription-workflow.config.ts PRESCRIPTION_WORKFLOW_E2E_OUT
run prescriptions playwright.prescriptions.config.ts PRESCRIPTIONS_E2E_OUT
run workspace playwright.workspace.config.ts C16_OUT
run safety playwright.safety.config.ts C16_OUT
run clinical playwright.clinical.config.ts C16_OUT
run clinical_companion playwright.clinical-companion.config.ts C16_OUT
run diagnoses playwright.diagnoses.config.ts C16_OUT
run treatment_plans playwright.treatment-plans.config.ts C16_OUT
run auth playwright.auth.config.ts C16_OUT
run patients playwright.patients.config.ts C16_OUT
echo DONE >> "$OUT/summary.txt"
