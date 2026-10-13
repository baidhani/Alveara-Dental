#!/bin/sh
# Runs each real-backend walkthrough against its own fresh database, one after another (they share the API port): every existing real-backend walkthrough, then the STORY-015 and ALV-015-C01 treatment plan walkthroughs.
OUT="C:/tmp/c08c02/sweep"; mkdir -p "$OUT"
cd "C:/Users/firas/Documents/AI-Project-[Alveara-Dental]"
run() { # name config
  sh /c/tmp/run_e2e.sh "alveara_c08s_$1" "$2" "C01_OUT" "$OUT/$1" > "$OUT/$1.txt" 2>&1
  echo "== $1" >> "$OUT/summary.txt"; grep -E "passed|failed|did not run" "$OUT/$1.txt" | tail -3 >> "$OUT/summary.txt"
}
: > "$OUT/summary.txt"
run auth playwright.auth.config.ts
run patients playwright.patients.config.ts
run workspace playwright.workspace.config.ts
run forms playwright.forms.config.ts
run schedule playwright.schedule.config.ts
run calendar playwright.calendar.config.ts
run flow playwright.flow.config.ts
run board playwright.board.config.ts
run clinical playwright.clinical.config.ts
run companion playwright.clinical-companion.config.ts
run safety playwright.safety.config.ts
run odontogram playwright.odontogram.config.ts
run odontogram_longitudinal playwright.odontogram-longitudinal.config.ts
run perio playwright.perio.config.ts
run perio_sessions playwright.perio-sessions.config.ts
run diagnoses playwright.diagnoses.config.ts
run diagnoses_structure playwright.diagnoses-structure.config.ts
run procedures playwright.procedures.config.ts
sh /c/tmp/run_e2e.sh "alveara_c08s_treatment_plans" playwright.treatment-plans.config.ts "TREATMENT_PLANS_E2E_OUT" "$OUT/treatment_plans" > "$OUT/treatment_plans.txt" 2>&1
echo "== treatment_plans" >> "$OUT/summary.txt"; grep -E "passed|failed|did not run" "$OUT/treatment_plans.txt" | tail -3 >> "$OUT/summary.txt"
sh /c/tmp/run_e2e.sh "alveara_c08s_treatment_plans_complete" playwright.treatment-plans-complete.config.ts "TREATMENT_PLANS_COMPLETE_E2E_OUT" "$OUT/treatment_plans_complete" > "$OUT/treatment_plans_complete.txt" 2>&1
echo "== treatment_plans_complete" >> "$OUT/summary.txt"; grep -E "passed|failed|did not run" "$OUT/treatment_plans_complete.txt" | tail -3 >> "$OUT/summary.txt"
sh /c/tmp/run_e2e.sh "alveara_c08s_billing" playwright.billing.config.ts "BILLING_E2E_OUT" "$OUT/billing" > "$OUT/billing.txt" 2>&1
echo "== billing" >> "$OUT/summary.txt"; grep -E "passed|failed|did not run" "$OUT/billing.txt" | tail -3 >> "$OUT/summary.txt"
sh /c/tmp/run_e2e.sh "alveara_c08s_billing_ledger" playwright.billing-ledger.config.ts "BILLING_LEDGER_E2E_OUT" "$OUT/billing_ledger" > "$OUT/billing_ledger.txt" 2>&1
echo "== billing_ledger" >> "$OUT/summary.txt"; grep -E "passed|failed|did not run" "$OUT/billing_ledger.txt" | tail -3 >> "$OUT/summary.txt"
sh /c/tmp/run_e2e.sh "alveara_c08s_adoption" playwright.completion-adoption.config.ts "COMPLETION_ADOPTION_E2E_OUT" "$OUT/adoption" > "$OUT/adoption.txt" 2>&1
echo "== adoption" >> "$OUT/summary.txt"; grep -E "passed|failed|did not run" "$OUT/adoption.txt" | tail -3 >> "$OUT/summary.txt"
sh /c/tmp/run_e2e.sh "alveara_c08s_completion" playwright.completion.config.ts "COMPLETION_E2E_OUT" "$OUT/completion" > "$OUT/completion.txt" 2>&1
echo "== completion" >> "$OUT/summary.txt"; grep -E "passed|failed|did not run" "$OUT/completion.txt" | tail -3 >> "$OUT/summary.txt"
echo DONE >> "$OUT/summary.txt"
cd "C:/Users/firas/Documents/AI-Project-[Alveara-Dental]"
sh /c/tmp/run_e2e.sh "alveara_c08s_statements" playwright.billing-statements.config.ts "BILLING_STATEMENTS_E2E_OUT" "$OUT/statements" > "$OUT/statements.txt" 2>&1
echo "== statements" >> "$OUT/summary.txt"; grep -E "passed|failed|did not run" "$OUT/statements.txt" | tail -3 >> "$OUT/summary.txt"
echo DONE2 >> "$OUT/summary.txt"
