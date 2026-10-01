# ALV-N001 R03 - Next Story Impact

Interfaces: Button variants keep their API. New UI code must keep using tokens; a token used as a TEXT colour must be checked against the surfaces it appears on (a token that is a button background, like dark `--color-primary`, is not a text colour). `buttonContrast.test.ts` is the pattern for pinning a component state's contrast.

Next: the `ALV-001-C01` R07 and `ALV-002-C01` R04 corrections, then a rerun of Gate A, which should keep the state-aware, settle-aware scan (`src/alveara-client/gate-a/`). No Execution Plan prompt change is required.
