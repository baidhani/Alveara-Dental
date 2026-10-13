import re, subprocess, sys, time

ROOT = "c:/Users/firas/Documents/AI-Project-[Alveara-Dental]/src/"
API = ROOT + "Alveara.Api/"
TESTS = ROOT + "Alveara.Api.Tests"
LOG = sys.argv[1]
ONLY = set(sys.argv[2:])

C = "Architecture/Completion/"
RC = C + "CompletedProcedureService.Reconciliation.cs"
CH = "Architecture/Odontogram/OdontogramService.CompletionSupport.cs"
AD = C + "CompletedProcedureService.Adopt.cs"
CS = C + "CompletedProcedureService.cs"
MIG = "Migrations/20261013170209_AddCompletionChartVersion.cs"
EV = "CompletionChartEvidenceTests"
RT = "CompletionReconciliationTests"

MUTANTS = [
 ("B01", "reconcile: a version that does not show the finding Completed is no longer a mismatch", [(RC, "v.State != FindingStates.Completed || ", "")], EV + "," + RT),
 ("B02", "reconcile: a version that shows the finding withdrawn is no longer a mismatch", [(RC, "v.Status != FindingStatuses.Active || ", "")], EV + "," + RT),
 ("B03", "reconcile: a version newer than the completion is no longer a mismatch", [(RC, " || v.OccurredAtUtc > c.At", "")], EV + "," + RT),
 ("B04", "reconcile: judge the finding's LATEST version instead of the recorded one (the reviewed defect)", [(RC, "versions.TryGetValue((c.FindingId, number), out var v)", "versions.TryGetValue((c.FindingId, versions.Keys.Where(k => k.Item1 == c.FindingId).Max(k => k.Item2)), out var v)")], EV + "," + RT),
 ("B05", "chart: read the finding's FIRST version instead of its latest", [(CH, "w.FindingId == v.FindingId).Max(w => w.VersionNumber)", "w.FindingId == v.FindingId).Min(w => w.VersionNumber)")], EV + "," + RT + ",CompletedProcedureAdoptionServiceTests"),
 ("B06", "chart: a version that is not an active Completed state is no longer refused when read", [(CH, "if (v.State != FindingStates.Completed || v.Status != FindingStatuses.Active)", "if (false)")], EV),
 ("B07", "adopt: the version is not recorded", [(AD, "row.ToothFindingVersionNumber = row.ToothFindingId is Guid finding ? versions[finding] : null;", "row.ToothFindingVersionNumber = null;")], EV + ",CompletedProcedureAdoptionServiceTests"),
 ("B08", "completion: the version is not recorded", [(CS, "row.ToothFindingVersionNumber = row.ToothFindingId is Guid finding ? versions[finding] : null;", "row.ToothFindingVersionNumber = null;")], EV + ",CompletedProcedureEffectsTests"),
 ("B09", "database: trigger 51173 no longer requires the version to show the finding Completed", [(MIG, "AND v.[State] = 'Completed' AND v.[Status] = 'Active' AND v.[OccurredAtUtc] <= i.[FinalizedAtUtc]", "AND v.[Status] = 'Active' AND v.[OccurredAtUtc] <= i.[FinalizedAtUtc]")], EV),
 ("B10", "database: trigger 51173 no longer requires the version to be no newer than the completion", [(MIG, " AND v.[OccurredAtUtc] <= i.[FinalizedAtUtc]", "")], EV),
 ("B11", "migration backfill: take the LATEST active Completed version instead of the first", [(MIG, "SELECT MIN(v.[VersionNumber])", "SELECT MAX(v.[VersionNumber])")], EV),
 ("B12", "migration backfill: no longer bounded by the completion's instant", [(MIG, " AND v.[OccurredAtUtc] <= c.[FinalizedAtUtc])", ")")], EV),
]


def run_filter(flt):
    start = time.time()
    expr = "|".join("FullyQualifiedName~" + x for x in flt.split(","))
    p = subprocess.run(["dotnet", "test", "--nologo", "--filter", expr], cwd=TESTS, capture_output=True, text=True, timeout=1500)
    out = p.stdout + p.stderr
    failed = re.findall(r"^\s+Failed (\S+)", out, re.M) or re.findall(r"\] (\S+) \[FAIL\]", out)
    summary = re.findall(r"(?:Passed|Failed)!.*", out)
    build_error = "error CS" in out or "Build FAILED" in out
    return p.returncode, failed, summary[-1] if summary else "", build_error, time.time() - start


with open(LOG, "a", encoding="utf-8") as log:
    for mid, desc, edits, flt in MUTANTS:
        if ONLY and mid not in ONLY:
            continue
        backups = {}
        try:
            for path, old, new in edits:
                full = API + path
                if full not in backups:
                    backups[full] = open(full, "rb").read()
                text = open(full, "rb").read().decode("utf-8")
                if text.count(old) != 1:
                    raise RuntimeError(f"{mid}: expected exactly one occurrence of the text in {path}, found {text.count(old)}")
                open(full, "wb").write(text.replace(old, new).encode("utf-8"))
            code, failed, summary, build_error, secs = run_filter(flt)
            status = "BUILD-ERROR" if build_error else ("KILLED" if code != 0 else "SURVIVED")
            log.write(f"{mid} | {status} | {desc} | filter={flt} | {summary} | failing: {'; '.join(failed[:4])} | {secs:.0f}s\n")
        except Exception as e:
            log.write(f"{mid} | ERROR | {desc} | {e}\n")
        finally:
            for full, raw in backups.items():
                open(full, "wb").write(raw)
        log.flush()
    log.write("DONE\n")
