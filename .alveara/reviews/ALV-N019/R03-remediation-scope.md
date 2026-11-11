# ALV-N019 — PD-26 Remediation Evidence Scope Ruling

**Ruling: a full frozen Stage 2 regression is required after R03 Stage 1 approval.**

The proposed direct `System.Security.Cryptography.Pkcs` 9.0.21 pin is appropriately narrow: one package-reference addition in the existing 9.0 line, with no source, test, script, configuration, or lock-file change proposed.  It is authorized by the recorded product-owner PD-26 decision to remediate.

It nevertheless changes the restore and runtime dependency graph.  The accepted R02 Stage 2 run certifies the prior resolved graph containing 9.0.18; it cannot certify the graph with 9.0.21.  The binding two-stage policy requires broad regression to pass before final `COMPLETE` after a reviewed implementation change.  This run is therefore necessary evidence, not a duplicate run without a change.

## R03 procedure

1. Submit a focused R03 Stage 1 handoff containing the exact package-reference diff and no unrelated package movement.
2. Include `dotnet restore`, `dotnet list package --vulnerable --include-transitive` for `Alveara.Api`, `Alveara.Api.Tests`, and `Alveara.Api.ChainHost`, plus `dotnet nuget why` evidence proving 9.0.21 resolves on every relevant path.
3. Run the focused API and injected-chain coverage described in the R02 Stage 2 review, update the advisory disposition to `fixed` only if the raw results prove it, and preserve the R02 evidence unchanged.
4. After independent Stage 1 approval, run one new frozen Stage 2 contract on the unchanged R03 snapshot.  It may reuse the R02 contract shape, but must produce fresh results for the R03 package graph.  No code/test/package edit may occur between that approval and the run.

The R03 Stage 2 comparison should retain the R02 test-identity baseline and explicitly account for any expected test-count change.  ALV-N019 may close only after that run passes final independent review.
