# ALV-N020 Pre-Implementation Brief Review

**Decision: APPROVED FOR IMPLEMENTATION**

The briefing is sufficiently complete to authorize implementation of ALV-N020.
It provides the required raw finding logs, scoped technical design, file
inventory, acceptance mapping, security/audit behavior, focused-proof plan, and
program-level deferred-regression record. ALV-N019 is durably closed at R07,
with broad regression correctly deferred to ALV-N028.

## Approved decisions

1. **Staff deactivation permission:** require both the existing practice-
   configuration authority and `ManageUsers`. A user missing either permission
   receives a clear refusal before any change occurs. This reflects that the
   operation disables an account and revokes its sessions, not merely a staff
   profile.
2. **Failure notification:** use the existing `Urgent` priority. Send only to
   enabled Admin and OfficeManager accounts. The post-rollback durable
   operational/audit record is required; notification is best effort and can
   never be the sole evidence of failure.
3. **Chain coverage:** build the stated Stage 1, minimal Stage 2, and outage
   recovery chain coverage in this story. Run its focused proof only with the
   owner's explicit permission. The accumulated chain runs belong to ALV-N028.
4. **PD-01:** leave the signed-in outage wording unchanged in this story. The
   sign-in page may use the narrowly scoped unavailable-server wording in the
   briefing. Do not alter `REQUIREMENTS.md` or make a broader offline-product
   claim without a separate owner decision.
5. **PD-36:** keep session extension out of scope. Fixed expiry and the
   existing ALV-N009 sequence guard remain unchanged.

## Required implementation guardrails

- **Only authenticated 401/expiry/revocation may end an authenticated client
  session.** Network failures and all authenticated refresh/revalidation 5xx
  responses—including a 500—must retain the signed-in shell and unsaved form,
  expose the connection-failure state, and revalidate later. Sign-in itself may
  describe an ordinary 5xx as a server problem.
- Do not persist a prior user identity, patient context, or return destination
  in local storage. A same-user expired-session return may be held only in
  ephemeral, identity-bound navigation state; explicit sign-out and any identity
  change clear it. A different identity always lands on `/`.
- The staff-profile change and linked-account disable must share one transaction.
  The failure record is written only after rollback through a separate short
  transaction, includes correlation context, and never claims the deactivation
  occurred.
- Preserve all existing account-disable protections: target authorization,
  last-admin protection, security-stamp rotation, account audit behavior, row
  version conflict handling, and reactivation not re-enabling the account.
- No migration was authorized by this briefing initially. The narrowly scoped
  notification-kind migration is subsequently authorized only by
  `ALV-N020_MIGRATION_DECISION.md`; any other migration or persistent-model
  change still requires a new stop-and-review decision.

## Testing and handoff control

No test run is authorized by this briefing alone. Before each focused test
group, ask the owner for permission with the exact command group and expected
duration. Do not request or run a broad regression. The final handoff must use
`fullRegression: "DEFERRED_TO_ALV-N028"`, update the program regression ledger,
and state that it is not release-certified.

The Stage 1 package must include the acceptance evidence in section 11 of the
briefing, negative controls for F-037, F-014/F-155, and F-021, and the explicit
disposition/evidence for the F-012 and F-015 verify-first checks. If an owner
declines either check, record that truthfully rather than reporting it as run.

ALV-N020 implementation may now begin. Do not start ALV-N021 until this story
has independently closed through the focused deferred-regression route.
