"""ALV-N025 slice 7, run C: negative controls. Each control breaks ONE rule, rebuilds, runs only the test(s) that should catch it, restores the file byte-for-byte
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
    ("C1 MRN retry removed (collision is rethrown at once)", r"Alveara.Api\Data\AlveraDbContext.cs",
     "if (attempt >= PatientMrn.MaxAttempts) throw PatientMrn.Busy();", "throw;", "all",
     "MrnTests.Concurrent_registrations_never_share_a_number_over_twenty_rounds|MrnTests.Past_the_retry_cap_the_save_is_mrn_busy_and_leaves_no_patient_and_no_archive_row"),
    ("C2 guard 51298 disabled (the trigger lets an MRN change)", r"Alveara.Api\Migrations\20261120011518_AddPatientMrn.cs",
     "THROW 51298, 'A patient''s record number (MRN) never changes.', 1;", "RETURN;", "one",
     "MrnTests.A_number_never_changes_by_sql_or_through_ef"),
    ("C3 once-per-session check removed (every open writes)", r"Alveara.Api\Architecture\Auditing\PatientAccessRecorder.cs",
     "if (await db.PatientRecordOpens.AsNoTracking().AnyAsync(o => o.SessionId == sid && o.PatientId == patientId, ct)) return false;",
     "AuditService.Record(db, PatientAccessEvents.RecordOpened, nameof(Patient), patientId, userId, \"control\", timestampUtc: nowUtc); await db.SaveChangesAsync(ct); return true;", "one",
     "PatientAccessTrailTests.A_record_open_is_written_once_per_patient_per_session_and_never_for_searches_or_the_history"),
    ("C4 username fallback removed", r"Alveara.Api\Architecture\Clinical\ClinicalNames.cs",
     "map.TryAdd(u.Id, u.Username);", "_ = u;", "one",
     "AuditLogApiTests.A_person_is_named_by_staff_display_name_else_username_and_never_by_a_bare_id"),
    ("C5 different-birth-date rule removed", r"Alveara.Api\Architecture\Patients\Merge\PatientDuplicateScanner.cs",
     "public static IReadOnlyList<string> DifferentBirthDateReasons(PatientFacts a, PatientFacts b)\n    {",
     "public static IReadOnlyList<string> DifferentBirthDateReasons(PatientFacts a, PatientFacts b)\n    {\n        if (a.FirstName is not null) return Array.Empty<string>();", "one",
     "PatientDuplicateWarningTests.The_same_name_with_a_different_birth_date_and_a_shared_contact_detail_is_warned_never_refused"),
    ("C6 WHERE EndedAtUtc IS NULL removed from the expiry pass", r"Alveara.Api\Architecture\Auditing\AuditSessionStore.cs",
     ".Where(x => x.SessionId == s.SessionId && x.EndedAtUtc == null && x.ExpiresAtUtc <= nowUtc)", ".Where(x => x.SessionId == s.SessionId && x.ExpiresAtUtc <= nowUtc)", "one",
     "AuditSessionLifecycleTests.A_sign_out_racing_the_expiry_pass_leaves_exactly_one_end_event_over_twenty_rounds"),
    ("C7 permission check removed from the History route", r"Alveara.Api\Controllers\PatientsController.cs",
     "[HttpGet(\"{id:guid}/history\")]\n    [RequirePermission(Permission.ViewPatientRecords)]", "[HttpGet(\"{id:guid}/history\")]", "one",
     "AuditLogApiTests.An_audit_reader_without_patient_access_sees_the_same_phi_free_row_and_is_refused_the_history_itself"),
    ("C8 MA-01: the MRN guard given the merge stand-aside line", r"Alveara.Api\Migrations\20261120011518_AddPatientMrn.cs",
     "CREATE TRIGGER [TR_Patients_MrnImmutable] ON [Patients] AFTER UPDATE AS\nBEGIN\n    SET NOCOUNT ON;",
     "CREATE TRIGGER [TR_Patients_MrnImmutable] ON [Patients] AFTER UPDATE AS\nBEGIN\n    SET NOCOUNT ON;\n    IF CONVERT(int, SESSION_CONTEXT(N'alveara.patient_merge')) = 1 RETURN;", "one",
     "MrnTests.The_mrn_guard_holds_inside_a_merge_session|PatientMergeSchemaTests.Every_update_trigger_on_those_tables_stands_aside_for_a_merge_except_the_merge_scope_triggers_and_the_merged_mark"),
    ("C9 MA-01: the MRN guard's trigger order not set", r"Alveara.Api\Migrations\20261120011518_AddPatientMrn.cs",
     "EXEC sp_settriggerorder @triggername = N'TR_Patients_MrnImmutable', @order = N'First', @stmttype = N'UPDATE';", "SELECT 1;", "one",
     "MrnTests.The_mrn_guard_holds_inside_a_merge_session"),
    ("C10 U-017 stand-aside line not added", r"Alveara.Api\Migrations\20261120022648_AddMergeTriggerIntegrityRepairs.cs",
     "IF CHARINDEX(N'alveara.patient_merge', @def) = 0", "IF 1 = 0", "one",
     "MergeTriggerIntegrityTests.U017_reproduced_before_the_repair_and_after_it_a_patient_with_a_document_linked_clearance_merges_and_unmerges"),
    ("C11 U-016 term for Clearances.DocumentId not added", r"Alveara.Api\Migrations\20261120022648_AddMergeTriggerIntegrityRepairs.cs",
     "            (\"TR_Clearances_MergeScope\", \"DocumentId\"),\n", "", "one",
     "MergeTriggerIntegrityTests.U016_each_column_is_refused_inside_a_merge_only_after_the_repair_and_unchanged_outside_one|PatientMergeSchemaTests.Every_table_that_holds_a_patient_id_has_a_merge_scope_trigger_that_lists_every_other_column"),
]

started = time.time()
say(f"ALV-N025 negative controls - started {datetime.datetime.now().isoformat(timespec='seconds')}  (HEAD {run('git rev-parse --short HEAD', 30)[1].strip()})")
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
