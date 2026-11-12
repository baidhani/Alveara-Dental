# ALV-N020-C01 R01: demo evidence

Real-browser screenshots of the built application (mocked API) in `artifacts/focused-proof/tile-gate/`:

- `tile-<key>-1920x1080.png`, `-1280x800.png`, `-375x812.png` for all five bundled backgrounds: the owner's mockup layout (page background, full-height light panel, the cleaned DentalKeep logo, "Sign in" heading, visible Username and Password labels, Sign in button).
- `mode-stretch-1280x800.png`, `mode-fill-1280x800.png`: the two other display modes on the blue operatory background.
- `seam-zoom-horizontal.png`, `seam-zoom-vertical.png`: 2x zooms of the repeat boundaries used for the tile-readiness verdict.

Real backend: the chain run (`chain-evidence/`, run `bdfe4ff950944046b817de3eb4cbcb0b`) drove the new sign-in page through stage 0 and stage 1 (sign-in, refusals, revocation, expiry, outage and recovery, brand checks B-01/B-02 with their screenshots in `chain-evidence/brand/`), and the auth walkthrough (12/12) drove first-admin bootstrap, sign-in, MFA, security administration and a real backup that now lists "Sign-in backgrounds".

Not demonstrated in a browser: the Sign-in screen tab and the presentation-administrator control are proven by component tests and the API tests, not by a real-backend browser walkthrough.
