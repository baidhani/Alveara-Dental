# ALV-N022 A2-R01: next-story impact

- **A3 (not authorized):** unaffected by A2.
- **Walkthroughs that record a diagnosis** (diagnoses, completion, completion-adoption and the chain) must record it on a Draft encounter; a script that finalizes an encounter and then records a diagnosis on it now gets 409 `encounter_finalized`. A likely candidate for the reviewer's Stage 2 contract.
- **Existing data:** diagnoses already attached to finalized encounters (2 on the dev database) stay as they are, unlabelled (Q-3 deferred).
- **Deferred decisions** (register Q-5ad note): documents and referral cases on finalized encounters; a display of links made after finalization.
