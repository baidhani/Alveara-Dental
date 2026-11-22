# ALV-N026 R04: parent and dependency regression

**Parent course story:** none, so parent regression is n/a.

**Broad regression:** `DEFERRED_TO_ALV-N028`; none was performed or is implied.

**R04 changes no product or test code.** It changes only the shared walkthrough runner, additively. Every real-backend walkthrough now starts its API with the Test-Data PDF engine; before, any PDF request in a walkthrough answered 503.
