# ALV-N020 R02: demo evidence

The chain re-run of 2026-11-12 (run `b46044d2ae1f4dab85d69ab97e88bbcc`) in a real browser against the real chain host on a throw-away database. Raw: `artifacts/focused-proof/chain-evidence/` and `chain-output.txt`. All R01 steps passed again; the two changed steps:

| Step | What a person sees | Result |
|---|---|---|
| 13.05 | The front desk is typing a new patient's first name. The local server stops (the runner stopped its own API process). The "Local server unavailable." banner appears; the screen, the page and the typed name stay; nobody is sent to the sign-in page. The server starts again: the banner goes, the person is still signed in (the server accepts the same session), the typed name is still there, no "session expired". | PASS |
| 1.12 | With a 5-minute session, the front desk types a first name and clicks Patients: the app asks "You have unsaved changes. Discard them?"; they stay. About 3 minutes in, "Your session will expire soon. Save your work and sign in again to continue." appears and the name is still in the form. At 5 minutes the app goes straight to the sign-in page with "Your session expired"; no prompt holds it, and the unsaved form is gone from the screen. | PASS |

Runner records for 13.05: `api-control/stopped.ack`, `api-control/started.ack`, and the `API-STOP` / `API-START` PASS entries in `evidence.json`.
