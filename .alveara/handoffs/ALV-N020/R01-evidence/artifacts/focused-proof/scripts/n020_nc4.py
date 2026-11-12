"""ALV-N020 NC-4 negative control: the Urgent alert is never sent (the record-then-alert call removed). The alert tests must FAIL; the file is then restored
byte for byte and the same tests must PASS. The pattern is checked BEFORE anything runs (pass --check to only check)."""
import os, re, subprocess, sys

R = r"C:\Users\firas\Documents\AI-Project-[Alveara-Dental]"
OUT = r"C:\tmp\n020-proof\g10"
F = os.path.join(R, "src", "Alveara.Api", "Architecture", "Configuration", "StaffInactivationService.cs")
ORIG = open(F, "rb").read()
TEXT = ORIG.decode("utf-8")
NL = "\r\n" if "\r\n" in TEXT else "\n"
OLD = ("            if (recordId is { } id)" + NL +
       "                await TryAlertAsync(new StaffInactivationFailure(staffProfileId, profile.DisplayName, profile.UserAccountId, actor, errorClass, correlation, id));" + NL)
FILTER = "FullyQualifiedName~Alveara.Api.Tests.StaffInactivationAlertTests|FullyQualifiedName~Alveara.Api.Tests.StaffInactivationServiceTests"

assert TEXT.count(OLD) == 1, f"NC-4 pattern matches {TEXT.count(OLD)} times"
if "--check" in sys.argv:
    print("NC-4 pattern matches once; nothing run")
    sys.exit(0)


def run(name):
    b = subprocess.run(["dotnet", "build", "Alveara.Api.Tests/Alveara.Api.Tests.csproj", "--nologo", "-v", "q"], cwd=os.path.join(R, "src"), capture_output=True, text=True)
    if b.returncode != 0:
        open(os.path.join(OUT, name + "-build.txt"), "w", encoding="utf-8").write(b.stdout)
        return b.returncode, ["BUILD FAILED"]
    r = subprocess.run(["dotnet", "test", "--no-build", "--nologo", "--filter", FILTER], cwd=os.path.join(R, "src", "Alveara.Api.Tests"), capture_output=True, text=True)
    open(os.path.join(OUT, name + ".txt"), "w", encoding="utf-8").write(r.stdout[-20000:])
    return r.returncode, [l.strip() for l in r.stdout.splitlines() if re.match(r"\s*(Passed|Failed)!", l) or re.match(r"\s*Failed Alveara", l)]


os.makedirs(OUT, exist_ok=True)
results = {}
try:
    open(F, "wb").write(TEXT.replace(OLD, "").encode("utf-8"))
    results["NC-4 alert never sent (expect FAIL)"] = run("nc4-alert-removed")
finally:
    open(F, "wb").write(ORIG)
    results["restored (expect pass)"] = run("nc4-restored")

lines = [f"{k} -> exit {v[0]} {v[1]}" for k, v in results.items()]
lines.append("file restored byte for byte: " + str(open(F, "rb").read() == ORIG))
open(os.path.join(OUT, "NC4_SUMMARY.txt"), "w", encoding="utf-8").write("\n".join(lines) + "\n")
print("\n".join(lines))
