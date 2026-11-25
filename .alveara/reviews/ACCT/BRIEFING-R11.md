# ALV-N030-C02 Briefing R11 — Independent Review

**Decision:** CHANGES_REQUIRED

**Reviewed package:** `Alveara_Briefing_ALV-N030-C02_d76884a_ADDENDUM-R11.zip`  
**Package SHA-256:** `EF5F3343FAE6A10DAC097CDA8A9F107DD264ADE5DF6E475CEEDF1313D1BF02D2`  
**Brief commit:** `d76884ac6cb32fedb5d3bc9ae68ed38a0761647b`  
**Scope reviewed:** section 19 only; sections 1-18 remain unchanged

## Decisions confirmed

1. **R11-Q1 — existing Deployment Administrators:** yes. `MustEnrollMfa` defaults to false for every existing account. Enforcement applies when first-run setup newly creates the Deployment Administrator and whenever C01 recovery creates or resets it. An existing Deployment Administrator may enroll voluntarily and becomes mandatory only after a later recovery. This matches the owner's “newly created or recovered” scope.
2. **R11-Q2 — one story with two controlled stages:** yes. ALV-N030-C02 may remain one story with a Stage 1 QR-provisioning handoff and a later Stage 2 enforcement handoff. C02 remains incomplete between them. Stage 2 code is unauthorized until Stage 1 automated proof, the required real-authenticator walkthrough, and independent Stage 1 approval are all complete.
3. **Dependency choices:** QRCoder in the server and ZXing.Net in tests only are acceptable in principle. QRCoder's official NuGet package is MIT-licensed; ZXing.Net's is Apache-2.0. The exact versions and complete rendering/decoding path must be frozen by CR-2 before implementation.
4. **Manual-proof boundary:** only the targeted MFA walkthrough is mandatory. No general application walkthrough, broad regression, full chain, or unrelated manual testing is added by C02.

## What is accepted in R11

- The server constructs a standards-compatible, locally generated `otpauth://` payload with a DentalKeep issuer and an encoded account label.
- The API remains additive and `no-store`; no online QR service, browser storage, log, or audit receives the secret, provisioning URI, or SVG.
- Manual-key fallback, grouping, Copy success/failure feedback, pending-factor semantics, replacement step-up, existing recovery codes, and sign-in MFA behavior are preserved.
- Stage 2 adds a server-side gate rather than relying on client navigation.
- Setup and recovery set the flag inside their existing transactions; successful confirmation clears it atomically while activating the factor and reissuing the session.
- Existing accounts are not retroactively forced by the migration.
- The real phone-authenticator scan, confirmation, sign-out and sign-in proof remains a hard gate before enforcement.

## Required corrections

### CR-1 — use the real identity endpoint and freeze the exact gate allowlist

Section 19.5 names `GET /api/auth/me`, but the current product has no such route. The signed-in identity call is `GET /api/auth/permissions`, and the existing password-change gate already marks that exact action as allowed.

Revise section 19 everywhere to:

- return `mustEnrollMfa` from `GET /api/auth/permissions`;
- mark that exact action with `AllowedWhileMfaEnrollmentRequired`;
- freeze the authenticated allowlist as exactly:
  - `GET /api/auth/permissions`;
  - `GET /api/auth/csrf-token`;
  - `POST /api/auth/mfa/enroll`;
  - `POST /api/auth/mfa/confirm`;
  - `POST /api/auth/logout`;
- state that anonymous endpoints remain governed by their existing metadata and are not made privileged by this gate; and
- add proof that a freshly created or recovered, gated Deployment Administrator can call `/api/auth/permissions`, receives `mustEnrollMfa: true`, obtains a CSRF token, enrolls and confirms, while an ordinary protected route is refused.

S49/S52 must exercise the real endpoint. A route-name alias or a new `/me` route is not requested.

### CR-2 — pin the dependency versions and define the returned-SVG decode path

The brief authorizes dependencies by name but does not freeze versions. It also says the returned SVG is rasterized and decoded with ZXing.Net without naming how an SVG becomes pixels. ZXing.Net core does not itself render SVG; an undeclared renderer or binding would silently expand the dependency boundary.

Revise section 19.1/19.4 to:

- pin the exact reviewed stable versions before code (current official stable versions at review time are QRCoder `1.9.0` and ZXing.Net `0.16.11`);
- state the exact NuGet packages, which project receives each reference, and all transitive/runtime assets;
- identify the precise mechanism that rasterizes **the actual API-returned SVG** for S42/S43, including any binding or renderer package and its pinned version/licence; or redesign the proof so no unapproved renderer is needed while the real returned SVG is still proven scan-capable;
- distinguish a test of the URI/matrix from a test of the exact SVG sent to the client; and
- run and retain `dotnet list package --vulnerable --include-transitive` for both the product and test restore graphs, plus licence/notice evidence.

The existing SkiaSharp product reference alone is not an SVG parser. No third package may appear during implementation without being disclosed and reviewed.

### CR-3 — make the Stage 1 evidence transition immutable and reviewable

Section 19 says “Stage 1 handoff, then walkthrough, then review,” while also requiring the Stage 1 evidence to record the phone app and result. Freeze one unambiguous sequence:

1. implement Stage 1 only;
2. pass S41-S48 and the focused static/dependency checks;
3. perform the short owner walkthrough with a real authenticator;
4. build an immutable Stage 1 handoff containing the automated evidence and a privacy-safe walkthrough record (date, authenticator app name, pass/fail; no QR, secret, provisioning URI, recovery code, screenshot containing them, or phone identifier);
5. obtain independent Stage 1 approval; and only then
6. begin Stage 2 implementation.

If the workflow instead needs a code-review package before the walkthrough, define a separate immutable proof addendum and require its independent acceptance before Stage 2. Do not mutate an already reviewed package.

## R12 route

Submit a records-only R12 briefing package that changes section 19 and its review request only. No product code, package reference, migration, test, or configuration change is authorized before R12 approval.

R12 should map CR-1 through CR-3 to exact revised paragraphs and test rows. The accepted owner decisions, C01 boundary, and requirement that only the MFA walkthrough is mandatory remain unchanged.
