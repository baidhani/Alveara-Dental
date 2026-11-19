# ALV-N024 — addendum 01 independent review

**Package:** `Alveara_Addendum_ALV-N024_5e4f799_01.zip`  
**SHA-256:** `EE25C9E76C1667F7187B7E8E5DD97DD7E6C8C317B048A6AC970B47059D0C1DA8`  
**Decision:** **APPROVED — implementation may resume within briefing R02 as amended.**

## Ruling

The direct-SQL result does not weaken the receipt invariant or require a mechanism change.

- On the direct path, each session inserts before its `AFTER INSERT` trigger acquires the payment lock. The lock holder can then wait on the other session's uncommitted row while the other session waits for the lock. A deadlock victim (1205), an eventual prior-receipt refusal (51295), or the defined five-second lock-timeout refusal (51297) are all valid losing outcomes.
- Each valid outcome rolls the losing transaction back. The required invariant is exactly one surviving receipt, with no partial document, audit row, or surviving transaction-owned application lock.
- The service path acquires the same transaction-owned payment lock *before* it inserts. A competing service call therefore holds no uncommitted receipt row while waiting and cannot create this direct-SQL wait cycle.

The proof may accept only **51295**, **1205**, or **51297** for the losing direct-SQL contender, while continuing to require one success and exactly one surviving receipt in every repeated round. The existing service-path tests retain their stricter expected outcomes.

## Scope boundary

This approves only the addendum to the R02 briefing and its proof expectation. It is not a review or approval of the partial Slice 1 production code included for context. Continue implementation under the approved R02 contract and package a complete R01 handoff for the full independent implementation review. Keep all focused groups within the five-minute policy; no broad/full regression is authorized.

