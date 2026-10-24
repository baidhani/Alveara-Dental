import subprocess, sys, re, os
ROOT = r"C:\Users\firas\Documents\AI-Project-[Alveara-Dental]"
API = os.path.join(ROOT, "src", "Alveara.Api")
TESTS = os.path.join(ROOT, "src", "Alveara.Api.Tests")
OUT = r"C:\tmp\alv009c01\negative-controls.txt"
RJ = r"Architecture\Recall\RecallJobs.cs"
U = r"Architecture\Recall\UnscheduledTreatmentJobs.cs"
W = r"Architecture\FollowUps\FollowUpService.WorkQueue.cs"
A = r"Architecture\Recall\RecallAdminService.cs"
F = "FullyQualifiedName~RecallJobTests|FullyQualifiedName~UnscheduledTreatmentJobTests|FullyQualifiedName~WorkQueueApiTests|FullyQualifiedName~FollowUpReminderJobTests|FullyQualifiedName~FollowUpReminderTests"
RM = r"Architecture\FollowUps\FollowUpReminderService.cs"
NOAUDIT = 'AuditService.Record(db, "NoSuchEvent_ControlOnly",'

MUTATIONS = [
    ("K1 recall runs while the practice has it off", RJ, "if (!settings.Enabled) return new RecallPassResult(0, 0, 0);", ""),
    ("K2 opted-out patients are recalled", RJ, "                     && !db.PatientRecallPlans.Any(r => r.PatientId == x.PatientId && r.OptedOut)\n", ""),
    ("K3 patients with an open recall get another", RJ, "                     && !db.FollowUpTasks.Any(t => t.PatientId == x.PatientId && t.Kind == FollowUpKinds.Recall && t.Status == FollowUpStatuses.Open)\n", ""),
    ("K4 patients with a future appointment are recalled", RJ, "\n                     && !db.Appointments.Any(a => a.PatientId == x.PatientId && a.Status == AppointmentStatuses.Scheduled && a.StartUtc > now))", ")"),
    ("K5 the handler does not look again (task made though recall was switched off)", RJ, "        if (!settings.Enabled) return;\n", ""),
    ("K6 the handler ignores an existing task for the key (retry duplicates)", RJ, "        if (await db.FollowUpTasks.AsNoTracking().AnyAsync(t => t.SourceKey == key, ct)) return;      // already made (an earlier attempt committed it)\n", ""),
    ("K7 the recall job key is new every time", RJ, "await queue.EnqueueAsync(key, JobType,", 'await queue.EnqueueAsync(key + Guid.NewGuid().ToString("N"), JobType,'),
    ("K8 unscheduled: withdrawn items still get tasks", U, "where i.WithdrawnAtUtc == null && p.Status == TreatmentPlanStatuses.Proposed", "where p.Status == TreatmentPlanStatuses.Proposed"),
    ("K9 unscheduled: scheduled items still get tasks", U, "s.Scheduling == ItemScheduling.Unscheduled && s.Completion", "s.Completion"),
    ("K10 unscheduled: handler ignores an existing task for the key", U, "        if (await db.FollowUpTasks.AsNoTracking().AnyAsync(t => t.SourceKey == key, ct)) return;\n        var judgement", "        var judgement"),
    ("K11 unscheduled: the close handler closes tasks whose treatment still waits", U, "        if (judgement is { Waiting: true } && FollowUpRules.UnscheduledSourceKey(itemId, judgement.Sequence) == task.SourceKey) return;\n", ""),
    ("K12 queue: assign ignores the row version", W, "        db.Entry(task).Property(t => t.RowVersion).OriginalValue = version;\n        task.AssigneeUserId = newOwner;", "        task.AssigneeUserId = newOwner;"),
    ("K13 queue: priority change ignores the row version", W, "        db.Entry(task).Property(t => t.RowVersion).OriginalValue = version;\n        task.Priority", "        task.Priority"),
    ("K14 queue: assign is not audited", W, "AuditService.Record(db, AuditEventTypes.FollowUpAssigned,", NOAUDIT),
    ("K15 queue: overdue filter includes tasks not yet due", W, "QueueFilters.Overdue => query.Where(t => t.Status == FollowUpStatuses.Open && t.DueAtUtc < now)", "QueueFilters.Overdue => query.Where(t => t.Status == FollowUpStatuses.Open)"),
    ("K16 queue: a closed task can be reassigned", W, 'if (task.Status != FollowUpStatuses.Open) throw new FollowUpException("task_closed", "That follow-up is already closed and cannot be changed.", 409);\n        return task;', "return task;"),
    ("K17 settings: a change ignores the row version", A, "        db.Entry(s).Property(x => x.RowVersion).OriginalValue = version;\n", ""),
    ("K18 settings: a change is not audited", A, "AuditService.Record(db, AuditEventTypes.RecallSettingsChanged,", NOAUDIT),
    ("R1 reminder: a task already reminded is enqueued again", RM, "                    if (await db.BackgroundJobs.AsNoTracking().AnyAsync(j => j.IdempotencyKey == key, ct)) continue;", ""),
    ("R2 reminder: closed tasks are reminded", RM, "t.Status == FollowUpStatuses.Open && t.DueAtUtc <= now && !db.FollowUpReminderDeliveries", "t.DueAtUtc <= now && !db.FollowUpReminderDeliveries"),
]


def run(cmd, cwd):
    return subprocess.run(cmd, cwd=cwd, capture_output=True, text=True, shell=True)


only = sys.argv[1:]
lines = []
os.makedirs(os.path.dirname(OUT), exist_ok=True)
for name, rel, old, new in MUTATIONS:
    if only and not any(name.startswith(o + " ") for o in only):
        continue
    path = os.path.join(API, rel)
    raw = open(path, "rb").read().decode("utf-8")
    text = raw.replace("\r\n", "\n")
    if text.count(old) != 1:
        lines.append(f"{name}: SKIPPED (pattern found {text.count(old)} times)")
        open(OUT, "w", encoding="utf-8").write("\n".join(lines) + "\n")
        continue
    mutated = text.replace(old, new)
    if "\r\n" in raw:
        mutated = mutated.replace("\n", "\r\n")
    open(path, "wb").write(mutated.encode("utf-8"))
    try:
        b = run("dotnet build Alveara.Api.Tests.csproj -v q", TESTS)
        if "Build succeeded" not in b.stdout:
            lines.append(f"{name}: BUILD FAILED " + " | ".join(l.strip()[:160] for l in b.stdout.splitlines() if "error" in l)[:400])
            continue
        t = run(f'dotnet test --no-build --filter "{F}"', TESTS)
        m = re.search(r"(Passed!|Failed!)\s+-\s+Failed:\s+(\d+), Passed:\s+(\d+)", t.stdout)
        failed = [l.strip() for l in t.stdout.splitlines() if l.strip().startswith("Failed ")]
        lines.append(f"{name}: {'KILLED' if m and int(m.group(2)) > 0 else 'SURVIVED'} ({m.group(0) if m else 'no summary'})")
        lines.extend("    " + f[:170] for f in failed[:3])
    finally:
        open(path, "wb").write(raw.encode("utf-8"))
    open(OUT, "w", encoding="utf-8").write("\n".join(lines) + "\n")

d = run("git status --porcelain -- src/Alveara.Api src/Alveara.Api.Tests", ROOT)
lines.append("restored: the backend source is " + ("UNCHANGED by the controls" if not d.stdout.strip() else "CHANGED: " + d.stdout.strip()))
open(OUT, "w", encoding="utf-8").write("\n".join(lines) + "\n")
print("\n".join(lines))
