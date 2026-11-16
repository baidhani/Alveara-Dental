# ALV-N021 A2-R02: acceptance evidence (review CR-1)

Review `.alveara/reviews/ALV-N021/A2-R01.md` (CHANGES_REQUIRED, one correction).

| Review requirement | Done | Proof |
|---|---|---|
| Parse the documented ISO `CHAIN_CLOCK_UTC` with `CultureInfo.InvariantCulture` and the appropriate UTC styles | `ChainHostMain.TryParseClockUtc`: `DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal \| DateTimeStyles.AdjustToUniversal, ...)`, result in UTC; `Main` uses it (same error text and exit code 2) | `CHANGED_FILES.md` diff |
| Show the identical accepted clock value under th-TH and the nonstandard-time-separator culture of `TimeCultureGuardTests` | `ChainHostClockParseTests`, each test under th-TH, the dotted-time culture (en-US clone with time separator ".") and invariant | R1 33/33 |
| Expand the guard or add a narrow companion guard so fixed ISO parsing cannot regress to a current-culture overload | `TimeCultureGuardTests`: non-exact date `Parse`/`TryParse` must name the invariant culture; the chain host is scanned; the guard asserts it sees the chain-host parse; the A2-R01 line is a negative example | R1; R2 (the old line fails the guard, naming the file and line) |

The only other non-exact date parse in the two scanned projects (`BillingService.Documents.cs`, statement token) already names the invariant culture and stays green.

All other A2-R01 acceptance evidence (OD-01..OD-32, decision 02, the 42 finding records) is unchanged: `A2-R01-evidence/ACCEPTANCE_EVIDENCE.md`.
