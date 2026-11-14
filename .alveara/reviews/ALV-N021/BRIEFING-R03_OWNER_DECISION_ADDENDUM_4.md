# ALV-N021 owner decision addendum 4 (answering BRIEFING-R03)

- **Date:** 2026-11-13. **Recorded by:** the coding agent (session CC-20261109-8w3d) from the product owner's messages in the working session.
- **Applies to:** `BRIEFING-R03.md` blocking correction 2 and the briefing's approval path.

## Decision 1: completion permission stays grantable to hygienists (R03 blocking 2)

Owner's words: "I want the permission to stay grantable to hygienists."

Resulting rule (OD-09 rev. 2): `CompleteOwnStartedVisit` defaults to Dentist and may be granted to Hygienist. It takes effect only when the actor's role is Dentist or Hygienist, the account is linked to a provider record, and that account recorded the visit's InTreatment event. Assistant and every other role are always denied on this permission, even if a grant is present in data; the Permission Matrix refuses granting it to any other role. The checkout gate still applies (OD-29).

This keeps the reviewer's safeguard against an accidental grant to an assistant or a non-clinical role, and departs from R03 only in allowing hygienists.

## Decision 2: nothing starts until the whole briefing is approved

Owner's words: "I want everything resolved and approved before we start. I put a lot of time and effort in this and I want it finished before we start anything."

Resulting rule: A1, although approved in R03, does not start until the reviewer approves the whole briefing (revision 4 or later).

## Owner approval

☑ Approved as written ☐ Approved with the changes noted below — Product owner: __Firas Baidhani__ Date: 11/13/2026__
