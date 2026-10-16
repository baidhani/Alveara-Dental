import subprocess, shutil, json, sys, time

ROOT = "C:/Users/firas/Documents/AI-Project-[Alveara-Dental]/src/"
API = "Alveara.Api/"
CLI = "alveara-client/"
LOG = "C:/tmp/wf_mutation_results.json"
P = "Architecture/Prescribing/"

def back(f):
    return 'dotnet test "%sAlveara.Api.Tests" --filter "%s" --nologo -v q' % (ROOT, f)

SVC = back("FullyQualifiedName~PrescriptionWorkflowServiceTests|FullyQualifiedName~PrescriptionWorkflowApiTests")
API_ONLY = back("FullyQualifiedName~PrescriptionWorkflowApiTests")
SCHEMA = back("FullyQualifiedName~PrescriptionWorkflowSchemaTests")
RULES = back("FullyQualifiedName~PrescriptionWorkflowRulesTests|FullyQualifiedName~PrescriptionWorkflowServiceTests")
FRONT = 'npx vitest run src/pages/prescription src/App.patientWorkspaceRoutes.test.tsx'

DRAFT = API + P + "PrescriptionDraftService.cs"
FIN = API + P + "PrescriptionFinalizeService.cs"
LIFE = API + P + "PrescriptionLifecycleService.cs"
CTRL = API + "Controllers/PrescriptionWorkflowController.cs"
MIG = None  # resolved below
import glob
import os
MIG = API + "Migrations/" + [f for f in os.listdir(ROOT + API + "Migrations") if f.endswith("AddPrescriptionWorkflow.cs")][0]
WF = CLI + "src/pages/prescription/workflow/"

M = [
 ("W01 finalize ignores a changed allergy or medication list", FIN, "if (read.Fingerprint != d.SafetyFingerprint) throw", "if (false && read.Fingerprint != d.SafetyFingerprint) throw", SVC),
 ("W02 an acknowledgement is accepted for lists that were not on screen", DRAFT, "if (!string.Equals(seenFingerprint, read.Fingerprint, StringComparison.Ordinal)) throw SafetyChanged(d, read);", "if (false) throw SafetyChanged(d, read);", SVC),
 ("W03 the draft never reports that the lists changed", DRAFT, "new SafetyCheck(read.Fingerprint, d.SafetyFingerprint != read.Fingerprint,", "new SafetyCheck(read.Fingerprint, false,", SVC),
 ("W04 finalize does not require instructions, quantity and refills", FIN, "if (check.Problems.Count > 0) throw PrescriptionDraftService.Invalid(check.Problems);", "if (false) throw PrescriptionDraftService.Invalid(check.Problems);", SVC),
 ("W05 finalize does not require the prescribers review of an allergy match", FIN, "if (PrescriptionRules.ReviewProblem(assessment, v.ReviewNote) is { } needsReview)", "if (false && PrescriptionRules.ReviewProblem(assessment, v.ReviewNote) is { } needsReview)", SVC),
 ("W06 an unreadable safety context counts as no allergies (fails open)", API + P + "PrescribingSafetyReader.cs", 'throw new PrescriptionException("allergy_check_unavailable",', 'return new SafetyRead(new List<RecordedAllergy>(), "Reviewed", "Reviewed", new List<string>(), "x"); throw new PrescriptionException("allergy_check_unavailable",', SVC),
 ("W07 an amendment does not mark the original superseded", FIN, "Stage(replaced, PrescriptionEventTypes.Superseded, null, row.Id, actor, now, correlation);", "", SVC),
 ("W08 an amendment can replace a prescription that already ended", FIN, 'if (!PrescriptionStatuses.CanEnd(status)) throw new PrescriptionException("prescription_not_active", $"The prescription this amends', 'if (false) throw new PrescriptionException("prescription_not_active", $"The prescription this amends', SVC),
 ("W09 cancelling needs no reason", LIFE, "if (problem is not null) throw new PrescriptionException(\"validation_failed\", \"A reason is needed.", "if (false) throw new PrescriptionException(\"validation_failed\", \"A reason is needed.", SVC),
 ("W10 an ended prescription can be ended again", LIFE, "if (!PrescriptionStatuses.CanEnd(current.Status)) throw", "if (false) throw", SVC),
 ("W11 a print is not logged", LIFE, 'AuditService.Record(db, "PrescriptionPrinted"', 'if (false) AuditService.Record(db, "PrescriptionPrinted"', SVC),
 ("W12 a prescription saved before the workflow can be printed", LIFE, "if (!r.HasDetails || r.Instructions is null || r.QuantityValue is null || r.QuantityUnit is null || r.Refills is null)", "if (false)", SVC),
 ("W13 a finalized draft can be discarded", DRAFT, "if (d.Status == DraftStatuses.Discarded) return (await ViewsAsync([d], ct))[0];\n        if (d.Status != DraftStatuses.Draft) throw Closed(d);", "if (d.Status == DraftStatuses.Discarded) return (await ViewsAsync([d], ct))[0];", SVC),
 ("W14 discarding needs no CSRF token", CTRL, '    [HttpPost("api/prescription-drafts/{draftId:guid}/discard")]\n    [RequirePermission(Permission.ManagePrescriptions)]\n    [RequireCsrfToken]\n', '    [HttpPost("api/prescription-drafts/{draftId:guid}/discard")]\n    [RequirePermission(Permission.ManagePrescriptions)]\n', API_ONLY),
 ("W15 discontinuing needs no CSRF token", CTRL, '    [HttpPost("api/prescriptions/{prescriptionId:guid}/discontinue")]\n    [RequirePermission(Permission.ManagePrescriptions)]\n    [RequireCsrfToken]\n', '    [HttpPost("api/prescriptions/{prescriptionId:guid}/discontinue")]\n    [RequirePermission(Permission.ManagePrescriptions)]\n', API_ONLY),
 ("W16 acknowledging the lists needs no CSRF token", CTRL, '    [HttpPost("api/prescription-drafts/{draftId:guid}/acknowledge-safety")]\n    [RequirePermission(Permission.ManagePrescriptions)]\n    [RequireCsrfToken]\n', '    [HttpPost("api/prescription-drafts/{draftId:guid}/acknowledge-safety")]\n    [RequirePermission(Permission.ManagePrescriptions)]\n', API_ONLY),
 ("W17 the details trigger does nothing", MIG, "THROW 51178, 'The details of a finalized prescription are never changed or deleted.', 1;", "PRINT 'mutant';", SCHEMA),
 ("W18 the events trigger does nothing", MIG, "THROW 51180, 'The events of a prescription are append-only.', 1;", "PRINT 'mutant';", SCHEMA),
 ("W19 a finalized draft can change", MIG, "THROW 51176, 'A finalized or discarded prescription draft never changes.', 1;", "PRINT 'mutant';", SCHEMA),
 ("W20 a draft can be deleted", MIG, "THROW 51175, 'A prescription draft is never deleted; an unwanted draft is discarded.', 1;", "PRINT 'mutant';", SCHEMA),
 ("W21 a supersession can cross patients", MIG, "THROW 51181, 'A prescription can only be replaced by a prescription of the same patient.', 1;", "PRINT 'mutant';", SCHEMA),
 ("W22 the migration rolls back over recorded drafts", MIG, "THROW 51178, 'Prescription workflow records exist, so this migration cannot be rolled back.', 1;", "PRINT 'mutant';", SCHEMA),
 ("W23 a quantity ceiling never applies", API + P + "PrescriptionWorkflowRules.cs", "else if (q > QuantityTypingCeiling)", "else if (q > decimal.MaxValue)", RULES),
 ("W24 refills have no ceiling", API + P + "PrescriptionWorkflowRules.cs", "else if (r > RefillsMax)", "else if (r > int.MaxValue - 1)", RULES),
 ("F01 finalizing opens the confirmation despite problems", WF + "DraftComposer.tsx", "if (c.problems.length > 0) {", "if (false && c.problems.length > 0) {", FRONT),
 ("F02 a changed list does not stop the finalization", WF + "DraftComposer.tsx", "if (c.safety.changedSinceAcknowledged) {", "if (false && c.safety.changedSinceAcknowledged) {", FRONT),
 ("F03 a print failure is not shown", WF + "PrintPreview.tsx", "{error && <p role=\"alert\"", "{false && <p role=\"alert\"", FRONT),
 ("F04 a prescription without details can be printed", WF + "PrescriptionRecordList.tsx", "{p.hasDetails && <button", "{true && <button", FRONT),
 ("F05 an ended prescription offers actions", WF + "PrescriptionRecordList.tsx", '{canPrescribe && p.status === "Active" && (', "{canPrescribe && (", FRONT),
 ("F06 a box that is not a number is sent", WF + "DraftComposer.tsx", "if (local.length > 0) {", "if (false && local.length > 0) {", FRONT),
 ("F07 the not-configured banner says something else", WF + "PrescriptionWorkflowPanel.tsx", "<strong>Not configured:</strong> {options.decisionSupportMessage}", "<strong>Ready.</strong>", FRONT),
]


