# Auth walkthrough stabilization evidence (recovery-key step)

Measured on the real API (`gate-a/playwright.timing.config.ts`, 6 keys per run) with the real RSA-3072 OpenPGP key generation behind `POST /api/backup/recovery-key`.

- Key-generation latency, idle machine: n=18, min 709 ms, median 2239 ms, max 4581 ms, above 4.3 s: 3, above 5 s: 0
- Key-generation latency, under CPU load (two concurrent full vitest suites): n=18, min 1952 ms, median 2531 ms, max 6610 ms, above 4.3 s: 3, above 5 s: 2

Auth walkthrough (`playwright.auth.config.ts`, default config) under the same CPU load:

- Unchanged spec (5 s default assertion timeout), 6 runs: 1 passed 12/12, 5 failed at the recovery-key step (`getByLabel('Recovery key file contents')`, 5000 ms), 0 failed elsewhere.
- Stabilized spec (30 s bounded wait on that assertion, 90 s budget for that test), 12 runs: 11 passed 12/12, 0 failed at the recovery-key step, 1 failed elsewhere.
  Per run: 1: 12 passed, 2: 12 passed, 3: 12 passed, 4: 12 passed, 5: 12 passed, 6: 12 passed, 7: 12 passed, 8: 12 passed, 9: failed elsewhere (practice configuration, see report), 10: 12 passed, 11: 12 passed, 12: 12 passed

The one run that failed elsewhere (run 9) failed in the practice-configuration test: its whole 30 s test budget was spent waiting to click "Save practice information" while the button stayed disabled. It passes in every idle run (including all eight idle runs of this gate rerun). This is disclosed in the report; it belongs to ALV-N003 and is not changed here.
