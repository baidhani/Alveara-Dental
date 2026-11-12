"""ALV-N020-C01 negative controls. Each control removes ONE protection, runs only the tests that must catch it, and restores the file byte for byte.
Every pattern is checked before anything runs (pass --check to only check)."""
import os, re, subprocess, sys

R = r"C:\Users\firas\Documents\AI-Project-[Alveara-Dental]"
C = os.path.join(R, "src", "alveara-client")
OUT = r"C:\tmp\n020c01\negative-controls"
F = {
    "layout": os.path.join(C, "src", "pages", "login", "LoginLayout.tsx"),
    "service": os.path.join(R, "src", "Alveara.Api", "Architecture", "LoginPresentation", "LoginPresentationService.cs"),
    "controller": os.path.join(R, "src", "Alveara.Api", "Controllers", "LoginPresentationController.cs"),
    "pipeline": os.path.join(R, "src", "Alveara.Api", "Architecture", "LoginPresentation", "LoginBackgroundPipeline.cs"),
}
ORIG = {k: open(p, "rb").read() for k, p in F.items()}

CONTROLS = {
    "NC-1 notice rendered as HTML": ("layout", "<p>{presentation.footerNotice}</p>", "<p dangerouslySetInnerHTML={{ __html: presentation.footerNotice }} />", "client", ["src/pages/login/LoginLayout.test.tsx"]),
    "NC-2 disabled notice still sent": ("service", "var notice = s.ShowFooterNotice ? s.FooterNoticeText ?? DeploymentDefaultNotice() : null;", "var notice = s.FooterNoticeText ?? DeploymentDefaultNotice();", "server", "FullyQualifiedName~Alveara.Api.Tests.LoginPresentationApiTests"),
    "NC-3 permission check removed": ("controller", "    [HttpGet(\"config/login-presentation\")]\n    [Authorize]\n    [RequireAccountPermission(AccountPermission.ManageLoginPresentation)]\n", "    [HttpGet(\"config/login-presentation\")]\n    [Authorize]\n", "server", "FullyQualifiedName~Alveara.Api.Tests.LoginPresentationApiTests"),
    "NC-4 input bytes stored instead of the clean derivative": ("pipeline", "            var bytes = encoded.ToArray();", "            var bytes = scaled is null ? input : encoded.ToArray();", "server", "FullyQualifiedName~Alveara.Api.Tests.LoginBackgroundPipelineTests"),
    "NC-5 a non-selected image served": ("service", "if (image is null || !image.IsActive || image.Sha256 != sha256 || image.Extension != extension || !store.Exists(image.StorageName)) return null;",
             "if (image is null || !image.IsActive || image.Extension != extension || !store.Exists(image.StorageName)) return null;", "server", "FullyQualifiedName~Alveara.Api.Tests.LoginPresentationApiTests"),
}


def native(key, s):
    return s.replace("\n", "\r\n") if b"\r\n" in ORIG[key] else s


for name, (key, old, _, _, _) in CONTROLS.items():
    n = ORIG[key].decode("utf-8-sig").count(native(key, old))
    assert n == 1, f"{name}: pattern matches {n} times in {key}"
if "--check" in sys.argv:
    print("all 5 patterns match once; nothing run")
    sys.exit(0)


def apply(key, old, new):
    text = ORIG[key].decode("utf-8-sig")
    bom = ORIG[key].startswith(b"\xef\xbb\xbf")
    data = text.replace(native(key, old), native(key, new)).encode("utf-8")
    open(F[key], "wb").write((b"\xef\xbb\xbf" if bom else b"") + data)


def restore():
    for k, p in F.items():
        open(p, "wb").write(ORIG[k])


def vitest(tag, files):
    r = subprocess.run("npx vitest run " + " ".join(files), cwd=C, shell=True, capture_output=True, text=True, encoding="utf-8", errors="replace")
    open(os.path.join(OUT, tag + ".txt"), "w", encoding="utf-8").write(r.stdout + r.stderr)
    return r.returncode, [l.strip() for l in r.stdout.splitlines() if re.search(r"Tests\s+\d|×", l)][:6]


def xunit(tag, flt):
    b = subprocess.run(["dotnet", "build", "Alveara.Api.Tests/Alveara.Api.Tests.csproj", "--nologo", "-v", "q"], cwd=os.path.join(R, "src"), capture_output=True, text=True)
    if b.returncode != 0:
        open(os.path.join(OUT, tag + "-build.txt"), "w", encoding="utf-8").write(b.stdout)
        return b.returncode, ["BUILD FAILED"]
    r = subprocess.run(["dotnet", "test", "--no-build", "--nologo", "--filter", flt], cwd=os.path.join(R, "src", "Alveara.Api.Tests"), capture_output=True, text=True)
    open(os.path.join(OUT, tag + ".txt"), "w", encoding="utf-8").write(r.stdout[-20000:])
    return r.returncode, [l.strip() for l in r.stdout.splitlines() if re.match(r"\s*(Passed|Failed)!|\s*Failed Alveara", l)][:8]


os.makedirs(OUT, exist_ok=True)
results = {}
try:
    for i, (name, (key, old, new, kind, target)) in enumerate(CONTROLS.items(), 1):
        apply(key, old, new)
        results[name + " (expect FAIL)"] = vitest(f"nc{i}", target) if kind == "client" else xunit(f"nc{i}", target)
        restore()
finally:
    restore()
    results["restored client (expect pass)"] = vitest("restored-client", ["src/pages/login/LoginLayout.test.tsx"])
    results["restored server (expect pass)"] = xunit("restored-server", "FullyQualifiedName~Alveara.Api.Tests.LoginPresentationApiTests|FullyQualifiedName~Alveara.Api.Tests.LoginBackgroundPipelineTests")

lines = [f"{k} -> exit {v[0]} {v[1]}" for k, v in results.items()]
lines.append("files restored byte for byte: " + str(all(open(p, "rb").read() == ORIG[k] for k, p in F.items())))
open(os.path.join(OUT, "SUMMARY.txt"), "w", encoding="utf-8").write("\n".join(lines) + "\n")
print("\n".join(lines))
