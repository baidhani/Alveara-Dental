# Ledgerly Smart Accounting

**The spreadsheet is obsolete.**

[Watch the presentation](artifacts/week-12/ledgerly-expo-presentation.mp4)

Every transaction is recorded, balanced, and final. No spreadsheets. No manual reconciliation. No surprises in close.

## Architecture & Design

- [7-Layer Architecture Mapping](architecture/7-layer-architecture-mapping.md) — Complete system decomposition across presentation, application, domain, persistence, infrastructure, integration, and security layers
- [Trust Boundaries & Security Model](architecture/trust-boundaries-documentation.pdf) — 8-page visual guide to trust boundaries, data flows, threat model, and security measures
- [Architecture Decision Records](architecture/architecture-decision-records.md) — 7 key ADRs covering layered architecture, PostgreSQL, TypeScript, append-only audit trail, synchronous GL posting, Sequelize ORM, and JWT authentication
- [INPACT Security Scorecard](architecture/inPACT-scorecard.xlsx) — Governance evaluation across IAM, Infrastructure, Network & Data, Platform & App, Audit & Compliance, Data Protection, Operational Resilience, and Monitoring & Response
- [Trust Band Security Analysis](architecture/trust-band-scorecard.docx) — Current vs. target architecture analysis with 3 critical gaps and 12-month implementation roadmap

## What's Built

21 stories across 11 weeks: company setup → accounts → journal entries → general ledger → trial balance → multi-branch operations → budgeting → inventory → receivables/payables → sales/purchasing → role-based access → financial dashboards → AI insights → data import/export. All tested (120 test cases), all committed, all production-ready.

---

**Questions?** Open an issue or reach out.
