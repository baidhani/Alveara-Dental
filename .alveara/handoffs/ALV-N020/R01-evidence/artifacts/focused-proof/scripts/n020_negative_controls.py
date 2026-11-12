"""ALV-N020 group 6: negative controls. Each mutation undoes one release-blocking fix, runs only the tests that prove it, and is restored byte for byte.
Every mutation pattern is checked BEFORE any test runs; a pattern that does not match stops the script with nothing run (pass --check to only check)."""
import os, re, subprocess, sys

R = r"C:\Users\firas\Documents\AI-Project-[Alveara-Dental]"
C = os.path.join(R, "src", "alveara-client")
OUT = r"C:\tmp\n020-proof\g6"

FILES = {
    "outcome": os.path.join(C, "src", "pages", "loginOutcome.ts"),
    "patient": os.path.join(C, "src", "contexts", "PatientContext.tsx"),
    "auth": os.path.join(C, "src", "contexts", "AuthContext.tsx"),
    "staff": os.path.join(R, "src", "Alveara.Api", "Architecture", "Configuration", "StaffInactivationService.cs"),
}
ORIG = {k: open(p, "rb").read() for k, p in FILES.items()}
NL = "\n"

MUTATIONS = {
    "NC-1": [
        ("outcome", 'return from && fromUser && fromUser === signedInUsername ? `${from.pathname}${from.search}` : "/";',
                    'return from ? `${from.pathname}${from.search}` : "/";'),
        ("patient", "    clearPatient();" + NL + "  }, [identityEpoch, clearPatient]);",
                    "  }, [identityEpoch, clearPatient]);"),
    ],
    "NC-2": [
        ("auth", "      setConnectionLost(true);" + NL + '      if (currentUserRef.current === null) setState({ kind: "signed-out" });',
                 "      setConnectionLost(true);" + NL + '      endSession("expired");'),
        ("outcome", '    if (isServerUnreachable(err)) return { kind: "server-unavailable" };' + NL + '    if (err.status >= 500) return { kind: "server-problem" };' + NL,
                    ""),
    ],
    "NC-3": [
        ("staff", "                loginDisabled = await accounts.DisableForStaffInactivationAsync(accountId, actor, profile.DisplayName, ct);",
                  "                loginDisabled = false;"),
    ],
}


def native(key, s):
    text = ORIG[key].decode("utf-8")
    return s.replace(NL, "\r\n") if "\r\n" in text else s


def check_patterns():
    for name, muts in MUTATIONS.items():
        for key, old, _ in muts:
            n = ORIG[key].decode("utf-8").count(native(key, old))
            assert n == 1, f"{name}: pattern in {key} matches {n} times: {old[:70]!r}"


def apply(name):
    texts = {}
    for key, old, new in MUTATIONS[name]:
        t = texts.get(key, ORIG[key].decode("utf-8"))
        texts[key] = t.replace(native(key, old), native(key, new))
    for key, t in texts.items():
        open(FILES[key], "wb").write(t.encode("utf-8"))


def restore():
    for k, p in FILES.items():
        open(p, "wb").write(ORIG[k])


def vitest(name, files):
    r = subprocess.run("npx vitest run " + " ".join(files), cwd=C, shell=True, capture_output=True, text=True, encoding="utf-8", errors="replace")
    open(os.path.join(OUT, name + ".txt"), "w", encoding="utf-8").write(r.stdout + r.stderr)
    return r.returncode, [l.strip() for l in r.stdout.splitlines() if re.search(r"Tests\s+\d", l)]


def xunit(name, flt):
    b = subprocess.run(["dotnet", "build", "Alveara.Api.Tests/Alveara.Api.Tests.csproj", "--nologo", "-v", "q"], cwd=os.path.join(R, "src"), capture_output=True, text=True)
    if b.returncode != 0:
        open(os.path.join(OUT, name + "-build.txt"), "w", encoding="utf-8").write(b.stdout)
        return b.returncode, ["BUILD FAILED"]
    r = subprocess.run(["dotnet", "test", "--no-build", "--nologo", "--filter", flt], cwd=os.path.join(R, "src", "Alveara.Api.Tests"), capture_output=True, text=True)
    open(os.path.join(OUT, name + ".txt"), "w", encoding="utf-8").write(r.stdout[-20000:])
    return r.returncode, [l.strip() for l in r.stdout.splitlines() if re.match(r"\s*(Passed|Failed)!", l)]


check_patterns()
if "--check" in sys.argv:
    print("all mutation patterns match; nothing run")
    sys.exit(0)

os.makedirs(OUT, exist_ok=True)
CLIENT_F037 = ["src/pages/loginOutcome.test.ts", "src/contexts/PatientContext.test.tsx", "src/App.routeGuards.test.tsx"]
CLIENT_F155 = ["src/contexts/AuthContext.test.tsx", "src/pages/loginOutcome.test.ts", "src/pages/LoginPage.test.tsx"]
SERVER_F021 = "FullyQualifiedName~Alveara.Api.Tests.StaffInactivationServiceTests"
results = {}
try:
    apply("NC-1"); results["NC-1 F-037 fix removed (expect FAIL)"] = vitest("nc1-f037", CLIENT_F037); restore()
    apply("NC-2"); results["NC-2 F-155/F-014 fix removed (expect FAIL)"] = vitest("nc2-f155-f014", CLIENT_F155); restore()
    apply("NC-3"); results["NC-3 F-021 fix removed (expect FAIL)"] = xunit("nc3-f021", SERVER_F021); restore()
finally:
    restore()
    results["restored client (expect pass)"] = vitest("restored-client", sorted(set(CLIENT_F037 + CLIENT_F155)))
    results["restored server (expect pass)"] = xunit("restored-server", SERVER_F021)

lines = [f"{k} -> exit {v[0]} {v[1]}" for k, v in results.items()]
lines.append("files restored byte for byte: " + str(all(open(p, "rb").read() == ORIG[k] for k, p in FILES.items())))
open(os.path.join(OUT, "SUMMARY.txt"), "w", encoding="utf-8").write("\n".join(lines) + "\n")
print("\n".join(lines))
