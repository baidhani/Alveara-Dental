"""ALV-N020 NC-5: AppShell no longer leaves the page when a different person is found (the two-tab fix removed). The two-tab test must FAIL; then the file is
restored byte for byte and the same test file must PASS. The pattern is checked BEFORE anything runs (pass --check to only check)."""
import os, re, subprocess, sys

R = r"C:\Users\firas\Documents\AI-Project-[Alveara-Dental]"
C = os.path.join(R, "src", "alveara-client")
OUT = r"C:\tmp\n020-proof\g12"
F = os.path.join(C, "src", "app", "AppShell.tsx")
ORIG = open(F, "rb").read()
TEXT = ORIG.decode("utf-8")
OLD = 'if (state.kind === "signed-in") navigate("/", { replace: true });'
assert TEXT.count(OLD) == 1, f"NC-5 pattern matches {TEXT.count(OLD)} times"
if "--check" in sys.argv:
    print("NC-5 pattern matches once; nothing run")
    sys.exit(0)


def vitest(name):
    r = subprocess.run("npx vitest run src/App.routeGuards.test.tsx", cwd=C, shell=True, capture_output=True, text=True, encoding="utf-8", errors="replace")
    open(os.path.join(OUT, name + ".txt"), "w", encoding="utf-8").write(r.stdout + r.stderr)
    return r.returncode, [l.strip() for l in r.stdout.splitlines() if re.search(r"Tests\s+\d|two tabs", l)]


os.makedirs(OUT, exist_ok=True)
results = {}
try:
    open(F, "wb").write(TEXT.replace(OLD, "void navigate;").encode("utf-8"))
    results["NC-5 two-tab navigation removed (expect FAIL)"] = vitest("nc5-removed")
finally:
    open(F, "wb").write(ORIG)
    results["restored (expect pass)"] = vitest("nc5-restored")

lines = [f"{k} -> exit {v[0]} {v[1]}" for k, v in results.items()]
lines.append("file restored byte for byte: " + str(open(F, "rb").read() == ORIG))
open(os.path.join(OUT, "NC5_SUMMARY.txt"), "w", encoding="utf-8").write("\n".join(lines) + "\n")
print("\n".join(lines))
