# Acceptance evidence - ALV-003-C01 R03 (corrective attempt)

The approved ALV-003-C01 conditions (R02 approval, `.alveara/reviews/ALV-003-C01/R02.md`) are unchanged: no product file changed, and the registration page's own tests (now 10, all passing) and the rest of the client suite (1,545) pass. The reviewer's corrective conditions:

| # | Condition | Evidence |
|---|---|---|
| R1 | The affected client test is deterministic: it waits for the user-visible state transition instead of asserting synchronously after the asynchronous interaction | `TEST_RESULTS.md` (root cause and the fix); 30 of 30 isolated runs; a second test with a late focus frame |
| R2 | No patient-registration product behaviour changed | the diff is the one test file; `PatientRegistrationPage.tsx` unchanged |
| R3 | A focused negative-control demonstration that the prior assertion can race | `artifacts/negative_control.test.tsx` and its failing output |
| R4 | Focused and full-client-suite evidence | file 10/10 and 30 runs; full Vitest 1,545 of 1,545; tsc, lint, repository checks 13 |
| R5 | One shared frontend-only gate for the pair and this corrective on a frozen closure tree; no backend run | frozen in `stage2/`; PENDING |
