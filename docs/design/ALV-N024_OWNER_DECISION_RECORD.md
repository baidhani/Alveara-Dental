# ALV-N024 owner decision record

**Date:** 2026-11-18. **Source:** the owner's answers to the decisions in `ALV-N024_FINDING_SURVEY.md`, quoted as given.

| Id | Decision | Owner's words |
|---|---|---|
| PD-17 (F-114, Q-5cs) | Replacement plus reason. | "Use a controlled replacement receipt with a new number, required reason, original marked "superseded by," and an audit record. This preserves financial history without allowing silent duplicate receipts." |
| PD-18 (F-209, F-205) | Guarantor plus household. | "Offer the patient, registered guarantor, and verified household members only - never an arbitrary patient search. Default to the responsible party. This accommodates a parent or spouse paying while keeping selection bounded to an established relationship." |
| Q-5bf | Allow it. | "Account responsibility should be recordable before the first charge. That is normal administrative setup, not a billing event." |
| F-193 (with D-09) | Move with the sub-tabs in ALV-N027. | "Keep practice-wide payment-method configuration out of individual patient billing screens for now, but move it once as part of the planned N027 Billing sub-tabs work." |

## What the brief must settle from these
- **PD-17:** what "superseded by" looks like on the original (its list row, its opened view and its print), and whether a statement can also be "replaced". A statement is a cut-off, and the survey recommends that a later statement is a new statement, not a replacement; the brief states this for review. There is also the migration for the replacement link and its guards (append-only; the replacement belongs to the same account and payment).
- **PD-18:** what "verified household members" means in the data model today (the household and guarantor records behind `HouseholdGuarantorPanel`). If no "verified" flag exists, the brief proposes the rule (for example, members of the patient's recorded household) and stops for the owner if that is a schema change. The server must enforce the same set; the client list alone is not enough.
- **Q-5bf:** the client shows the responsibility form before the first charge. The server already opens the account on that record; the brief confirms this has no billing side effect (no entry, no document).
