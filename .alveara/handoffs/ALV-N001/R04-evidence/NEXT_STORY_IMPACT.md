# ALV-N001 R04 - Next Story Impact

New code that shows status-coloured TEXT must use `--color-success-text` / `--color-warning-text` (or `--color-danger` / `--color-info`, which pass), never `--color-success` / `--color-warning` as a text colour; the plain values are for borders and icons. `statusContrast.test.ts` is the pattern for pinning a pairing.

Next: `ALV-002-C01` (finding F2): point the conflict-banner title at `--color-warning-text` and remove the three page-level workarounds (`Calendar.css`, `PatientWorkspace.css`, `FlowBoard.css`). Open: `ALV-N002`'s `.status-stale-banner` has the same warning-pair defect. Gate B stays blocked until both are corrected and approved. No Execution Plan prompt change is required.
