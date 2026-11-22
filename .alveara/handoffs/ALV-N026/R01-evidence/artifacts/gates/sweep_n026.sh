#!/usr/bin/env bash
# ALV-N026 R01 focused Stage 1 sweep on the final implementation commit. Each server group is class-exact, timed and kept as a trx; then the client gates
# and the repo-root node checks. Leak counts (leftover AlveraTest_ databases) before and after. Stops (exit 3) after a failing group.
set -u
OUT="$1"; mkdir -p "$OUT"
ROOT="/c/Users/firas/Documents/AI-Project-[Alveara-Dental]"
cd "$ROOT/src"
log() { echo "$*" | tee -a "$OUT/sweep.log"; }
leaks() { sqlcmd -S ".\SQLEXPRESS" -E -h -1 -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.databases WHERE name LIKE 'AlveraTest[_]%'" | tr -d ' \r\n'; }
log "ALV-N026 sweep on $(git rev-parse --short HEAD) ($(git status --short | wc -l) changed files in the tree) - $(date -Iseconds)"
log "leak count before: $(leaks) AlveraTest_ database(s)"
dotnet build Alveara.Api.Tests --nologo -v q > "$OUT/build.log" 2>&1; log "build: exit $?"

group() {
  local name="$1"; shift
  local filter=""; for c in "$@"; do filter="${filter:+$filter|}FullyQualifiedName~Tests.$c."; done
  local t0=$(date +%s)
  dotnet test Alveara.Api.Tests --nologo --no-build --filter "$filter" --logger "trx;LogFileName=$name.trx" --results-directory "$OUT" > "$OUT/$name.log" 2>&1
  local code=$? secs=$(( $(date +%s) - t0 ))
  log "$name: exit $code, ${secs}s - $(grep -E '(Passed!|Failed!) +-' "$OUT/$name.log" | tail -1 | sed 's/^ *//') - classes: $*"
  grep -E '^\s+Failed Alveara' "$OUT/$name.log" | tee -a "$OUT/sweep.log"
  if [ $code -ne 0 ]; then log "STOP: $name failed"; log "leak count after: $(leaks)"; exit 3; fi
}

group G1-documents DocumentApiTests DocumentBackupRestoreTests DocumentClassificationAccessApiTests DocumentContextServiceTests DocumentImportServiceTests DocumentIntegrityServiceTests DocumentLibraryApiTests DocumentLibraryFilterTests DocumentLibraryServiceTests DocumentOrphanServiceTests DocumentPermissionTests DocumentSettingsServiceTests DocumentStorageApiTests ExternalCaseControllerPermissionTests ExternalCaseServiceTests ExternalCaseApiTests
group G2-backup-status BackupAlertApiTests BackupAlertTests BackupApiTests BackupCryptoTests SystemStatusTests
group G3-pdf-engine PdfEngineTests InstallPdfEngineScriptTests
group G4-financial BillingDocumentTemplateVersionServiceTests BillingStatementsApiTests BillingDocumentServiceTests
group G5-prescriptions PrescriptionIssueOwnershipTests PrescriptionPdfTextTests PrescriptionWorkflowApiTests PrescriptionWorkflowServiceTests
group G6-plan-copy TreatmentPlanCopyApiTests TreatmentPlanCopyIssueOwnershipTests TreatmentPlanCopyDiagnosisLabelTests
group G7-record-copy-merge ClinicalRecordCopyApiTests ClinicalRecordCopyOwnershipTests ExportDomainCatalogTests PatientOwnedDataTests PatientIdExceptionTests TreatmentPlanItemAppointmentLinkMergeTests PatientMergeSchemaTests RetiredRecordGuardTests MergeTriggerIntegrityTests PatientMergeApiTests PatientUnmergeServiceTests

cd alveara-client
t0=$(date +%s); npx tsc --noEmit -p tsconfig.app.json > "$OUT/tsc-app.log" 2>&1; log "tsc app: exit $? ($(( $(date +%s) - t0 ))s)"
t0=$(date +%s); npx tsc --noEmit -p tsconfig.node.json > "$OUT/tsc-node.log" 2>&1; log "tsc node: exit $? ($(( $(date +%s) - t0 ))s)"
t0=$(date +%s); npm run lint > "$OUT/lint.log" 2>&1; log "lint: exit $? ($(( $(date +%s) - t0 ))s) - $(grep -E '[0-9]+ problems?' "$OUT/lint.log" | tail -1)"
t0=$(date +%s); npx vite build --outDir "$TEMP/n026-sweep-build" --emptyOutDir > "$OUT/build-client.log" 2>&1; log "vite build: exit $? ($(( $(date +%s) - t0 ))s)"
t0=$(date +%s); npx vitest run src/pages/documents src/app/BackupAlertBanner.test.tsx src/app/AppShell.permissionAwareNav.test.tsx src/components/dateOnly.test.tsx src/pages/BackupRecoveryPage.test.tsx src/pages/SystemStatusPage.test.tsx src/pages/billing src/pages/exports src/pages/prescription src/pages/treatmentplan/TreatmentPlanPatientCopy.test.tsx src/pages/treatmentplan/TreatmentPlanN023.test.tsx src/services/pdfApi.test.ts > "$OUT/vitest.log" 2>&1; code=$?
log "vitest (focused): exit $code ($(( $(date +%s) - t0 ))s) - $(grep -E 'Tests +[0-9]' "$OUT/vitest.log" | tail -1 | sed 's/^ *//')"
grep -E '^ *(×|FAIL )' "$OUT/vitest.log" | head -20 | tee -a "$OUT/sweep.log"
cd "$ROOT"; t0=$(date +%s); node --test tests/*.test.mjs > "$OUT/node-tests.log" 2>&1; log "node --test: exit $? ($(( $(date +%s) - t0 ))s) - $(grep -E '^# (pass|fail)' "$OUT/node-tests.log" | tr '\n' ' ')"
log "leak count after: $(leaks) AlveraTest_ database(s)"
log "done $(date -Iseconds)"
