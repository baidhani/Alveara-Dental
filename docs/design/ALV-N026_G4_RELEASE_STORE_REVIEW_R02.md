# ALV-N026 G4 release-store R02 — independent review closure

**Decision:** APPROVED. **G4 is CLOSED.**  
**Reviewed record:** `docs/design/ALV-N026_G4_RELEASE_STORE_RECORD_R02.md`

## Verified closure evidence

The actual release store at `C:\.alvera-data\release-artifacts\` is now inheritance-protected. Its effective write/full-control identities are limited to:

- the named owner, `FEERCY-LEGION7I\firas`;
- `BUILTIN\Administrators`; and
- `NT AUTHORITY\SYSTEM`.

There is no `Authenticated Users`, `Users`, or `Everyone` modify/write permission. The build/release identity is the named owner; a future separate build identity must receive only the documented read/execute access through a reviewed change.

The store artifacts remain unchanged and match their approved values:

- Typst ZIP: `19ce3551153c2fe7ee9fa2f95208310c8f4d3209fedb699e0333faf8913f6736`;
- Noto Sans ZIP: `0c34df072a3fa7efbb7cbf34950e1f971a4447cffe365d3a359e2d4089b958f5`;
- Microsoft Visual C++ Redistributable: `cc0ff0eb1dc3f5188ae6300faef32bf5beeba4bdd6e8e445a9184072096b713b`, with a Valid Microsoft Authenticode signature.

The later S4 release/build integration must compare against exact approved SHA-256 values in a separately reviewed, version-controlled manifest. Store-local `SHA256SUMS.txt` remains inventory only and is not the trust source.

## Resulting execution authorization

With G1, G3, and G4 now approved, the agent may begin the approved ALV-N026 PDF slices **S4, S5, S7, and S8**, subject to the existing pre-implementation briefing, focused-test policy, and handoff/review protocol.

S6 prescription-template content remains gated until the two wording-only corrections specified in `ALV-N026_R03_FINAL_DESIGN_REVIEW.md` are committed. No separate independent review is required for those exact wording changes.

No installer work is authorized; it remains ALV-N013 scope.
