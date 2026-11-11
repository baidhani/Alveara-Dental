# ALV-N019 R04 artifacts

- `preflight/`: the final preflight run (SUMMARY.txt, control.jsonl, treatment.jsonl and the two console logs). Each jsonl line is one iteration with every cycle's outcome, retry counts, duration and the independent database and file counts.
- `negative-controls/`: the raw output of the three mutations and of the restored check.
- `calibration/`: earlier smaller runs used to size the arms.
- `inventory/`: the script-generated owner inventory.
- `new-test-ids.txt`, `new-tests.trx`: the 18 new backend test ids and their results.
- `changed-files-name-status.txt`: the exact file list of the implementation commit.
- `sample-filter.txt`: the filter of the focused sample run.
