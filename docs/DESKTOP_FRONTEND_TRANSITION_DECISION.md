# Desktop Frontend Transition — Product Decision

**Decision date:** 2026-10-29  
**Status:** Deferred until the current non-AI release's deployment and operational-readiness phase is complete.

## Decision

After the current release is deployed and its deployment/readiness work is complete, Alveara will evaluate a Windows desktop frontend built with **C# and WPF**.

The existing web frontend remains the verified release frontend and reference implementation. This decision does not authorize a current-release frontend rewrite.

## Intended approach

- Retain the existing backend, API contracts, authorization model, audit controls, and domain/business rules as the shared source of truth.
- Treat the WPF client as a phased desktop-client program, rather than a parallel rewrite of backend behavior.
- Begin with high-value Windows workflows and desktop integrations after the release is stable.
- Consider a WPF + WebView2 shell as a migration option; native WPF views may replace web workflows incrementally after equivalent acceptance, accessibility, and evidence requirements are met.
- Do not retire a verified web workflow until its desktop equivalent has met the same product, security, audit, and accessibility obligations.

## Revisit trigger

Surface this decision when the deployment/readiness phase following Gate E is complete, before planning post-release product work.
