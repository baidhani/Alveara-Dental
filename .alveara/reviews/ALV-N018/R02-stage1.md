# ALV-N018 — Independent Review R02, Stage 1

**Decision:** APPROVED_PENDING_REGRESSION  
**Reviewed:** 2026-10-23  
**Handoff:** `Alveara_Handoff_ALV-N018_f70e93f_R02.zip`  
**Package SHA-256:** `9F4C6A9DEB6CDB9A6650DB72ADE8B5EE2AB923C71C9903E66BC54BE04681FE3D`  
**Implementation reviewed:** `f70e93f81a1f89ea9087c651917a2c1489d43731`  
**Evidence reviewed:** `a4643a5a4b1d54f4a971df0af6002fae8537b66d`

## Decision basis

R02 corrects `ALV-N018-R01-01`. The matrix refresh interval is now **25,000 ms**, preserving the editor-open and hidden-page safeguards while leaving margin below the 30-second item-44 limit. The real-backend walkthrough now starts timing when the external write returns, requires the refreshed matrix state within **30,000 ms**, and records a measured result of **25,143 ms**. The associated screen test pins the 25-second-or-less interval.

I verified the correction in the reviewed source and evidence. The server-side controls reviewed in R01 remain in place: protected and fixed roles are rejected in the write service; writes require CSRF, idempotency, policy-version concurrency, bounded reason, confirmation, and typed confirmation where required; policy/history/audit changes are transactionally coupled; delegation remains a closed Office Manager model; and the broadened account list preserves the legacy denial body for callers lacking every qualifying permission.

The R02 client and walkthrough gates passed on the reviewed tree: TypeScript, build, lint, Vitest **1,572 in 100 files**, mocked Playwright **140**, repository checks **13**, and the permission-policy-admin real-backend walkthrough **8/8**. The backend is unchanged from R01; its focused **238/238**, HTTP-level **788/788**, restore, and negative-control evidence remains applicable. I also independently ran the repository’s frozen Stage 2 evaluator validation: all **11/11** synthetic scenarios produced the required accept/reject result, including acceptance of an otherwise-valid 80-minute backend run and rejection of every substantive outcome or identity mismatch.

## Frozen Stage 2 authorization

This approval authorizes exactly **one** formal Stage 2 regression, after the required review-closure-only commit. The closure tree must be clean and have no `src/` differences from `f70e93f81a1f89ea9087c651917a2c1489d43731`.

Run once, with no other test activity:

```sh
sh .alveara/handoffs/ALV-N018/R02-evidence/stage2/run_stage2.sh f70e93f81a1f89ea9087c651917a2c1489d43731 <out-folder>
```

The evaluator must qualify all frozen substantive criteria:

- **4,628 backend passed, 0 failed, 0 skipped**;
- preservation of the ALV-N017 R02 baseline except the one declared retired ID, with exactly the 60 listed ALV-N018 IDs;
- one template build, zero fallbacks, at least 3,000 clones, permitted real-path fixture count, and unchanged leak counts;
- TypeScript, build, lint, Vitest **1,572 in 100 files**, mocked Playwright **140**, and repository checks **13**;
- real-backend walkthroughs **7, 8, 10, 14, and 12**; and
- the predeclared permission-policy performance budgets.

Backend elapsed time is operational evidence only; it is not a pass/fail condition. Submit the regression ZIP for final review. Do not mark ALV-N018 `COMPLETE` and do not begin STORY-009 until final approval is recorded.
