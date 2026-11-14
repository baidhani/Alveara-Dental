#!/bin/sh
# ALV-N021 A1 negative controls: break one fix at a time, run only the class that must catch it, restore the committed file, prove it is byte-identical.
cd "/c/Users/firas/Documents/AI-Project-[Alveara-Dental]" || exit 1
echo "commit $(git rev-parse HEAD)  start $(date +%T)"

control() { # name file python-old python-new runner
  name="$1"; file="$2"; old="$3"; new="$4"; shift 4
  before=$(git hash-object "$file")
  python -c "import sys;p=sys.argv[1];s=open(p,encoding='utf-8',newline='').read();o,n=sys.argv[2],sys.argv[3];assert s.count(o)==1,'anchor not unique';open(p,'w',encoding='utf-8',newline='').write(s.replace(o,n))" "$file" "$old" "$new" || { echo "$name: MUTATION NOT APPLIED"; return; }
  echo "=== $name  mutated $file  $(date +%T)"
  "$@" > "/c/tmp/negctl_$name.log" 2>&1; rc=$?
  grep -E "Failed |Passed!|Failed!|Tests +[0-9]|✗|×|FAIL" "/c/tmp/negctl_$name.log" | head -12
  git checkout -- "$file"
  after=$(git hash-object "$file")
  [ "$before" = "$after" ] && r=identical || r=DIFFERENT
  echo "$name: exit $rc (non-zero expected)  restored $r  $(date +%T)"
}

control F-107 src/alveara-client/src/pages/prescription/workflow/PrintPreview.tsx \
  "(born {formatDateOnly(content.patientDateOfBirth)})" "(born {day(content.patientDateOfBirth)})" \
  sh -c "cd src/alveara-client && npx vitest run src/components/dateOnly.test.tsx"

dotnet build src/Alveara.Api.Tests -v q > /dev/null 2>&1

control F-118 src/Alveara.Api/Architecture/Billing/BillingService.Timeline.cs \
  "performed {clock.ToPracticeLocal(service[e.EntryNumber]):yyyy-MM-dd}" "performed {service[e.EntryNumber]:yyyy-MM-dd}" \
  dotnet test src/Alveara.Api.Tests --filter "FullyQualifiedName~BillingTimelineAgingTests"

control PERIO-LABEL src/Alveara.Api/Architecture/Clinical/DiagnosisService.Structure.cs \
  "chart of {clock.ToPracticeLocal(c.RecordedAtUtc):yyyy-MM-dd}" "chart of {c.RecordedAtUtc:yyyy-MM-dd}" \
  dotnet test src/Alveara.Api.Tests --filter "FullyQualifiedName~DiagnosisStructureServiceTests"

control F-184 src/Alveara.Api/Architecture/Treatment/TreatmentPlanService.Queries.cs \
  "h => AnswerRow(h.Value));" "h => AnswerRow(h.Value) is null ? null : h.Value[^1]);" \
  dotnet test src/Alveara.Api.Tests --filter "FullyQualifiedName~TreatmentPlanStateServiceTests"

dotnet build src/Alveara.Api.Tests -v q > /dev/null 2>&1; echo "rebuilt on restored tree exit $?"
git status --short
echo "end $(date +%T)"
