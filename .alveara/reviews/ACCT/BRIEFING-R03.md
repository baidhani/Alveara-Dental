# ACCT — Pre-Implementation Brief R03 Review

**Decision:** CHANGES_REQUIRED  
**Reviewed document:** `docs/design/ACCT_PRE_IMPLEMENTATION_BRIEF.md` (R03)

R03 satisfies the substantive R01 and R02 corrections:

- newly created human staff are a linked profile-and-account pair, with only labelled legacy remediation for historical unlinked profiles;
- temporary-password recovery is explicit and never re-displays the original credential;
- the production application contains no test-support route or assembly, while the separate test host is both Test-environment-gated and loopback-only; and
- multiple legacy profile-less administrators do not cause an automatic Deployment Administrator or owner choice.

One documentation consistency correction remains before implementation approval.

## CR-6 — Remove superseded multiple-admin assignment language

Section 3 correctly selects R01 CR-3 path 1: where an upgrade finds multiple profile-less Administrators, **none** becomes Deployment Administrator and an existing Administrator designates the initial Practice Owner directly.

But the audit table still says `DeploymentAdministratorAssigned` occurs when an “administrator pick[s] (several)”. That is the rejected path 2. Remove that wording. The event may remain for the deterministic migration conversion when there is exactly one eligible legacy profile-less Administrator, if that event is actually required; otherwise retire it. No normal route may select a Deployment Administrator after upgrade.

Also correct section 12: slice 5 must include the full test-host guard set **G1 through G8**, not merely G1 through G6. The focused matrix and acceptance evidence must use the same list.

No new architecture decision is required. Submit R04 with these brief-only consistency edits. No code is authorized by this review.

