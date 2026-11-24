# ACCT — Pre-Implementation Brief R01 Review

**Decision:** CHANGES_REQUIRED  
**Reviewed document:** `docs/design/ACCT_PRE_IMPLEMENTATION_BRIEF.md`  
**Scope:** Account, staff-profile, Deployment Administrator, Practice Owner, bootstrap, and sign-up-removal architecture.

## What is approved in principle

The brief correctly captures the owner-approved model:

- a Deployment Administrator is an account-only, Administrator-equivalent bootstrap identity;
- a Practice Owner is a human staff member with both a linked account and staff profile, and a protected ownership designation rather than a new broad role;
- normal human staff creation is combined profile-and-account creation in Practice Configuration → Staff;
- anonymous registration and the old bootstrap-admin endpoint are removed;
- initial setup uses a short-lived, one-time token entered in a form body, not a URL; and
- no owner is silently selected during an upgrade.

The planned security evidence is appropriately focused and the separate, follow-on setup-wizard story is correctly out of scope.

## Required corrections

### CR-1 — Do not create new profile-only staff records

Sections 2 and 4 keep `POST /api/config/staff` for a new “staff profile (no login)” and expose an “Add staff profile (no login)” action. This conflicts with the owner's explicit rule: normal human staff are not created separately as an account or profile; a staff member/user is the linked pair.

Revise the brief so that:

- all *new* normal staff are created only by the combined **Add staff member** transaction;
- no current UI or ordinary API route creates a new standalone staff profile;
- legacy, already-unlinked profiles/accounts are retained for history and may be remediated only through a carefully labelled legacy-management path; and
- Deployment Administrator and future technical-service accounts are the only account-only exceptions. Technical service accounts remain outside ACCT scope.

Add direct tests that the old profile-only creation route/action cannot create a new record after the migration, while existing historical unlinked rows remain readable and auditable.

### CR-2 — Make test support structurally impossible in production

The desired rule is correct: the test helper must not exist in a deployed application. However, section 8 currently combines “the production API does not reference the test-support assembly” with a `--test-support` startup path used by real Kestrel walkthroughs. The brief does not explain how that production API process loads the test-support code while its published output simultaneously excludes it.

Replace this with a structurally separate test host/executable (for example `Alveara.Api.TestHost`) that references both the product API and the test-support assembly. Backend tests, walkthroughs, and chain runs start that test host; the normal API executable has neither a test-support reference nor a test-support startup argument.

The revised proof must establish:

- the production publish output contains no test-support assembly and maps no test-support route;
- a normal API invocation rejects/does not recognize test-support startup input;
- the separately built test host alone maps `POST /api/test-support/accounts` under the dedicated Test environment and explicit test setting; and
- no production/deployment script references the test host or enables its setting.

This is a design correction, not permission to implement either alternative first.

### CR-3 — Specify the multiple profile-less-admin upgrade path

The migration correctly leaves several profile-less Administrators unconverted. But the brief then says an upgrade notice “lists them” and an administrator “picks one” without defining the route, authority, confirmation, audit record, or how it differs from Practice Owner designation.

Choose and specify one of these explicit transition paths:

1. no Deployment Administrator is selected in this case; an active existing Administrator designates the initial eligible Practice Owner directly under D-7; or
2. a one-time, confirmed, audited transition action selects exactly one of the existing profile-less Administrators as Deployment Administrator, followed by the normal owner-creation flow.

Do not silently choose a candidate. The tests must cover zero, one, and multiple profile-less Administrators.

## Rulings resolving the brief's listed questions

- **Profile-only staff creation:** remove it for new staff (CR-1). This is not left open.
- **Administrator password reset:** retain the existing reset-complete flow without setting `MustChangePassword`; the recipient chooses their own password in that flow. This is not a temporary-password issuance.
- **Dashboard notice wording:** the supplied wording is directionally approved, subject only to ordinary copy-editing during implementation. It must remain advisory: manual configuration stays available to the Deployment Administrator, while the guided wizard remains exclusive to the Practice Owner.

## Next step

Revise the brief and submit R02 for independent pre-implementation review. No ACCT code, migration, test-harness change, route, or status transition is authorized by this review.

