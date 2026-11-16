# ALV-N021 A2-R02: test results

2026-11-16, tree `b7efad6` (the code below was shown to the product owner before the run; total expected about 1 minute, within the owner's five-minute rule; the review allows only a genuinely focused run).

| # | Run | Expected | Actual | Result | Raw (`artifacts/focused-proof/`) |
|---|---|---|---|---|---|
| R1 | `dotnet test src/Alveara.Api.Tests --no-build --filter "FullyQualifiedName~TimeCultureGuardTests\|FullyQualifiedName~ChainHostClockParseTests"` | ~1 min | 4 s | **33 / 33** | `run1-33-passed.txt` |
| R2 | negative control: the A2-R01 call (`DateTimeOffset.TryParse(value, null, DateTimeStyles.AssumeUniversal, ...)`) put back inside `TryParseClockUtc`, same filter | < 1 min | ~40 s incl. build | **1 failed, 32 passed**: `TimeCultureGuardTests.Every_fixed_format_date_or_time_in_the_api_names_the_invariant_culture`, naming `Alveara.Api.ChainHost/ChainHostMain.cs:22` | `negative-control-old-line.txt` |
| R3 | file restored from the copy taken before the mutation (SHA-256 compared: identical), rebuilt, same filter | < 1 min | ~40 s incl. build | **33 / 33** | `run2-after-restore-33-passed.txt` |

## What each new test proves, and what it does not

- `ChainHostClockParseTests` (3 tests x th-TH, a dotted-time culture, invariant = 9 cases): the documented ISO text with `Z`, with an offset and with no offset is accepted as the same UTC instant on every one of these machine cultures; the documented default parses; non-instants are refused.
- **These culture cases also pass with the A2-R01 code** (R2): .NET reads this ISO 8601 form the same way under th-TH and the dotted-time culture, so the old call did not misread these values. They prove the identical accepted value the review asks for; they are not what catches a regression.
- The regression is caught by the guard (R2): `TimeCultureGuardTests` now scans `src/Alveara.Api.ChainHost` as well as `src/Alveara.Api`, flags a non-exact date `Parse`/`TryParse` that does not name the invariant culture (the R01 guard saw only `ParseExact`/`TryParseExact`), asserts it sees the chain-host parse, and has the exact A2-R01 line among its negative examples.

## R01 evidence in this package

The A2-R01 final-evidence run and stopped-run artifacts (`A2-R01-evidence/artifacts/final-evidence/`, the walkthrough screenshots and the two `.trx` files in the R01 ZIP) are kept unchanged. They are **pre-Stage-1, unreviewed diagnostic evidence**, recorded at `7207b88`..`215436c` (and one test at `24417e1`), before the R01 review and before `2ef0817` and `b7efad6`. They are not accepted Stage 2 or final regression evidence for any SHA.

## Not run (review instruction 4)

The 8-21 minute backend groups, the five walkthroughs, the accumulated chain, the mocked-browser suite and every broad or full regression. No Stage 2 contract is authorized yet; broad regression stays `DEFERRED_TO_ALV-N028`.
