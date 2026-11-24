# ACCT — Pre-Implementation Brief R02 Review

**Decision:** CHANGES_REQUIRED  
**Reviewed document:** `docs/design/ACCT_PRE_IMPLEMENTATION_BRIEF.md` (R02)

R02 resolves the three R01 blocking issues. New human staff are created only as an account-and-profile pair; legacy remediation is explicit. The test helper is isolated behind a separate test host. The multiple profile-less-administrator upgrade path deliberately selects no Deployment Administrator and permits an existing Administrator to designate the initial owner. Those design choices are approved.

Two small but material corrections remain before implementation authorization.

## CR-4 — Resolve `MustChangePassword` reset contradiction

The schema table says `UserAccounts.MustChangePassword` is set by both **Add staff member** and an **administrator password reset**. Section 6 and the R01 ruling say the opposite: the existing reset-complete flow remains unchanged because the recipient chooses their own password, so it does **not** set `MustChangePassword`.

Make the contract unambiguous:

- ACCT's generated temporary-password staff creation sets `MustChangePassword = 1`.
- The existing reset-token/reset-complete flow does **not** set it.
- If creation succeeds but the one-time temporary-password response is lost, the authorized recovery path must be named explicitly (the existing reset-token flow is acceptable if it is available to the account manager). It must issue a new credential/reset path rather than ever re-display the original temporary password.

Update the schema row, service rules, failure-first behavior, and focused test matrix accordingly.

## CR-5 — Constrain the test host to loopback use

The separate `Alveara.Api.TestHost` is the correct structural boundary, but its account-provisioning route is anonymous inside that host. The brief must require that the TestHost binds only to loopback addresses for local backend, walkthrough, and chain execution. It must reject an explicit non-loopback listen address rather than merely relying on `ASPNETCORE_ENVIRONMENT=Test` and `TestSupport:Enabled=true`.

Add proof that:

- the TestHost refuses a non-loopback binding request; and
- the two runner scripts start it on an explicit loopback URL.

This does not apply to the normal product API, which must still have no test-support code or route at all.

## Next step

Submit R03 after these brief-only corrections. No implementation is authorized by R02.

