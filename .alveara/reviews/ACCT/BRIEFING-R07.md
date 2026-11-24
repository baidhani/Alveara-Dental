# ALV-N030 / ACCT — R07 ChainHost Addendum Review

**Decision:** APPROVED_FOR_IMPLEMENTATION

**Reviewed:** 2026-11-24

## Scope reviewed

Section 17 of `docs/design/ACCT_PRE_IMPLEMENTATION_BRIEF.md`, which replaces the former separate-TestHost design for Slice 5 with an extension of the existing ChainHost.

## Ruling

Approved. This is the correct architecture: it preserves ALV-N019's PD-32 boundary that the production application's `Program.cs` remains unchanged, avoids maintaining two separate test hosts, and still gives the browser and chain runners one controlled, loopback-only account-fixture surface.

The addendum satisfies the required boundaries:

1. The product executable neither references nor maps test support; G1–G3 and G6 prove that negative boundary.
2. The new TestSupport library has an intentionally narrow reference graph, with an exhaustive project-reference scan.
3. ChainHost requires the exact `Test` environment, explicit opt-in, and a loopback-only listener before mapping the route; unsafe listeners refuse startup.
4. Real and injected clock behavior are explicit, while production and development continue to run only the product executable.
5. The harness migration is incremental, with focused green boundaries and the longer browser suite still requiring owner approval before it runs.

## Execution requirements

- Implement section 17 in Slice 5; do not create a second TestHost executable or split `Alveara.Api` startup.
- Correct the brief's top-level status wording before the first implementation handoff so it identifies R07 as the approved briefing/addendum set. This is a documentation consistency task, not an additional design review.
- Preserve the approved five-minute testing rule. Obtain the owner's explicit approval before the Slice 5c walkthrough suites or any other run expected to exceed five minutes.
- At handoff, distinguish the R07 ChainHost evidence from the still-unimplemented ALV-N013 installer/service-supervisor acceptance proof.

