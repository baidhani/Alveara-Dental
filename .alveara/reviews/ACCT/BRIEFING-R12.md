# ALV-N030-C02 Briefing R12 — Independent Review

**Decision:** APPROVED_FOR_STAGE_1_IMPLEMENTATION

**Reviewed package:** `Alveara_Briefing_ALV-N030-C02_1bd09e3_ADDENDUM-R12.zip`  
**Package SHA-256:** `AD7C42306AF8CEAEDF4D9EB43865F54045CECD2605CA73996DD76A7E610E7C5B`  
**Brief commit:** `1bd09e3b25af349fa3f909aaba2f9d1af1cacb58`  
**Scope reviewed:** section 19 only; sections 1–18 remain unchanged

## Integrity result

- All 17 files listed by `MANIFEST.json` are present and match their SHA-256 values.
- `SECRET_SCAN.md` reports no matches.
- The package contains briefing and reference material only; it does not present product code as already implemented.

## R11 corrections closed

### CR-1 — real endpoint and exact allowlist: CLOSED

R12 correctly uses `GET /api/auth/permissions`, returns `mustEnrollMfa` there, and freezes the authenticated enrollment-gate allowlist to exactly these five actions:

1. `GET /api/auth/permissions`
2. `GET /api/auth/csrf-token`
3. `POST /api/auth/mfa/enroll`
4. `POST /api/auth/mfa/confirm`
5. `POST /api/auth/logout`

S49 and S52 now exercise the real endpoint, the ordinary protected-route refusal, the enrollment flow, post-confirmation access, logout, anonymous-route behavior, audit behavior, and the exact action-attribute set.

### CR-2 — dependencies and returned-SVG proof: CLOSED

R12 freezes the dependency boundary:

- QRCoder `1.9.0`, MIT, product server project only;
- ZXing.Net `0.16.11`, Apache-2.0, test project only; and
- no SVG renderer or platform binding.

The test-only `QrSvgTestReader` is narrowly defined to parse the exact reviewed QRCoder SVG grammar, fail on unexpected content, construct pixels, and feed ZXing.Net's QR decoder. S42a separates URI/matrix proof from S42b's exact returned-SVG proof; S43 exercises the API-returned `qrSvg`. S53 covers restore graphs, vulnerabilities, licences, unexpected dependencies, and published-product contents.

### CR-3 — immutable Stage 1 transition: CLOSED

Section 19.4a now establishes one unambiguous sequence: Stage 1 implementation, automated proof, the owner's real-authenticator walkthrough on the same commit, one immutable privacy-safe handoff, independent Stage 1 approval, and only then Stage 2 implementation. Failed walkthroughs require correction and repetition on the new commit; an already reviewed package is not mutated.

## Decisions and boundaries confirmed

1. Existing Deployment Administrators are not retroactively forced to enroll. The future Stage 2 rule applies to Deployment Administrators newly created by setup and those created or reset through recovery.
2. ALV-N030-C02 remains one story with two controlled stages and remains incomplete between them.
3. Only the targeted MFA walkthrough is mandatory: scan with a real authenticator, confirm enrollment, sign out, and sign in with MFA. A general application walkthrough, broad regression, full chain, or unrelated manual testing is not required by C02.
4. The walkthrough record must contain only its date, authenticator application name, and pass/fail result. It must not contain the QR image, secret, provisioning URI, recovery code, sensitive screenshot, or phone identifier.

## Authorization

Implementation is authorized for **Stage 1 only**, comprising section 19's QR-provisioning work and its slices 1–3, focused automated evidence, dependency evidence, and targeted real-authenticator walkthrough.

This approval does **not** authorize:

- the `MustEnrollMfa` migration;
- setup/recovery enforcement flags;
- the server enrollment-required gate;
- the forced-enrollment client flow;
- any other Stage 2 implementation; or
- release certification.

After the Stage 1 implementation and proof are complete, submit one immutable Stage 1 handoff for independent review. Stage 2 may begin only after that handoff receives explicit independent approval.

## Handoff requirements

The Stage 1 handoff must map the final commit to S41–S48 and S53, include the dependency/restore and publish evidence, record the privacy-safe real-authenticator result, and disclose any deviation or newly introduced dependency. Any additional runtime or test dependency outside the frozen boundary requires review before use.

Broad regression remains deferred to ALV-N028 under the program regression ledger.
