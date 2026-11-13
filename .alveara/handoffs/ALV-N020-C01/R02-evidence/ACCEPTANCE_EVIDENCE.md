# ALV-N020-C01 R02: acceptance evidence

The R01 mapping of the plan's 16 acceptance sentences stands (`../R01-evidence/ACCEPTANCE_EVIDENCE.md`). R02 closes the three review items and the two R01 limitations they came from.

| Review item | Closed? | Evidence |
|---|---|---|
| C01-1 200% zoom: keyboard operable, no horizontal scroll, sign-in and footer not obscured | Yes | `e2e/login-presentation-access.spec.ts` 4 tests (640x400 and 960x540, light and dark): `artifacts/focused-proof/a11y-run1.txt`; screenshots `artifacts/focused-proof/a11y/zoom200-*.png` |
| C01-1 forced colours: background suppressed; solid panel, focus, labels, form, status/error, notice readable | Yes (after a CSS fix) | same spec, 2 tests (1280, 375): failed 2/2 before the `LoginLayout.css` rule (`a11y-run0.txt`, `a11y-before/`), 6/6 after (`a11y-run1.txt`, `a11y/forced-colors-*.png`) |
| C01-2 deterministic select-versus-inactivate race; final state never selects an inactive image; exactly one refusal; truthful audit; trigger kept | Yes | `LoginBackgroundCatalogConcurrencyTests.Choosing_an_added_image_races_making_it_unavailable...` (barrier, 6 rounds); triggers untouched (`LoginPresentationSchemaTests` still pass) |
| C01-3 30-image cap concurrency-safe for insert and reactivation; `background_catalog_full`; never above 30; no orphan derivative | Yes | `Two_uploads_at_29...` and `An_upload_races_a_reactivation_at_29...`; negative control (lock off) fails the first |

Plan acceptance total stays 16 of 16.
