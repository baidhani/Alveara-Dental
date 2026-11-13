# ALV-N020-C02 R01: acceptance evidence

Acceptance sentences of plan item 57b, in order:

| # | Acceptance | Evidence |
|---|---|---|
| 1 | Username, Password and Sign in one equal-width column at desktop, scoped to the login form only | `login-layout-polish.spec.ts` desktop test (all three within 1 px of each other (the button is 420 px; the fields were 360 before, NC1)); NC1; `FormField.css` unchanged (`CHANGED_FILES.md`) |
| 2 | Desktop logo spacing balanced without hiding, covering or pushing content/notice out of reach | same test: logo 72 px below the panel top, 40 px above the heading, Sign in and the footer unobscured (`elementFromPoint`); NC2; screenshots `screenshots/after/*-1280x800.png` |
| 3 | SVG bundled locally, via the existing `<img>` with the `DentalKeep` name; 1774x820 ratio undistorted/unclipped at ordinary, narrow, 200%-equivalent and forced-colours layouts | spec: `.svg` source on the same origin, loaded, `alt`, rendered ratio within 0.5%, inside the panel, `object-fit: contain`, no request leaving the origin, at 1280, 960x540, 640x400, 375 and forced colours; `loginLogo.test.ts` pins the hash; NC4; row 8 |
| 4 | Narrow and 200%-equivalent: spacing contracts, no horizontal scroll, form/error/footer reachable and unobscured | spec at 960x540, 640x400, 375x812 (contracted minimums, no scroll, nothing covered); C01's `login-presentation-access.spec.ts` (including the failure message at zoom) still 6/6 in the group run |
| 5 | Footer notice 12 px, readable line height, compliant contrast in light, dark, forced colours | spec: computed 12 px and smaller than the 13 px labels at every layout and in forced colours; axe (all rules) clean in light and dark; line-height 1.5; NC3 |
| 6 | No other wording, asset, route, permission, server, schema, migration, dependency, test infrastructure or shared-component change | `CHANGED_FILES.md`: six files, all under `src/alveara-client` (CSS, one component, the logo asset, two test files); no package or lock file |

Total 6 of 6.
