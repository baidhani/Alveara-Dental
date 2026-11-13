# ALV-N020-C02 R01: next-story impact

- ALV-N021 (next executable item) is unaffected: no server, schema or shared-component change.
- ALV-N029 (rotation) and F-075 (ALV-N027) unaffected. Any future full-width control added inside the sign-in layout inherits the equal-width rule; a logo with a different aspect ratio fails `login-layout-polish.spec.ts` and `loginLogo.test.ts` instead of distorting.
- The shared `FormField` wrapper still caps at 360 px elsewhere; only the sign-in layout overrides it.
