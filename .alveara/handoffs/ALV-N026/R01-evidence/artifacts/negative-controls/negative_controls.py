"""ALV-N026 R01: negative controls (copied from ALV-N025's script; controls replaced). Each control breaks ONE rule, rebuilds, runs only the test(s) that should catch it, restores the file byte-for-byte
and proves the restore with `git diff --exit-code`. A control PASSES when the targeted test(s) FAIL. Output: a log with every step, edit and result."""
import datetime, os, re, subprocess, sys, time

SRC = r"c:\Users\firas\Documents\AI-Project-[Alveara-Dental]\src"
LOG = sys.argv[1]
os.chdir(SRC)
log = open(LOG, "w", encoding="utf-8")

def say(text=""):
    print(text, flush=True); log.write(text + "\n"); log.flush()

def run(cmd, timeout):
    t = time.time()
    p = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=timeout, shell=True)
    return p.returncode, p.stdout + p.stderr, time.time() - t

CONTROLS = [
    ("C1 clinical-category read rule removed (ViewDocuments alone reads clinical documents)", r"Alveara.Api\Architecture\Documents\DocumentAccess.cs",
     "var clinical = nonClinical && await permissions.HasAsync(role, Permission.ViewClinicalDocumentation, ct);", "var clinical = nonClinical;", "one",
     "DocumentClassificationAccessApiTests.A_clinical_document_is_read_only_with_clinical_access_and_is_otherwise_answered_as_not_found"),
    ("C2 plan-copy integrity check on the recorded reference code removed", r"Alveara.Api\Architecture\Treatment\TreatmentPlanCopyIssues.cs",
     "            throw Mismatch(issue.Id, \"reference_code_changed\");", "            { }", "one",
     "TreatmentPlanCopyApiTests.A_copy_whose_recorded_sources_no_longer_match_is_refused_with_integrity_mismatch_before_rendering"),
    ("C3 record-copy key replay broken (the stored key is never found)", r"Alveara.Api\Architecture\Patients\Portability\Package\ClinicalRecordCopy.cs",
     "i.PatientId == patientId && i.IdempotencyKey == key, ct)", "i.PatientId == patientId && i.IdempotencyKey == key + \"#\", ct)", "all",
     "ClinicalRecordCopyApiTests.A_second_request_is_a_new_issue_and_a_retry_with_the_same_key_returns_the_same_issue"),
    ("C4 backup alert reaches only one administrator", r"Alveara.Api\Architecture\Backup\BackupAlerts.cs",
     "a.Role == Role.Admin).Select(a => a.Id).ToListAsync(ct);", "a.Role == Role.Admin).Select(a => a.Id).Take(1).ToListAsync(ct);", "one",
     "BackupAlertTests.A_failure_reaches_every_enabled_administrator_once_urgently_and_no_one_else_however_often_it_is_delivered"),
    ("C5 named exception silently dropped (PatientRecordOpens in neither list)", r"Alveara.Api\Architecture\Patients\Merge\PatientIdExceptions.cs",
     "    public static readonly IReadOnlyList<PatientIdException> All =\n    [",
     "    public static readonly IReadOnlyList<PatientIdException> All = [];\n    private static readonly IReadOnlyList<PatientIdException> Dropped =\n    [", "one",
     "PatientOwnedDataTests.The_registry_lists_exactly_the_columns_of_the_model_that_hold_a_patient_id|ExportDomainCatalogTests.Every_table_that_holds_a_patient_id_is_in_a_domain_the_package_carries|PatientIdExceptionTests"),
    ("C6 appointment-link merge stand-aside removed (U-022 repair undone)", r"Alveara.Api\Migrations\20261122140138_AddTreatmentPlanItemAppointmentLinksMergeScope.cs",
     "/*patient-merge*/ IF CONVERT(int, SESSION_CONTEXT(N'alveara.patient_merge')) = 1 AND", "/*patient-merge*/ IF 1 = 0 AND", "one",
     "TreatmentPlanItemAppointmentLinkMergeTests.An_approved_merge_moves_the_link_with_its_plan_item_and_appointment_and_its_undo_moves_it_back|PatientMergeSchemaTests.Every_update_trigger_on_those_tables_stands_aside_for_a_merge_except_the_merge_scope_triggers_and_the_merged_mark"),
]

started = time.time()
say(f"ALV-N026 negative controls - started {datetime.datetime.now().isoformat(timespec='seconds')}  (HEAD {run('git rev-parse --short HEAD', 30)[1].strip()})")
results = []
ONLY = sys.argv[2].split(',') if len(sys.argv) > 2 else None
for name, path, old, new, mode, tests in CONTROLS:
    if ONLY and name.split()[0] not in ONLY: continue
    say(""); say("=" * 100); say(name); say(f"  file: {path}")
    raw = open(path, "rb").read()
    text = raw.decode("utf-8-sig") if raw.startswith(b"\xef\xbb\xbf") else raw.decode("utf-8")
    bom = raw.startswith(b"\xef\xbb\xbf")
    crlf = "\r\n" in text
    norm = text.replace("\r\n", "\n")
    count = norm.count(old)
    if count == 0 or (mode == "one" and count != 1):
        say(f"  ABORT: the anchor occurs {count} time(s)"); results.append((name, "NOT RUN (anchor)")); continue
    changed = norm.replace(old, new) if mode == "all" else norm.replace(old, new, 1)
    out = changed.replace("\n", "\r\n") if crlf else changed
    open(path, "wb").write((b"\xef\xbb\xbf" if bom else b"") + out.encode("utf-8"))
    say(f"  edit: {count} occurrence(s) of the rule replaced")
    try:
        code, text_b, secs = run("dotnet build Alveara.Api.Tests --nologo -v q", 600)
        say(f"  build: exit {code} ({secs:.0f}s)")
        if code != 0:
            say("  " + "\n  ".join(l for l in text_b.splitlines() if " error " in l)[:2000]); results.append((name, "BUILD FAILED")); continue
        filt = "|".join(f"FullyQualifiedName~Tests.{t}" for t in tests.split("|"))
        code, text_t, secs = run(f'dotnet test Alveara.Api.Tests --nologo --no-build --filter "{filt}"', 600)
        summary = [l.strip() for l in text_t.splitlines() if re.search(r"(Passed!|Failed!)\s+-", l)]
        failed = [l.strip() for l in text_t.splitlines() if re.match(r"\s+Failed Alveara", l)]
        say(f"  tests: {summary[-1] if summary else 'no summary'} ({secs:.0f}s)")
        for f in failed: say(f"    caught by: {f}")
        msgs = [l.strip() for l in text_t.splitlines() if "Error Message" in l or "Assert." in l or "Exception" in l][:6]
        for m in msgs: say(f"    {m[:220]}")
        results.append((name, "CAUGHT" if failed else "SURVIVED"))
    finally:
        open(path, "wb").write(raw)
        code, d, _ = run(f'git diff --exit-code -- "{path}"', 60)
        say(f"  restore: {'byte-identical (git diff clean)' if code == 0 else 'NOT CLEAN - STOP'}")
        if code != 0: say(d); sys.exit(2)

code, text_b, secs = run("dotnet build Alveara.Api.Tests --nologo -v q", 600)
say(""); say(f"final rebuild of the restored tree: exit {code} ({secs:.0f}s)")
say(""); say("SUMMARY")
for n, r in results: say(f"  {r:10}  {n}")
say(f"total {time.time() - started:.0f}s; all caught: {all(r == 'CAUGHT' for _, r in results)}")
