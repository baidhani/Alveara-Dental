# ALV-N022 A1-R02: parent and dependency regression

- Parent regression: n/a (new production story); broad regression `DEFERRED_TO_ALV-N028` (ledger unchanged except the A1-R02 line).
- Dependency: the only changed component (`FinalizeReview`) is rendered by `EncounterView`; covered by its component test and the clinical walkthrough (9/9) that opens it. No server, API or schema change.
