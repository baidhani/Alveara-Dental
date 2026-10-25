import subprocess, sys, re, os
ROOT = r"C:\Users\firas\Documents\AI-Project-[Alveara-Dental]"
API = os.path.join(ROOT, "src", "Alveara.Api")
TESTS = os.path.join(ROOT, "src", "Alveara.Api.Tests")
OUT = r"C:\tmp\alv009c02\negative-controls.txt"
RULES = r"Architecture\ExternalCases\ExternalCaseRules.cs"
SVC = r"Architecture\ExternalCases\ExternalCaseService.cs"
STA = r"Architecture\ExternalCases\ExternalCaseService.Status.cs"
DOC = r"Architecture\ExternalCases\ExternalCaseService.Documents.cs"
RD = r"Architecture\ExternalCases\ExternalCaseService.Reads.cs"
PTY = r"Architecture\ExternalCases\ExternalPartyService.cs"
JOB = r"Architecture\ExternalCases\ExternalCaseTaskJobs.cs"
CTL = r"Controllers\ExternalCasesController.cs"
F = "FullyQualifiedName~ExternalCaseServiceTests|FullyQualifiedName~ExternalCaseTaskJobTests|FullyQualifiedName~ExternalCaseApiTests|FullyQualifiedName~ExternalCaseRulesTests|FullyQualifiedName~ExternalCaseControllerPermissionTests"
NOAUDIT = '"NoSuchEvent_ControlOnly"'