def run(cmd):
    t = time.time()
    p = subprocess.run(cmd, shell=True, cwd=(ROOT + CLI if cmd.startswith("npx") else None), capture_output=True, text=True, encoding="utf-8", errors="replace")
    return p.returncode, p.stdout + p.stderr, round(time.time() - t)


results = []
only = set(sys.argv[1:])
if not only:
    rc, out, secs = run(FRONT)
    results.append({"id": "BASELINE frontend", "rc": rc, "secs": secs})
    if rc:
        json.dump(results, open(LOG, "w"), indent=1)
        sys.exit("baseline is not green")

for name, rel, old, new, cmd in M:
    if only and name.split()[0] not in only:
        continue
    path = ROOT + rel
    raw = open(path, encoding="utf-8-sig", newline="").read()
    bom = open(path, "rb").read(3) == b"\xef\xbb\xbf"
    nl = "\r\n" if "\r\n" in raw else "\n"
    text = raw.replace("\r\n", "\n")
    if text.count(old) != 1:
        results.append({"id": name, "verdict": "NO-MATCH", "count": text.count(old)})
        json.dump(results, open(LOG, "w"), indent=1)
        continue
    backup = path + ".mutbak"
    shutil.copyfile(path, backup)
    try:
        open(path, "w", encoding="utf-8-sig" if bom else "utf-8", newline="").write(text.replace(old, new).replace("\n", nl))
        rc, out, secs = run(cmd)
    finally:
        shutil.move(backup, path)
    verdict = "INVALID (did not compile)" if " error CS" in out else ("SURVIVED" if rc == 0 else "KILLED")
    failed = [l.strip()[:160] for l in out.splitlines() if "[FAIL]" in l or l.strip().startswith(("FAIL ", "\u00d7", "×"))][:3]
    results.append({"id": name, "verdict": verdict, "secs": secs, "first_failures": failed})
    json.dump(results, open(LOG, "w"), indent=1)

json.dump(results, open(LOG, "w"), indent=1)
print(json.dumps([(r["id"], r.get("verdict", r.get("rc"))) for r in results], indent=1))
