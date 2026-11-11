# ALV-N019 R04: demo evidence

No user-visible change. The demonstrable result is the preflight: without the warm-up, the real forced pruning condition breaks the first pooled open in 154 of 800 cycles (`artifacts/preflight/control.jsonl`); with it, 900 of 900 first opens succeed, with 39 of the warm-up opens themselves recovered by the bounded retry, and nothing is left behind after any of the 60 iterations (`treatment.jsonl`). No screenshots.