MUTATIONS = [
    ("M1 a cancellation needs no note", RULES, "public static bool NoteRequired(string kind, string from, string to) => to == Cancelled || (kind == ExternalCaseKinds.Lab && from == Received && to == Sent);",
     "public static bool NoteRequired(string kind, string from, string to) => (kind == ExternalCaseKinds.Lab && from == Received && to == Sent);"),
    ("M2 a lab remake needs no note", RULES, "public static bool NoteRequired(string kind, string from, string to) => to == Cancelled || (kind == ExternalCaseKinds.Lab && from == Received && to == Sent);",
     "public static bool NoteRequired(string kind, string from, string to) => to == Cancelled;"),
    ("M3 a case due today is already overdue", RULES, "Tracks(status) && dueOn is { } due && due < todayLocal;", "Tracks(status) && dueOn is { } due && due <= todayLocal;"),
    ("M4 the task waits until the day after the due date", RULES, "Tracks(status) && dueOn is { } due && due <= todayLocal;", "Tracks(status) && dueOn is { } due && due < todayLocal;"),
    ("M5 a draft is tracked against its due date", RULES, "public static bool Tracks(string status) => status is Sent or InProgress or Received;", "public static bool Tracks(string status) => status is Draft or Sent or InProgress or Received;"),
    ("M6 a different request under a used key is replayed", SVC, ": throw new ExternalCaseException(\"key_reused\", \"That Idempotency-Key was already used for a different request.\", 409);", ": new ExternalCaseResult(await ViewAsync(existing, ct), true);"),
    ("M7 a referral may go to a lab vendor", SVC, "        if (party.Kind != partyKind) throw new ExternalCaseException(\"party_kind_invalid\", partyKind == ExternalPartyKinds.LabVendor ? \"Choose a laboratory.\" : \"Choose a referral provider.\", 400);\n", ""),
    ("M8 an inactive party can receive a new case", SVC, "        if (!party.IsActive) throw new ExternalCaseException(\"party_inactive\", \"That outside party is no longer active. Choose another or ask for it to be made active.\", 409);\n", ""),
    ("M9 a status move ignores the row version", STA, "        db.Entry(c).Property(x => x.RowVersion).OriginalValue = version;\n        switch (to)", "        switch (to)"),
    ("M10 a referral edit ignores the row version", SVC, "        db.Entry(c).Property(x => x.RowVersion).OriginalValue = version;\n        c.Reason = reason;", "        c.Reason = reason;"),
    ("M11 a lab edit ignores the row version", SVC, "        db.Entry(c).Property(x => x.RowVersion).OriginalValue = version;\n        c.Title = title;", "        c.Title = title;"),
    ("M12 a sent case may change its party", SVC, "        if (c.Status != ExternalCaseRules.Draft) throw new ExternalCaseException(\"party_locked\", \"The case has been sent, so it keeps its outside party.\", 409);\n", ""),
    ("M13 a finished case may be changed", SVC, "        if (ExternalCaseRules.IsFinal(c.Status)) throw new ExternalCaseException(\"case_closed\", $\"That case is already {c.Status.ToLowerInvariant()} and cannot be changed.\", 409);", ""),
    ("M14 any status move is allowed", STA, "        if (!ExternalCaseRules.CanMove(kind, from, to))", "        if (false)"),
    ("M15 it may have come back before it was sent", STA, "if (c.SentOn is { } sent && date < sent) throw new ExternalCaseException(\"date_before_sent\", \"It cannot have come back before it was sent.\", 400);", ""),
    ("M16 a date in the future is accepted", STA, "if ((to == ExternalCaseRules.Sent || to == ExternalCaseRules.Received) && !ExternalCaseRules.EventDateValid(date, today))", "if (false)"),
    ("M17 a status move is not audited", STA, "kind == ExternalCaseKinds.Lab ? AuditEventTypes.LabCaseStatusChanged : AuditEventTypes.ReferralCaseStatusChanged", NOAUDIT),
    ("M18 a status move leaves no history entry", STA, "        AddEvent(kind, c.Id, ExternalCaseEventKinds.StatusChanged, actor, now, from, to, note: note);\n", ""),
    ("M19 a remake keeps the old received date", STA, "c.SentOn = date; c.ReceivedOn = null;", "c.SentOn = date;"),
    ("M20 a document of another patient can be linked", DOC, "d.Id == documentId && d.PatientId == c.PatientId", "d.Id == documentId"),
    ("M21 an inactive document can be linked", DOC, "        if (doc.Status != DocumentStatuses.Active) throw new ExternalCaseException(\"document_inactive\", \"That document is inactive. Make it active in the document library first.\", 409);\n", ""),
    ("M22 linking is not audited", DOC, "AuditService.Record(db, AuditEventTypes.CaseDocumentLinked,", "AuditService.Record(db, " + NOAUDIT + ","),
    ("M23 an inactive document is not flagged as broken", DOC, "var reason = x.Status != DocumentStatuses.Active ?", "var reason = false ?"),
    ("M24 the overdue filter is ignored", RD, "        if (q.Overdue == true) filtered = filtered.Where(r => tracked.Contains(r.Status) && r.DueOn != null && r.DueOn < today);\n", ""),
    ("M25 the same party name with other contact details is replayed", PTY, "existing.ContactDetails == contact ? View(existing) : throw new ExternalCaseException(\"party_exists\", \"A party of this kind with that name already exists.\", 409);", "View(existing);"),
    ("M26 a party edit ignores the row version", PTY, "        db.Entry(party).Property(p => p.RowVersion).OriginalValue = version;\n        party.Name = name;", "        party.Name = name;"),
    ("M27 a draft gets a chase task", JOB, "var tracked = new[] { ExternalCaseRules.Sent, ExternalCaseRules.InProgress, ExternalCaseRules.Received };", "var tracked = new[] { ExternalCaseRules.Draft, ExternalCaseRules.Sent, ExternalCaseRules.InProgress, ExternalCaseRules.Received };"),
    ("M28 the create handler ignores an existing task", JOB, "        if (await db.FollowUpTasks.AsNoTracking().AnyAsync(t => t.SourceKey == key, ct)) return;      // already made (an earlier attempt committed it)\n", ""),
    ("M29 the create handler does not look at the case again", JOB, "        if (!await ExternalCaseTaskRules.StillWantedAsync(db, key, lab ? null : payload.CaseId, lab ? payload.CaseId : null, today, ct)) return;\n", ""),
    ("M30 the close handler closes a task its case still wants", JOB, "        if (await ExternalCaseTaskRules.StillWantedAsync(db, task.SourceKey!, task.SourceReferralCaseId, task.SourceLabCaseId, today, ct)) return;\n", ""),
    ("M31 an overdue case's task is not raised in priority", JOB, "var priority = dueOn < today ? FollowUpPriorities.High : FollowUpPriorities.Normal;", "var priority = FollowUpPriorities.Normal;"),
    ("M32 the documents route does not need the document permission", CTL, "    public async Task<IActionResult> Documents(string route, Guid id, CancellationToken ct) =>\n        await NeedsAsync(Permission.ViewDocuments, ct) ?? await Run(", "    public async Task<IActionResult> Documents(string route, Guid id, CancellationToken ct) =>\n        await Run("),
    ("M33 linking a document does not need the document permission", CTL, "    public async Task<IActionResult> LinkDocument(string route, Guid id, Guid documentId, CancellationToken ct) =>\n        await NeedsAsync(Permission.ViewDocuments, ct) ?? await Run(", "    public async Task<IActionResult> LinkDocument(string route, Guid id, Guid documentId, CancellationToken ct) =>\n        await Run("),
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
        open(OUT, "a", encoding="utf-8").write(lines[-1] + "\n")
        continue
    mutated = text.replace(old, new)
    if "\r\n" in raw:
        mutated = mutated.replace("\n", "\r\n")
    open(path, "wb").write(mutated.encode("utf-8"))
    try:
        b = run("dotnet build Alveara.Api.Tests.csproj -v q", TESTS)
        if "Build succeeded" not in b.stdout:
            lines.append(f"{name}: BUILD FAILED " + " | ".join(l.strip()[:160] for l in b.stdout.splitlines() if "error" in l)[:400])
            open(OUT, "a", encoding="utf-8").write(lines[-1] + "\n")
            continue
        t = run(f'dotnet test --no-build --filter "{F}"', TESTS)
        m = re.search(r"(Passed!|Failed!)\s+-\s+Failed:\s+(\d+), Passed:\s+(\d+)", t.stdout)
        failed = [l.strip() for l in t.stdout.splitlines() if l.strip().startswith("Failed ")]
        entry = f"{name}: {'KILLED' if m and int(m.group(2)) > 0 else 'SURVIVED'} ({m.group(0) if m else 'no summary'})"
        lines.append(entry)
        extra = ["    " + f[:170] for f in failed[:3]]
        lines.extend(extra)
        open(OUT, "a", encoding="utf-8").write(entry + "\n" + "".join(e + "\n" for e in extra))
    finally:
        open(path, "wb").write(raw.encode("utf-8"))

d = run("git status --porcelain -- src/Alveara.Api src/Alveara.Api.Tests", ROOT)
final = "restored: the backend source is " + ("UNCHANGED by the controls" if not d.stdout.strip() else "CHANGED: " + d.stdout.strip())
lines.append(final)
open(OUT, "a", encoding="utf-8").write(final + "\n")
print("\n".join(lines))
