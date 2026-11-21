# ALV-N026 S5: PdfPig, a test-only dependency

**Approved by:** the owner, 2026-11-21. PdfPig is used in the test project only, for rendered-text assertions. No separate design review is needed, but it is disclosed in the S5 handoff.

| Field | Value |
|---|---|
| Package | `PdfPig` 0.1.16, pinned exactly in `src/Alveara.Api.Tests/Alveara.Api.Tests.csproj` |
| Source | nuget.org (`https://api.nuget.org/v3/index.json`); project `https://github.com/UglyToad/PdfPig`, commit `a7bb35662bbbf405efddad50aedc9bcdcf515afc` |
| Licence | Apache-2.0 (the package's `<license type="expression">`) |
| `.nupkg` SHA-256 | `d67171846ea8c28f50359137065fec4514266d7a32b23eae6c5f2ebed8ffcfc4` |
| NuGet content hash (SHA-512) | `qQcbsNMYVY+K5ELptax2S8thNMk0+rSQq3H/KdYj3LLcko8yTCz8y5KIvsKxIyAuTN0ZYNnfsZkM+0EvxTwHVA==` |
| Known vulnerabilities | None reported (`dotnet list package --vulnerable --include-transitive`, 2026-11-21) |

**Where it is used and where it is not:**
- **Referenced by:** the test project only. `src/Alveara.Api/Alveara.Api.csproj` does not reference it.
- **Never shipped:** it is not in the API's build output and not in the installer.
- **Not in the release store:** `C:\.alvera-data\release-artifacts` holds no PdfPig files.
- **Used for:** reading the text of rendered PDFs in tests (identifiers, MRN, dates, amounts, labels, warnings and notices), never for byte comparison.
