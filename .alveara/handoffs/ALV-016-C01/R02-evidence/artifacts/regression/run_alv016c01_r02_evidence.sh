#!/bin/sh
# ALV-016-C01 R02: the frontend and repository gates, then the real-backend walkthroughs of the story, its parent and the changed neighbours, one after another on a quiet machine.
OUT="C:/tmp/c016c01/r02"; rm -rf "$OUT"; mkdir -p "$OUT/gates"; : > "$OUT/summary.txt"
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
run() { sh /c/tmp/run_e2e.sh "alveara_c16r2_$1" "$2" "$3" "$OUT/$1" > "$OUT/$1.txt" 2>&1; echo "== walkthrough $1" >> "$OUT/summary.txt"; grep -E "passed|failed|did not run" "$OUT/$1.txt" | tail -3 >> "$OUT/summary.txt"; }
run completion_adoption playwright.completion-adoption.config.ts COMPLETION_ADOPTION_E2E_OUT
run completion playwright.completion.config.ts COMPLETION_E2E_OUT
run treatment_plans_complete playwright.treatment-plans-complete.config.ts TREATMENT_PLANS_COMPLETE_E2E_OUT
run treatment_plans playwright.treatment-plans.config.ts C16R2_OUT
run billing playwright.billing.config.ts BILLING_E2E_OUT
run odontogram playwright.odontogram.config.ts ODONTOGRAM_E2E_OUT
run prescription_workflow playwright.prescription-workflow.config.ts PRESCRIPTION_WORKFLOW_E2E_OUT
run prescriptions playwright.prescriptions.config.ts PRESCRIPTIONS_E2E_OUT
echo DONE >> "$OUT/summary.txt"
