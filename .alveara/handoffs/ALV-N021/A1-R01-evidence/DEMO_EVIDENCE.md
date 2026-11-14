# ALV-N021 A1-R01: demo evidence

No screenshots or walkthroughs in A1. Every A1 change is a date or time value whose correctness depends on the time zone and the time of day, which a test can set exactly (a test clock, `process.env.TZ` in four zones from -10 to +14 hours) and a screenshot on this machine (Central Time, daytime) cannot show. The proof is the focused tests and the negative controls (`TEST_RESULTS.md`, `ACCEPTANCE_EVIDENCE.md`).

What the owner would see after a rebuild:
- a printed prescription shows the same date of birth as the patient header (F-107);
- a charge made from a procedure performed in the evening names the practice's day (F-118), and a linked periodontal chart is named by the practice's day;
- a treatment-plan item keeps "Entered by ... on <time of the answer>" after it is scheduled, unscheduled or completed, and shows "Last changed by ... on ..." separately (F-184/F-218);
- the date-of-birth box accepts today and not tomorrow, at any time of day;
- new statements count ages in practice days; statements issued before the change reprint unchanged.

Walkthroughs: not run (`DEFERRED_TO_ALV-N028`).
