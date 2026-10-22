# Next-story impact - ALV-007-C01 R02

- **ALV-N016** (test infrastructure) is blocked on this correction and on the revalidation; after review it resumes as R03 Stage 1 (clone-based rollback design already approved) with focused evidence against this corrected dependency and one new frozen Stage 2.
- **ALV-N015** (item 42) stays blocked behind ALV-N016.
- Any code that reads the ledger gets the coherent read automatically; a caller can now receive `ConcurrencyConflictException` from a read-only method such as `GetAccountAsync` under sustained write contention (4 attempts exhausted); the existing controller handling returns the usual 409 and the client reloads.
