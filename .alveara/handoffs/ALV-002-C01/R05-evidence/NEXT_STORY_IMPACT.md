# ALV-002-C01 R05 - Next Story Impact

Do not add per-page overrides for the banner title; `conflictBannerContrast.test.ts` fails if a stylesheet re-colours it. Warning-coloured TEXT uses `--color-warning-text`.

Next: `ALV-N002`'s `.status-stale-banner` (`SystemStatusPage.css`) still uses `--color-warning` text on `--color-warning-bg` (3.47:1); point it at `--color-warning-text` and add a contrast test, as its own reopened attempt. Then Gate B. No Execution Plan prompt change is required.
