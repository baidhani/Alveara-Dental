# ALV-N020-C01 R02: parent and dependency regression

No parent course story. R02 changes only `LoginPresentationService` transaction wrapping, one new guard class, one CSS media rule and tests. The R01 dependency proof stands; the login browser group (12 + shell + banner) and the login/backup/inventory server group were re-run and pass (TEST_RESULTS 5 and 6). Broad regression: `DEFERRED_TO_ALV-N028`.
