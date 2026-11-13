# ALV-N020-C01 R02: next-story impact

- Every change to the catalog or the selection must go through `LoginPresentationService`, which takes the catalog lock; a future writer (ALV-N029 rotation) must take `LoginBackgroundCatalogGuard.AcquireAsync` inside its own transaction before reading the count or the selection.
- Forced-colours rule: any new full-page decorative layer on the sign-in screens needs the same `forced-colors` suppression; the new browser spec is the check.
- ALV-N021 is not started and not authorized until ALV-N020-C01 is COMPLETE.
