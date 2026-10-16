import subprocess, shutil, json, sys, time

ROOT = "C:/Users/firas/Documents/AI-Project-[Alveara-Dental]/src/"
API = "Alveara.Api/"
CLI = "alveara-client/"
LOG = "C:/tmp/dl_mutation_results.json"
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

DL = "Architecture/Concurrency/DeadlockRetry.cs"
SVC = "Architecture/Completion/CompletedProcedureService.cs"
ADOPT = "Architecture/Completion/CompletedProcedureService.Adopt.cs"
DLS = "Architecture/Completion/CompletedProcedureService.Deadlock.cs"
RUN = back("FullyQualifiedName~CompletionDeadlockTests")

M = [
 ("D01 a deadlock wrapped by EF is not recognised", API + DL, "for (var e = exception; e is not null; e = e.InnerException)", "for (var e = exception; e is not null; e = null)", RUN),
 ("D02 a deadlock inside an AggregateException is not recognised", API + DL, "if (e is AggregateException aggregate && aggregate.InnerExceptions.Any(Is)) return true;", "if (false) return true;", RUN),
 ("D03 any SQL error is treated as a deadlock", API + DL, "if (e is SqlException sql && (sql.Number == SqlErrorNumber || sql.Errors.Cast<SqlError>().Any(x => x.Number == SqlErrorNumber))) return true;", "if (e is SqlException) return true;", RUN),
 ("D04 the policy never retries", API + DL, "if (attempt >= MaxAttempts) throw new DeadlockRetriesExhaustedException(attempt, ex);", "if (true) throw new DeadlockRetriesExhaustedException(attempt, ex);", RUN),
 ("D05 the policy makes one attempt too many", API + DL, "if (attempt >= MaxAttempts) throw new DeadlockRetriesExhaustedException(attempt, ex);", "if (attempt >= MaxAttempts + 1) throw new DeadlockRetriesExhaustedException(attempt, ex);", RUN),
 ("D06 the policy retries failures that are not deadlocks", API + DL, "catch (Exception ex) when (DeadlockVictim.Is(ex))", "catch (Exception ex) when (true)", RUN),
 ("D07 an exhausted retry is not turned into save_failed", API + DLS, 'throw new TreatmentPlanException("save_failed", $"The {operation} could not be saved because the database was busy, so nothing was {outcome}. Try again; if it keeps failing, contact support.", 503, inner: ex.InnerException);', "throw;", RUN),
 ("D08 the change tracker is not cleared between attempts", API + DLS, "db.ChangeTracker.Clear();                                    // nothing of the failed attempt may be carried into the next one", "/* mutant */", RUN),
 ("D09 a completion is not retried", API + SVC, 'WithDeadlockRetryAsync("completion", "completed", () => CompleteOnceAsync(planId, request, actor, ct), ct);', "CompleteOnceAsync(planId, request, actor, ct);", RUN),
 ("D10 an adoption is not retried", API + ADOPT, 'WithDeadlockRetryAsync("adoption", "adopted", () => AdoptOnceAsync(planId, itemId, request, actor, ct), ct);', "AdoptOnceAsync(planId, itemId, request, actor, ct);", RUN),
 ("D11 a retry is not logged", API + DLS, '"completion_deadlock_retry operation=', '"completion_deadlock_retried operation=', RUN),
 ("D12 the pause does not grow with each failure", API + DL, "BaseDelayMilliseconds * failedAttempt", "BaseDelayMilliseconds", RUN),
 ("D13 the failure message names the deadlock", API + DLS, "because the database was busy, so nothing was", "because of a deadlock, so nothing was", RUN),
]
def run(cmd):
    t = time.time()
    p = subprocess.run(cmd, shell=True, cwd=(ROOT + CLI if cmd.startswith("npx") else None), capture_output=True, text=True, encoding="utf-8", errors="replace")
    return p.returncode, p.stdout + p.stderr, round(time.time() - t)


results = []
only = set(sys.argv[1:])
if not only:
    rc, out, secs = run(RUN)
    results.append({"id": "BASELINE deadlock tests", "rc": rc, "secs": secs})
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
