#!/usr/bin/env bash
# ALV-N025 run D: the final focused sweep on the committed final tree. Each server group is class-exact, timed and kept as a trx; then the client gates.
# Stops (exit 3) after a group that fails or takes longer than 300 s, as the owner asked.
set -u
OUT="$1"; mkdir -p "$OUT"
cd "/c/Users/firas/Documents/AI-Project-[Alveara-Dental]/src"
log() { echo "$*" | tee -a "$OUT/sweep.log"; }
log "ALV-N025 sweep D on $(git rev-parse --short HEAD) ($(git status --short | wc -l) changed files in the tree) - $(date -Iseconds)"
dotnet build Alveara.Api.Tests --nologo -v q > "$OUT/build.log" 2>&1; log "build: exit $?"

group() {
  local name="$1"; shift
  local filter=""; for c in "$@"; do filter="${filter:+$filter|}FullyQualifiedName~Tests.$c."; done
  local t0=$(date +%s)
  dotnet test Alveara.Api.Tests --nologo --no-build --filter "$filter" --logger "trx;LogFileName=$name.trx" --results-directory "$OUT" > "$OUT/$name.log" 2>&1
  local code=$? secs=$(( $(date +%s) - t0 ))
  log "$name: exit $code, ${secs}s - $(grep -E '(Passed!|Failed!) +-' "$OUT/$name.log" | tail -1 | sed 's/^ *//') - classes: $*"
  grep -E '^\s+Failed Alveara' "$OUT/$name.log" | tee -a "$OUT/sweep.log"
  if [ $code -ne 0 ] || [ $secs -gt 300 ]; then log "STOP: $name failed or exceeded 300 s"; exit 3; fi
}

# (done on 3dcaf8d) group G1-identity PatientDuplicateWarningTests PatientRegistrationServiceTests PatientIdentityApiTests PatientDuplicateScannerTests PatientDuplicateReviewTests ImportPatientDryRunTests ImportCommitTests
# (done on 3dcaf8d) group G2-mrn-merge MrnTests MrnMigrationTests MergeTriggerIntegrityTests PatientMergeSchemaTests PatientUnmergeSchemaTests PatientMergeServiceTests RetiredRecordGuardTests HouseholdDissolveTests
# (done on 3dcaf8d) group G3-demographics-files DemographicsValidationTests DemographicsMigrationTests PatientExportServiceTests PatientImportValidatorTests PatientImportServiceTests PatientExportApiTests PatientImportApiTests PatientExportsApiTests
group G4-audit-access AuditLogApiTests AuditSessionLifecycleTests PatientAccessTrailTests DocumentApiTests AuthControllerMfaConfirmSessionTests AccountServiceMfaTests ObservabilityClassificationTests PermissionPolicyWriteTests AppointmentSchedulerTests

cd alveara-client
t0=$(date +%s); npx tsc --noEmit -p tsconfig.app.json > "$OUT/tsc-app.log" 2>&1; log "tsc app: exit $? ($(( $(date +%s) - t0 ))s)"
t0=$(date +%s); npx tsc --noEmit -p tsconfig.node.json > "$OUT/tsc-node.log" 2>&1; log "tsc node: exit $? ($(( $(date +%s) - t0 ))s)"
t0=$(date +%s); npm run lint > "$OUT/lint.log" 2>&1; log "lint: exit $? ($(( $(date +%s) - t0 ))s) - $(grep -E '[0-9]+ problems?' "$OUT/lint.log" | tail -1)"
t0=$(date +%s); npx vite build --outDir "$TEMP/n025-sweep-build" --emptyOutDir > "$OUT/build-client.log" 2>&1; log "vite build: exit $? ($(( $(date +%s) - t0 ))s)"
t0=$(date +%s); npx vitest run > "$OUT/vitest.log" 2>&1; code=$?; log "vitest: exit $code ($(( $(date +%s) - t0 ))s) - $(grep -E 'Tests +[0-9]' "$OUT/vitest.log" | tail -1 | sed 's/^ *//')"
grep -E '^ *(×|FAIL )' "$OUT/vitest.log" | head -20 | tee -a "$OUT/sweep.log"
log "done $(date -Iseconds)"
