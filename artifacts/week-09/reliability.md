# How the desk behaves when the vendor misbehaves

Written against the code in this folder. `node desk.js confirm <orderId>` asks the stand-in vendor (`vendor.js`, behavior set by `VENDOR_MODE`) to confirm an order, then "sends" by appending a line to `data/sent.log`. Run `npm test` to re-check the duplicate protection and `node verify.js` to see all four vendor modes.

## 1. What happens when the vendor fails?

Every call to the vendor runs under three layers, outside in: `runOnce` (is this order already done?), `circuitBreaker` (is the vendor worth calling right now?), and `retry` with a per-attempt `withTimeout`. If the vendor is down it throws `UpstreamUnavailable`; if it hangs it hits the 2-second deadline and throws `TimeoutError`. After three exhausted operations the breaker in `data/breaker.json` opens, and further calls fail instantly with `BreakerOpen` without touching the vendor at all. A reply that arrives is then judged by the quality gate (`assertQuality`, built on `scoreDetail`): below 70 it is refused with `QualityGateRejected`. Each run ends by printing one receipt with the order, correlation id, attempts, breaker state, gate score and outcome (`sent`, `fallback`, `duplicate` or `dead-lettered`).

## 2. Does it retry? With what strategy?

Yes, via `retry` in `reliability.js`: at most 3 attempts, each with a 2-second deadline that lives inside the retry. The gap before the next attempt starts at 500 ms and doubles (500 ms, then 1 s), each scaled by random jitter between 50% and 100%, so the worst case for one order is about 7 seconds. It retries only `TimeoutError` and `UpstreamUnavailable`. It never retries `QualityGateRejected` (a wrong answer is not fixed by asking again as if it were an outage), `BreakerOpen` (the whole point is to stop calling), or `ClaimInFlight`. The circuit breaker wraps the whole retry loop, so a full set of three failed attempts counts as one failure toward the breaker's threshold of three.

## 3. What is the recovery path when the retries are exhausted?

It depends on the error name. For `UpstreamUnavailable` and `BreakerOpen` the desk sends a plain template, "Your order <id> is confirmed. Full details will follow shortly.", which is itself gated (it scores 100), and marks the `sent.log` line `"fallback": true`. Note that this marks the order as done in `data/keys.json`, so the full vendor message is not sent later for that order. For `QualityGateRejected`, and for `TimeoutError`, there is no fallback: the order is appended to `data/dead-letter.jsonl` with its error name, correlation id and time (plus the score and the reasons for lost points when the gate refused it). Nothing is sent to the customer. `node desk.js replay` re-runs every dead-lettered order through the normal path and removes the ones that send. Replay is manual; nothing schedules it.

## 4. What does this code handle, and what does it NOT?

**Handled:** a vendor that is down (`UpstreamUnavailable`), a vendor that hangs past the deadline (`TimeoutError`), a vendor that keeps failing (`circuitBreaker` stops the spend), the same order submitted twice, in sequence or at the same moment from two processes (checked: one `sent`, one `duplicate`, because `runOnce` claims the key under a lock file before the side effect), and a vendor that answers confidently and wrongly in the ways the gate can see: wrong order number, apology boilerplate, a message that is too short or too long.

**Not handled, and I checked the first two by breaking them:**

- **A corrupted `keys.json` fails open.** `readKeys` treats unreadable JSON as "no keys", so the next run for an order already sent sends it again (confirmed: two lines in `sent.log`), and the file is then overwritten with only the new key, erasing the history. A corrupted `breaker.json` likewise resets to closed. Nothing detects or reports either.
- **A crash between claiming a key and storing its result leaves the order stuck.** The key stays `claimed`; later runs wait 5 seconds, then fail with `ClaimInFlight` and the order is dead-lettered (confirmed). Replay cannot clear it. Someone must remove the key by hand. If the crash came after the `sent.log` append, the message did go out, and the stuck key is what stops a second one.
- **Two processes at once is only half covered.** The key file is locked; `data/breaker.json` is not. Concurrent runs can lose a failure count, and two runs can both slip through as the "one probe" after the cooldown. Appends to `sent.log` and the dead-letter file are not coordinated either. And a second run that arrives while the first is still retrying waits at most 5 seconds before giving up with `ClaimInFlight`, even though the first may succeed.
- **A vendor that is slow but under the 2-second deadline is invisible.** It succeeds, so the breaker never sees a problem, and nothing measures latency. Separately, `withTimeout` abandons a slow call; it does not cancel it, so a vendor may still finish (and bill) work the desk has given up on, and a retry can make it do the same order twice.
- **The gate checks form, not truth.** A reply that contains the right order id, no banned phrase, and a reasonable length passes, even if it says the wrong ship date or cancels the order.
- **Everything is files on one machine.** No shared storage across hosts, no transaction spanning `keys.json` and `sent.log`, and cooldowns depend on the local clock. "Send" is a log line, not a real message.
