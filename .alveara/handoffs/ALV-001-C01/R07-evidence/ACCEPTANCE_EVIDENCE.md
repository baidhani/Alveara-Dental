# ALV-001-C01 R07 - Acceptance Evidence

| Finding | Evidence |
|---|---|
| GATE-A-01: enabled-badge contrast 4.09:1 (light) | root cause: non-existent `--color-success-surface` token fell back to a literal tint; badge now `--color-text` on `--color-success-bg`; `adminUsersContrast.test.ts` (badge, both variants, both themes, both surfaces); browser scan 0 violations |
| GATE-A-01: "View" link contrast 1.77:1 (dark) | link colour was `--color-primary` (a dark-theme button background); now `--color-info`; test + browser scan |
| Preserve readable focus/hover and permission-gated behaviour | hover/keyboard-focus states scanned (0 violations); no markup/permission change; 12/12 real-backend flows |
| Revalidate shared badge uses | configuration and backup pages scanned with real data (0 violations); their component suites pass; `ALV-N003`/`ALV-N004` marked `REVALIDATION_REQUIRED` for review |
| No authentication/backend change | no backend file touched |

Original seven acceptance items: unchanged and re-asserted (authentication, MFA, recovery, session controls and authorization administration behaviour is covered by the unchanged backend suites and the real-backend browser run).
