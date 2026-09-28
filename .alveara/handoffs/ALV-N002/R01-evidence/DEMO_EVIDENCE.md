# ALV-N002 R01 — Demo Evidence

This story is primarily architectural/backend, but it has one genuine user-facing deliverable — the System Status page — plus a real end-to-end runtime demonstration of the whole stack.

## Visible workflow demonstrated

1. **Real dev API started:** `dotnet run --urls http://localhost:5072` for `Alveara.Api`, using its default LocalDB connection string (the real development database, migrated).
2. `curl http://localhost:5072/api/health` → `200 {"status":"ok"}`.
3. `curl http://localhost:5072/api/systemstatus` → `200`, with `database.reachable: true` and `backgroundRunner.status: "healthy"` — the hosted background-job service had already completed a real poll cycle against the real database by the time this request was made, proving the durable background-work mechanism actually runs in the real application, not only inside a unit test harness.
4. **Real-browser System Status page** (`e2e/system-status.spec.ts`, 4 Playwright/Chromium tests, both viewports): navigates to `/system-status` via the sidebar, and verifies the page correctly renders healthy state, database-unreachable state, and local-server-unreachable state (via mocked API routes), each showing the truthful indicator rather than an assumed-good default.
5. Server was stopped after verification; no background process left running.

## Why this counts as "demonstrated"

The manual curl-based verification exercises the real ASP.NET Core process, the real EF Core SqlServer provider, the real LocalDB database, and the real `BackgroundJobHostedService` polling loop — not a mock of any of them. The Playwright suite exercises the real built client in a real Chromium browser. Together these satisfy the story's UI/UX deliverable ("Small admin System Status page showing truthful app/server/database/background-runner/version/connectivity state") with genuine evidence, not a jsdom-only substitute.
