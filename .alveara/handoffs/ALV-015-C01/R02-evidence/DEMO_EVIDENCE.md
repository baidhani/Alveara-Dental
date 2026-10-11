# ALV-015-C01 R02 - Demo evidence

Real browser against the real API and a fresh SQL Server database (`playwright.treatment-plans-complete.config.ts`, 11 of 11). The R02 step is the last walkthrough test, "CATALOG USAGE (review finding R01-01)":

1. An admin opens Procedures and fees and chooses Inactivate on PERIO-1, which two plans use. The warning reads "PERIO-1 is referred to by 2 records (Treatment plans: 2)", says inactivating does not change those records, and asks for an explicit "I understand and want to inactivate it" (`artifacts/walkthrough/13-catalog-usage-warning.png`; the screenshot also shows "Confirm that you understand before inactivating." while the box is unchecked).
2. After confirming and giving a reason the procedure is inactivated.
3. The patient's plans still show PERIO-1 with its fee $120.00 (catalog version 1) and its states (`artifacts/walkthrough/14-plan-after-inactivation.png`); the plan form no longer lists PERIO-1; an API attempt to add it to new work is refused (400, "inactive").
4. Axe accessibility scan, light and dark themes: no critical or serious violations on the new view.

The earlier screenshots (01-12) are unchanged and still produced by the same walkthrough (`R01-evidence/artifacts/walkthrough/`).
