# ALV-N004 R06 - Next Story Impact

Pages that gate content on `hasPermission(...)` must not put the permission in a data-loading callback's dependency list: when the permission set arrives late it re-runs the blocking load and unmounts the page (and any form in progress). Read it through a ref and refresh in place, as `BackupRecoveryPage` now does. Next: **Gate B**, a mandatory stop; it needs a clean re-run of the auth walkthrough (its recovery-key step is timing-sensitive, see the disclosure). No Execution Plan prompt change is required.
