# ALV-N020 R02: next-story impact

Everything in `../R01-evidence/NEXT_STORY_IMPACT.md` still holds, with these changes:

- **Chain runner:** Playwright now runs as a child process of `scripts/Invoke-AccumulatedChain.ps1`, started through `System.Diagnostics.Process` with an explicit working folder (never `Start-Process`: the repository path contains `[` and `]`). A later stage that needs the API stopped or restarted can use the same channel: write `stop.request` / `start.request` into `CHAIN_API_CONTROL` and wait for `stopped.ack` / `started.ack` (see `apiControl()` in `chain/stage-1.spec.ts`). The runner acts only on the API process it started.
- **A restarted chain host** keeps the run's database, key ring and storage, so sessions survive. Whether the injected test clock keeps a moved time across a restart was not examined; no stage-1 step depends on it, but a later stage that moves the clock and then restarts the API must check it.
- **Imports:** code that needs the identity counter imports `useIdentityEpoch` from `src/contexts/authContextObject.ts`; `AuthState` is still importable from `AuthContext.tsx`.
- **Chain duration:** stage 0 + stage 1 took 5.8 minutes of Playwright time (about 6.5 minutes end to end).
- ALV-N021 is not started and not authorized until ALV-N020 is closed.
